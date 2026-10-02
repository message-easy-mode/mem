using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Commands;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Migrations.Cutover;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Performs the target-host half of coordinated pre-acceptance rollback. The service restores
/// the exact pre-cutover NPM route snapshots, removes normal MEM public route ownership, stops
/// the migrated target containers, preserves all target data and runtime records, and emits a
/// portable source-restoration handoff. It never controls the legacy source host directly.
/// </summary>
public sealed class MigrationProductionRollbackService(
    MemDbContext db,
    MigrationCutoverContextResolver contextResolver,
    MigrationProductionAdoptionService adoptionService,
    NpmProxyHostService npmProxyHostService,
    RuntimeStackManifestStore manifestStore,
    DockerClient docker,
    ILogger<MigrationProductionRollbackService> logger,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    internal const string SourceHandoffSchemaVersion =
        "mem.migration.source-restoration-handoff.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MigrationProductionAdoptionStateResponse> PreparePreviewAsync(
        string migrationId,
        PrepareMigrationProductionRollbackRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "A completed controlled public cutover is required before pre-acceptance rollback preview.");

        if (string.Equals(plan.RollbackStatus, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffJson))
        {
            return await adoptionService.GetStateAsync(migrationId, ct);
        }

        ValidateRollbackEligible(plan);
        ResetFailedExecutionForFreshPreview(plan);
        await EnsureNotAcceptedAsync(plan, ct);
        var context = await contextResolver.ResolveAsync(migrationId, ct);
        ValidateFrozenSourceAuthority(context);
        ValidatePlanAuthority(plan, context);
        var cutoverCheckpoint = DeserializeCutoverRollbackCheckpoint(
            plan.CutoverRollbackCheckpointJson);
        ValidateCutoverRollbackCheckpoint(
            migrationId,
            plan,
            context,
            cutoverCheckpoint);

        var runtime = await LoadRuntimeAsync(plan, tracking: false, ct);
        var runtimeRoutes = runtime.Stack.Routes.ToArray();
        var runtimeRouteCheckpoints = runtimeRoutes
            .Select(ToRuntimeRouteCheckpoint)
            .ToArray();
        var blockers = new List<string>();

        ValidateRuntimeRouteOwnership(plan, runtimeRoutes, blockers);
        var targetRoutes = new List<TargetRouteSnapshot>();
        var publicRoutes = new List<MigrationProductionRollbackRouteState>();

        foreach (var checkpoint in cutoverCheckpoint.RouteCheckpoints)
        {
            var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
            var expectedRouteId = ResolveTargetRouteId(plan, checkpoint.ServiceKey);
            var matchesTarget = TargetRouteMatches(checkpoint, expectedRouteId, current);
            if (!matchesTarget)
            {
                blockers.Add(
                    $"NPM route '{checkpoint.PublicHost}' no longer matches the exact migrated target selected by the completed cutover.");
            }

            var snapshot = current is null ? null : NpmProxyHostService.CaptureSnapshot(current);
            targetRoutes.Add(new TargetRouteSnapshot(
                checkpoint.ServiceKey,
                checkpoint.PublicHost,
                expectedRouteId,
                snapshot));
            publicRoutes.Add(ToPublicRouteState(checkpoint, snapshot, matchesTarget));
        }

        var containers = await CaptureTargetContainersAsync(plan, requireRunning: true, blockers, ct);
        var now = DateTime.UtcNow;
        var previewId = CreateId("mprv", now);
        var previewSha256 = ComputePreviewSha256(
            plan.PlanSha256,
            plan.CutoverExecutionId!,
            targetRoutes,
            cutoverCheckpoint.RouteCheckpoints,
            runtimeRouteCheckpoints,
            containers);
        var status = blockers.Count == 0 ? "ready" : "blocked";

        plan.RollbackPreviewId = previewId;
        plan.RollbackPreviewStatus = status;
        plan.RollbackPreviewCreatedAtUtc = now;
        plan.RollbackPreviewExpiresAtUtc = null;
        plan.RollbackPreviewSha256 = previewSha256;
        plan.RollbackPreviewJson = JsonSerializer.Serialize(new RollbackPreviewEvidence(
            plan.AdoptionPlanId,
            plan.PlanSha256,
            plan.CutoverExecutionId!,
            context.Capture.SourceMigrationId,
            targetRoutes,
            cutoverCheckpoint.RouteCheckpoints,
            runtimeRouteCheckpoints,
            containers,
            publicRoutes,
            blockers), JsonOptions);
        plan.RollbackStatus = blockers.Count == 0
            ? "preview-ready"
            : "preview-blocked";
        plan.RollbackFailureCode = null;
        plan.RollbackFailureSummary = null;
        plan.BlockerSummary = blockers.Count == 0 ? null : string.Join("; ", blockers);
        plan.Status = blockers.Count == 0
            ? "rollback-preview-ready"
            : "public-awaiting-verification";
        plan.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return await adoptionService.GetStateAsync(migrationId, ct);
    }

    public async Task<MigrationProductionAdoptionStateResponse> ExecuteAsync(
        string migrationId,
        ExecuteMigrationProductionRollbackRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAcknowledgements(request);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "A completed controlled public cutover is required before pre-acceptance rollback.");

        if (string.Equals(plan.RollbackStatus, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffJson))
        {
            return await adoptionService.GetStateAsync(migrationId, ct);
        }

        ValidateRollbackExecutionState(plan);
        ValidatePreview(plan, request.PreviewId);
        await EnsureNotAcceptedAsync(plan, ct);
        var context = await contextResolver.ResolveAsync(migrationId, ct);
        ValidateFrozenSourceAuthority(context);
        ValidatePlanAuthority(plan, context);
        var preview = DeserializeRollbackPreview(plan.RollbackPreviewJson);
        ValidateRollbackPreviewEvidence(plan, context, preview);
        if (preview.Blockers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Pre-acceptance target rollback remains blocked: {string.Join("; ", preview.Blockers)}");
        }

        var executionId = string.IsNullOrWhiteSpace(plan.RollbackExecutionId)
            ? CreateId("mpre", DateTime.UtcNow)
            : plan.RollbackExecutionId;
        var startedAtUtc = plan.RollbackStartedAtUtc ?? DateTime.UtcNow;
        plan.RollbackExecutionId = executionId;
        plan.RollbackStatus = "executing";
        plan.RollbackStartedAtUtc = startedAtUtc;
        plan.RollbackCompletedAtUtc = null;
        plan.RollbackFailureCode = null;
        plan.RollbackFailureSummary = null;
        plan.Status = "rollback-executing";
        plan.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await MigrationProductionDiagnosticEvents.RecordAsync(
            diagnostics,
            migrationId,
            eventCode: "migration.production.rollback.started",
            severity: MemDiagnosticSeverities.Information,
            stage: "production-rollback",
            message: "Migration target rollback started.",
            targetStackSlug: plan.TargetStackSlug,
            service: "synapse",
            details: new Dictionary<string, string?>
            {
                ["adoptionPlanId"] = plan.AdoptionPlanId,
                ["executionId"] = executionId,
                ["previewId"] = request.PreviewId
            });

        try
        {
            if (!plan.RollbackRoutesRestored)
            {
                if (!await ArePreCutoverRoutesRestoredAsync(preview.RestoreCheckpoints, ct))
                {
                    await EnsureRoutesRollbackResumableAsync(
                        preview.TargetRoutes,
                        preview.RestoreCheckpoints,
                        ct);
                    await RestorePreCutoverRoutesWithCompensationAsync(
                        plan,
                        preview.TargetRoutes,
                        preview.RestoreCheckpoints,
                        ct);
                }

                plan.RollbackRoutesRestored = true;
                plan.PublicRoutesCreated = false;
                plan.RuntimePromotionCompleted = false;
                plan.RollbackTargetRouteCompensationAttempted = false;
                plan.RollbackTargetRouteCompensationCompleted = false;
                plan.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                await EnsurePreCutoverRoutesRestoredAsync(preview.RestoreCheckpoints, ct);
            }

            if (!plan.RollbackRuntimeRoutesRemoved)
            {
                var runtime = await LoadRuntimeAsync(plan, tracking: false, ct);
                await manifestStore.SaveAsync(
                    plan.TargetStackSlug,
                    BuildRouteFreeRuntimeResult(
                        plan,
                        runtime,
                        executionId,
                        containersStopped: false),
                    ct);

                if (await db.RuntimeRoutes.AsNoTracking()
                    .AnyAsync(x => x.RuntimeStackId == plan.RuntimeStackId, ct))
                {
                    throw new InvalidOperationException(
                        "Normal MEM RuntimeRoute ownership remained after restoring the pre-cutover route snapshot.");
                }

                plan.RollbackRuntimeRoutesRemoved = true;
                plan.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            else if (await db.RuntimeRoutes.AsNoTracking()
                .AnyAsync(x => x.RuntimeStackId == plan.RuntimeStackId, ct))
            {
                throw new InvalidOperationException(
                    "Normal MEM RuntimeRoute ownership reappeared after target rollback.");
            }

            if (!plan.RollbackTargetContainersStopped)
            {
                await StopTargetContainersAsync(plan, ct);
                var stoppedRuntime = await LoadRuntimeAsync(plan, tracking: false, ct);
                await manifestStore.SaveAsync(
                    plan.TargetStackSlug,
                    BuildRouteFreeRuntimeResult(
                        plan,
                        stoppedRuntime,
                        executionId,
                        containersStopped: true),
                    ct);
                await EnsureTargetContainersStoppedAsync(plan, ct);

                plan.RollbackTargetContainersStopped = true;
                plan.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            else
            {
                await EnsureTargetContainersStoppedAsync(plan, ct);
            }

            var handoff = await BuildSourceHandoffAsync(
                plan,
                context,
                preview.RestoreCheckpoints,
                executionId,
                ct);
            var completedAtUtc = DateTime.UtcNow;
            plan.RollbackSourceHandoffId = handoff.Payload.HandoffId;
            plan.RollbackSourceHandoffSha256 = handoff.PayloadSha256;
            plan.RollbackSourceHandoffJson = JsonSerializer.Serialize(handoff, JsonOptions);
            plan.RollbackEvidenceJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                executionId,
                plan.CutoverExecutionId,
                plan.RollbackRoutesRestored,
                plan.RollbackRuntimeRoutesRemoved,
                plan.RollbackTargetContainersStopped,
                sourceHandoffId = handoff.Payload.HandoffId,
                sourceHandoffSha256 = handoff.PayloadSha256,
                request.Operator,
                request.Note,
                completedAtUtc,
            }, JsonOptions);
            plan.RollbackStatus = "target-rolled-back-awaiting-source";
            plan.RollbackCompletedAtUtc = completedAtUtc;
            plan.CutoverStatus = "target-rolled-back-awaiting-source";
            plan.Status = "target-rolled-back-awaiting-source";
            plan.BlockerSummary = null;
            plan.UpdatedAtUtc = completedAtUtc;
            await db.SaveChangesAsync(ct);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.rollback.completed",
                severity: MemDiagnosticSeverities.Information,
                stage: "production-rollback",
                message: "Migration target rollback completed and now awaits source restoration.",
                targetStackSlug: plan.TargetStackSlug,
                service: "synapse",
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.RollbackStatus,
                    ["routesRestored"] = plan.RollbackRoutesRestored ? "true" : "false",
                    ["targetContainersStopped"] = plan.RollbackTargetContainersStopped ? "true" : "false"
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["executionId"] = executionId
                });

            return await adoptionService.GetStateAsync(migrationId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = ClassifyFailure(ex);
            logger.LogError(
                ex,
                "Migration target rollback failed. MigrationId={MigrationId} AdoptionPlanId={AdoptionPlanId} RollbackExecutionId={RollbackExecutionId}",
                migrationId,
                plan.AdoptionPlanId,
                executionId);

            var failedAtUtc = DateTime.UtcNow;
            plan.RollbackStatus = "failed";
            plan.RollbackFailureCode = failure.Code;
            plan.RollbackFailureSummary = failure.Summary;
            plan.BlockerSummary = failure.Summary;
            plan.Status = "rollback-failed";
            plan.UpdatedAtUtc = failedAtUtc;
            plan.RollbackEvidenceJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                executionId,
                failure.Code,
                failure.Summary,
                plan.RollbackRoutesRestored,
                plan.RollbackRuntimeRoutesRemoved,
                plan.RollbackTargetContainersStopped,
                plan.RollbackTargetRouteCompensationAttempted,
                plan.RollbackTargetRouteCompensationCompleted,
                failedAtUtc,
            }, JsonOptions);
            await db.SaveChangesAsync(CancellationToken.None);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.rollback.failed",
                severity: MemDiagnosticSeverities.Error,
                stage: "production-rollback",
                message: "Migration target rollback failed.",
                targetStackSlug: plan.TargetStackSlug,
                createIncident: true,
                exception: ex,
                service: "synapse",
                expected: new Dictionary<string, string?>
                {
                    ["status"] = "target-rolled-back-awaiting-source"
                },
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.RollbackStatus,
                    ["failureCode"] = failure.Code,
                    ["routesRestored"] = plan.RollbackRoutesRestored ? "true" : "false",
                    ["targetContainersStopped"] = plan.RollbackTargetContainersStopped ? "true" : "false"
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["executionId"] = executionId,
                    ["failureSummary"] = failure.Summary
                },
                retryable: false,
                suggestedAction: "Open the Migration Workspace and review target rollback recovery evidence before making further changes.");
            throw new InvalidOperationException(failure.Summary);
        }
    }

    public async Task<MigrationProductionSourceRestorationHandoffEnvelope> GetSourceHandoffAsync(
        string migrationId,
        CancellationToken ct)
    {
        var plan = await LoadPlanAsync(migrationId, tracking: false, ct)
            ?? throw new FileNotFoundException(
                $"Migration Session '{migrationId}' has no production adoption plan.");

        if (!string.Equals(plan.RollbackStatus, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffJson) ||
            string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffSha256))
        {
            throw new InvalidOperationException(
                "The target rollback has not completed and no source-restoration handoff is available.");
        }

        var envelope = JsonSerializer.Deserialize<MigrationProductionSourceRestorationHandoffEnvelope>(
            plan.RollbackSourceHandoffJson,
            JsonOptions)
            ?? throw new InvalidDataException("The durable source-restoration handoff is empty.");
        var actualSha256 = ComputePayloadSha256(envelope.Payload);
        if (!string.Equals(actualSha256, envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualSha256, plan.RollbackSourceHandoffSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The durable source-restoration handoff hash does not match its payload.");
        }

        return envelope;
    }

    internal static IReadOnlyList<string> GetMissingAcknowledgements(
        ExecuteMigrationProductionRollbackRequest request)
    {
        var missing = new List<string>();
        if (!request.ExecuteTargetRollback) missing.Add("executeTargetRollback");
        if (!request.AcknowledgeMigrationNotAccepted) missing.Add("acknowledgeMigrationNotAccepted");
        if (!request.AcknowledgeRestoresPreCutoverRoutes) missing.Add("acknowledgeRestoresPreCutoverRoutes");
        if (!request.AcknowledgeStopsTargetContainers) missing.Add("acknowledgeStopsTargetContainers");
        if (!request.AcknowledgePreservesTargetData) missing.Add("acknowledgePreservesTargetData");
        if (!request.AcknowledgeSourceRemainsFrozen) missing.Add("acknowledgeSourceRemainsFrozen");
        if (!request.AcknowledgeSourceRestorationRequiresHandoff) missing.Add("acknowledgeSourceRestorationRequiresHandoff");
        if (!request.AcknowledgeNoAutomaticSourceHostMutation) missing.Add("acknowledgeNoAutomaticSourceHostMutation");
        return missing;
    }

    internal static bool TargetRouteMatches(
        MigrationProductionCutoverService.RouteCheckpoint checkpoint,
        int expectedRouteId,
        NpmProxyHost? current) =>
        current is not null &&
        current.id == expectedRouteId &&
        string.Equals(current.forward_host?.Trim(), checkpoint.DesiredForwardHost.Trim(), StringComparison.OrdinalIgnoreCase) &&
        current.forward_port == checkpoint.DesiredForwardPort &&
        string.Equals(NormalizeScheme(current.forward_scheme), "http", StringComparison.OrdinalIgnoreCase) &&
        (current.enabled ?? true);

    internal static string ComputePayloadSha256(
        MigrationProductionSourceRestorationHandoffPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Sha256(json);
    }

    private static void ValidateAcknowledgements(ExecuteMigrationProductionRollbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewId))
        {
            throw new InvalidOperationException(
                "A current server-generated target rollback preview id is required.");
        }

        var missing = GetMissingAcknowledgements(request);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required acknowledgements: {string.Join(", ", missing)}");
        }
    }

    private static void ValidateRollbackEligible(MigrationProductionAdoptionEntity plan)
    {
        var stateAllowsPreview =
            string.Equals(plan.Status, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "production-verification-running", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "production-verification-passed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "production-verification-failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "rollback-preview-ready", StringComparison.OrdinalIgnoreCase) ||
            CanPrepareFreshPreviewAfterFailure(plan);

        if (!stateAllowsPreview ||
            !string.Equals(plan.CutoverStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) ||
            !plan.PublicRoutesCreated ||
            !plan.RuntimePromotionCompleted ||
            string.IsNullOrWhiteSpace(plan.CutoverExecutionId) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewJson) ||
            string.IsNullOrWhiteSpace(plan.CutoverRollbackCheckpointJson) ||
            string.IsNullOrWhiteSpace(plan.MatrixNpmRouteId) ||
            string.IsNullOrWhiteSpace(plan.ElementNpmRouteId))
        {
            throw new InvalidOperationException(
                "Target rollback requires a completed, unaccepted controlled public cutover with durable route checkpoints.");
        }
    }

    private static bool CanPrepareFreshPreviewAfterFailure(
        MigrationProductionAdoptionEntity plan) =>
        string.Equals(plan.Status, "rollback-failed", StringComparison.OrdinalIgnoreCase) &&
        !plan.RollbackRoutesRestored &&
        !plan.RollbackRuntimeRoutesRemoved &&
        !plan.RollbackTargetContainersStopped &&
        (!plan.RollbackTargetRouteCompensationAttempted ||
         plan.RollbackTargetRouteCompensationCompleted);

    private static void ResetFailedExecutionForFreshPreview(
        MigrationProductionAdoptionEntity plan)
    {
        if (!CanPrepareFreshPreviewAfterFailure(plan))
        {
            return;
        }

        plan.RollbackExecutionId = null;
        plan.RollbackStartedAtUtc = null;
        plan.RollbackCompletedAtUtc = null;
        plan.RollbackEvidenceJson = null;
        plan.RollbackTargetRouteCompensationAttempted = false;
        plan.RollbackTargetRouteCompensationCompleted = false;
        plan.RollbackSourceHandoffId = null;
        plan.RollbackSourceHandoffSha256 = null;
        plan.RollbackSourceHandoffJson = null;
    }

    private static void ValidateRollbackExecutionState(MigrationProductionAdoptionEntity plan)
    {
        var allowed = string.Equals(plan.Status, "rollback-preview-ready", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "rollback-executing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "rollback-failed", StringComparison.OrdinalIgnoreCase);
        if (!allowed || string.IsNullOrWhiteSpace(plan.CutoverExecutionId))
        {
            throw new InvalidOperationException(
                "Prepare a current blocker-free target rollback preview before execution.");
        }
    }

    private static void ValidatePreview(MigrationProductionAdoptionEntity plan, string previewId)
    {
        if (!string.Equals(plan.RollbackPreviewStatus, "ready", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(plan.RollbackPreviewId) ||
            !string.Equals(plan.RollbackPreviewId, previewId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plan.RollbackPreviewJson) ||
            string.IsNullOrWhiteSpace(plan.RollbackPreviewSha256))
        {
            throw new InvalidOperationException(
                "The target rollback preview is missing, superseded, or not ready. Prepare a fresh preview.");
        }
    }

    private async Task EnsureNotAcceptedAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        if (await db.MigrationAcceptances.AsNoTracking()
            .AnyAsync(x => x.MigrationIntakeEntityId == plan.MigrationIntakeEntityId, ct))
        {
            throw new InvalidOperationException(
                "Pre-acceptance rollback is permanently unavailable after migration acceptance.");
        }
    }

    private static void ValidateFrozenSourceAuthority(MigrationCutoverContext context)
    {
        if (!context.Capture.FinalCutoverEligible || !context.Capture.SourceFrozen)
        {
            throw new InvalidOperationException(
                "Target rollback requires the same authoritative final frozen-source context used for public cutover.");
        }
    }

    private static void ValidatePlanAuthority(
        MigrationProductionAdoptionEntity plan,
        MigrationCutoverContext context)
    {
        if (plan.MigrationPackageRevisionEntityId != context.PackageRevision.Id ||
            plan.MigrationCandidateArtifactEntityId != context.CandidateArtifact.Id ||
            plan.MigrationStagingRunEntityId != context.StagingRun.Id ||
            !string.Equals(
                plan.MatrixServerName,
                context.Capture.MatrixServerName,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(context.PackageRevision.DecryptedArchiveSha256))
        {
            throw new InvalidDataException(
                "The production adoption plan no longer matches the authoritative final package, candidate, staging runtime, and Matrix identity.");
        }
    }

    private static void ValidateCutoverRollbackCheckpoint(
        string migrationId,
        MigrationProductionAdoptionEntity plan,
        MigrationCutoverContext context,
        CutoverRollbackCheckpointEvidence checkpoint)
    {
        if (checkpoint.TargetRoutes is null ||
            checkpoint.RouteCheckpoints is null ||
            !string.Equals(checkpoint.MigrationId, migrationId, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.AdoptionPlanId, plan.AdoptionPlanId, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.ExecutionId, plan.CutoverExecutionId, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.PlanSha256, plan.PlanSha256, StringComparison.OrdinalIgnoreCase) ||
            !checkpoint.SourceFrozen ||
            !string.Equals(
                checkpoint.SourceMigrationId,
                context.Capture.SourceMigrationId,
                StringComparison.Ordinal) ||
            !string.Equals(checkpoint.PreviewId, plan.CutoverPreviewId, StringComparison.Ordinal) ||
            !string.Equals(
                checkpoint.PreviewSha256,
                plan.CutoverPreviewSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                checkpoint.TargetRoutes.MatrixRouteId,
                plan.MatrixNpmRouteId,
                StringComparison.Ordinal) ||
            !string.Equals(
                checkpoint.TargetRoutes.ElementRouteId,
                plan.ElementNpmRouteId,
                StringComparison.Ordinal) ||
            checkpoint.RouteCheckpoints.Count != 2 ||
            checkpoint.RouteCheckpoints.Count(x =>
                string.Equals(x.ServiceKey, ServiceKeys.Matrix, StringComparison.OrdinalIgnoreCase)) != 1 ||
            checkpoint.RouteCheckpoints.Count(x =>
                string.Equals(x.ServiceKey, ServiceKeys.ElementWeb, StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new InvalidDataException(
                "The durable cutover rollback checkpoint does not match the completed controlled public cutover authority.");
        }

        foreach (var route in checkpoint.RouteCheckpoints)
        {
            if (route.ExistingRouteFound && route.ExistingSnapshot is null)
            {
                throw new InvalidDataException(
                    $"Cutover checkpoint for '{route.PublicHost}' lacks the exact NPM snapshot required for coordinated rollback.");
            }
        }
    }

    private static void ValidateRollbackPreviewEvidence(
        MigrationProductionAdoptionEntity plan,
        MigrationCutoverContext context,
        RollbackPreviewEvidence preview)
    {
        if (preview.TargetRoutes is null ||
            preview.RestoreCheckpoints is null ||
            preview.RuntimeRoutes is null ||
            preview.TargetContainers is null ||
            preview.Blockers is null)
        {
            throw new InvalidDataException(
                "The durable target rollback preview is missing required evidence collections.");
        }

        var expectedSha256 = ComputePreviewSha256(
            plan.PlanSha256,
            plan.CutoverExecutionId!,
            preview.TargetRoutes,
            preview.RestoreCheckpoints,
            preview.RuntimeRoutes,
            preview.TargetContainers);

        if (!string.Equals(preview.AdoptionPlanId, plan.AdoptionPlanId, StringComparison.Ordinal) ||
            !string.Equals(preview.PlanSha256, plan.PlanSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(preview.CutoverExecutionId, plan.CutoverExecutionId, StringComparison.Ordinal) ||
            !string.Equals(
                preview.SourceMigrationId,
                context.Capture.SourceMigrationId,
                StringComparison.Ordinal) ||
            !string.Equals(
                expectedSha256,
                plan.RollbackPreviewSha256,
                StringComparison.OrdinalIgnoreCase) ||
            preview.TargetRoutes.Count != 2 ||
            preview.RestoreCheckpoints.Count != 2 ||
            preview.RuntimeRoutes.Count != 2 ||
            preview.TargetContainers.Count != 2)
        {
            throw new InvalidDataException(
                "The durable target rollback preview does not match its plan, cutover, source, or content hash.");
        }

        foreach (var route in preview.TargetRoutes)
        {
            if (route.ExpectedTargetRouteId != ResolveTargetRouteId(plan, route.ServiceKey) ||
                route.Snapshot is null ||
                route.Snapshot.Id != route.ExpectedTargetRouteId)
            {
                throw new InvalidDataException(
                    $"Target rollback preview route evidence for '{route.ServiceKey}' is invalid.");
            }
        }
    }

    private static void ValidateRuntimeRouteOwnership(
        MigrationProductionAdoptionEntity plan,
        IReadOnlyList<RuntimeRouteEntity> routes,
        List<string> blockers)
    {
        var matrix = routes.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix);
        var element = routes.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb);
        if (routes.Count != 2 || matrix is null || element is null)
        {
            blockers.Add(
                "The migrated Runtime Stack does not own exactly one Matrix and one Element public route.");
            return;
        }

        if (!string.Equals(matrix.ProviderRouteId, plan.MatrixNpmRouteId, StringComparison.Ordinal) ||
            !string.Equals(element.ProviderRouteId, plan.ElementNpmRouteId, StringComparison.Ordinal))
        {
            blockers.Add(
                "Normal MEM RuntimeRoute ownership no longer matches the completed migration cutover route ids.");
        }
    }

    private async Task<IReadOnlyList<TargetContainerSnapshot>> CaptureTargetContainersAsync(
        MigrationProductionAdoptionEntity plan,
        bool requireRunning,
        List<string>? blockers,
        CancellationToken ct)
    {
        var results = new List<TargetContainerSnapshot>();
        foreach (var item in new[]
        {
            (ServiceKeys.Matrix, plan.MatrixContainerName),
            (ServiceKeys.ElementWeb, plan.ElementContainerName),
        })
        {
            try
            {
                var inspect = await docker.Containers.InspectContainerAsync(item.Item2, ct);
                var running = inspect.State?.Running is true;
                if (requireRunning && !running)
                {
                    blockers?.Add($"Target container '{item.Item2}' is not running before rollback.");
                }
                if ((inspect.HostConfig?.PortBindings?.Count ?? 0) > 0)
                {
                    blockers?.Add($"Target container '{item.Item2}' unexpectedly publishes a host port.");
                }

                results.Add(new TargetContainerSnapshot(
                    item.Item1,
                    item.Item2,
                    inspect.ID,
                    running));
            }
            catch (DockerApiException)
            {
                blockers?.Add($"Target container '{item.Item2}' was not found.");
                results.Add(new TargetContainerSnapshot(item.Item1, item.Item2, null, false));
            }
        }

        return results;
    }

    private static MigrationProductionRollbackRouteState ToPublicRouteState(
        MigrationProductionCutoverService.RouteCheckpoint checkpoint,
        NpmProxyHostSnapshot? snapshot,
        bool matchesTarget) =>
        new(
            checkpoint.ServiceKey,
            checkpoint.PublicHost,
            snapshot?.Id,
            snapshot?.ForwardScheme,
            snapshot?.ForwardHost,
            snapshot?.ForwardPort,
            snapshot?.Enabled,
            snapshot is null ? Sha256("absent") : SnapshotSha256(snapshot),
            matchesTarget,
            checkpoint.ExistingRouteFound ? "restore-pre-cutover-route" : "remove-target-route");

    private async Task<bool> ArePreCutoverRoutesRestoredAsync(
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> checkpoints,
        CancellationToken ct)
    {
        foreach (var checkpoint in checkpoints)
        {
            var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
            if (!MigrationProductionCutoverService.RouteCheckpointMatches(checkpoint, current))
            {
                return false;
            }
        }

        return true;
    }

    private async Task EnsureRoutesRollbackResumableAsync(
        IReadOnlyList<TargetRouteSnapshot> targetRoutes,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> checkpoints,
        CancellationToken ct)
    {
        foreach (var checkpoint in checkpoints)
        {
            var target = targetRoutes.Single(x =>
                string.Equals(x.ServiceKey, checkpoint.ServiceKey, StringComparison.OrdinalIgnoreCase));
            var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
            var matchesTarget = target.Snapshot is not null &&
                NpmProxyHostService.SnapshotMatches(target.Snapshot, current, requireSameId: true);
            var matchesRestored =
                MigrationProductionCutoverService.RouteCheckpointMatches(checkpoint, current);

            if (!matchesTarget && !matchesRestored)
            {
                throw new InvalidOperationException(
                    $"NPM route '{checkpoint.PublicHost}' changed outside the rollback workflow. " +
                    "Neither the captured migrated target nor the exact pre-cutover checkpoint is present.");
            }
        }
    }

    private async Task RestorePreCutoverRoutesWithCompensationAsync(
        MigrationProductionAdoptionEntity plan,
        IReadOnlyList<TargetRouteSnapshot> targetRoutes,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> checkpoints,
        CancellationToken ct)
    {
        var mutationStarted = false;
        try
        {
            foreach (var checkpoint in checkpoints.Reverse())
            {
                var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
                if (MigrationProductionCutoverService.RouteCheckpointMatches(checkpoint, current))
                {
                    continue;
                }

                mutationStarted = true;
                if (!checkpoint.ExistingRouteFound)
                {
                    var target = targetRoutes.Single(x =>
                        string.Equals(x.ServiceKey, checkpoint.ServiceKey, StringComparison.OrdinalIgnoreCase));
                    if (target.Snapshot is not null)
                    {
                        await npmProxyHostService.DeleteByIdIfExistsAsync(target.Snapshot.Id, ct);
                    }
                    else
                    {
                        await npmProxyHostService.DeleteByDomainIfExistsAsync(checkpoint.PublicHost, ct);
                    }
                    continue;
                }

                if (checkpoint.ExistingSnapshot is null)
                {
                    throw new InvalidDataException(
                        $"Cutover checkpoint for '{checkpoint.PublicHost}' lacks the exact pre-cutover NPM snapshot required for safe rollback.");
                }

                await npmProxyHostService.RestoreSnapshotAsync(
                    checkpoint.ExistingSnapshot,
                    checkpoint.PublicHost,
                    ct);
            }

            await EnsurePreCutoverRoutesRestoredAsync(checkpoints, ct);
        }
        catch
        {
            if (mutationStarted)
            {
                plan.RollbackTargetRouteCompensationAttempted = true;
                plan.RollbackTargetRouteCompensationCompleted =
                    await TryRestoreTargetRouteSnapshotsAsync(targetRoutes);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            throw;
        }
    }

    private async Task EnsurePreCutoverRoutesRestoredAsync(
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> checkpoints,
        CancellationToken ct)
    {
        foreach (var checkpoint in checkpoints)
        {
            var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
            if (!MigrationProductionCutoverService.RouteCheckpointMatches(checkpoint, current))
            {
                throw new InvalidOperationException(
                    $"NPM route '{checkpoint.PublicHost}' does not match its exact pre-cutover checkpoint.");
            }
        }
    }

    private async Task<bool> TryRestoreTargetRouteSnapshotsAsync(
        IReadOnlyList<TargetRouteSnapshot> targetRoutes)
    {
        try
        {
            foreach (var target in targetRoutes)
            {
                if (target.Snapshot is null)
                {
                    return false;
                }
                await npmProxyHostService.RestoreSnapshotAsync(
                    target.Snapshot,
                    target.PublicHost,
                    CancellationToken.None);
            }

            foreach (var target in targetRoutes)
            {
                var current = await npmProxyHostService.GetByDomainAsync(
                    target.PublicHost,
                    CancellationToken.None);
                if (target.Snapshot is null ||
                    !NpmProxyHostService.SnapshotMatches(target.Snapshot, current, requireSameId: true))
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not compensate the target NPM routes after a failed rollback mutation.");
            return false;
        }
    }

    private async Task StopTargetContainersAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        foreach (var containerName in new[] { plan.ElementContainerName, plan.MatrixContainerName })
        {
            var inspect = await docker.Containers.InspectContainerAsync(containerName, ct);
            if (inspect.State?.Running is true)
            {
                await docker.Containers.StopContainerAsync(
                    containerName,
                    new ContainerStopParameters { WaitBeforeKillSeconds = 30 },
                    ct);
            }
        }

        await EnsureTargetContainersStoppedAsync(plan, ct);
    }

    private async Task EnsureTargetContainersStoppedAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        foreach (var containerName in new[] { plan.MatrixContainerName, plan.ElementContainerName })
        {
            var inspect = await docker.Containers.InspectContainerAsync(containerName, ct);
            if (inspect.State?.Running is true)
            {
                throw new InvalidOperationException(
                    $"Target container '{containerName}' remained running after rollback stop.");
            }
        }
    }

    private CreateChatStackRuntimeResult BuildRouteFreeRuntimeResult(
        MigrationProductionAdoptionEntity plan,
        RuntimeOwnership runtime,
        string executionId,
        bool containersStopped) =>
        new(
            StackId: plan.RuntimeStackId,
            Status: containersStopped
                ? "migration_target_rolled_back_awaiting_source"
                : "migration_target_routes_restored",
            Message: containersStopped
                ? "The migrated target runtime is stopped and awaits source-host restoration. Target data and runtime records are retained."
                : "The pre-cutover routes are restored and normal MEM public route ownership has been removed.",
            Matrix: BuildServiceResult(plan, runtime.Matrix, executionId, containersStopped),
            Element: BuildServiceResult(plan, runtime.Element, executionId, containersStopped),
            Warnings:
            [
                "The legacy source remains frozen until the source-restoration handoff is executed on the source host.",
                "Target database, media, manifests, and runtime ownership are retained for evidence and controlled recovery."
            ],
            Evidence:
            [
                new HostAgentEvidence(
                    "migration-production.rollback.target-routes-restored",
                    "The exact pre-cutover NPM route checkpoints have been restored."),
                new HostAgentEvidence(
                    containersStopped
                        ? "migration-production.rollback.target-containers-stopped"
                        : "migration-production.rollback.target-containers-running-private",
                    containersStopped
                        ? "The target Matrix and Element containers are stopped."
                        : "The target containers remain private while rollback is completed.",
                    executionId)
            ]);

    private static HostAgentServiceRuntimeResult BuildServiceResult(
        MigrationProductionAdoptionEntity plan,
        RuntimeServiceInstanceEntity service,
        string executionId,
        bool stopped)
    {
        var metadata = DeserializeMetadata(service.RuntimeMetadataJson);
        metadata["migrationId"] = plan.MigrationIntake.IntakeId;
        metadata["adoptionPlanId"] = plan.AdoptionPlanId;
        metadata["cutoverExecutionId"] = plan.CutoverExecutionId;
        metadata["rollbackExecutionId"] = executionId;
        metadata["publicRouteReady"] = "false";
        metadata["readinessVerified"] = "false";
        metadata["acceptancePending"] = "false";
        metadata["sourceRestorationPending"] = "true";
        metadata["matrixStarted"] = service.ServiceKey == ServiceKeys.Matrix && !stopped ? "true" : "false";
        metadata["elementStarted"] = service.ServiceKey == ServiceKeys.ElementWeb && !stopped ? "true" : "false";

        return new HostAgentServiceRuntimeResult(
            InstanceId: service.InstanceId,
            ServiceKey: service.ServiceKey,
            ContainerId: service.ContainerId ?? string.Empty,
            ContainerName: service.ContainerName ?? string.Empty,
            HostPort: 0,
            DataPath: service.DataPath,
            ServerName: service.ServerName,
            PublicHost: null,
            PublicBaseUrl: null,
            InternalHost: service.InternalHost,
            InternalBaseUrl: service.InternalBaseUrl,
            PublicRouteId: null,
            InternalRouteId: null,
            NpmCertificateId: null,
            RuntimeMetadata: metadata);
    }

    private async Task<MigrationProductionSourceRestorationHandoffEnvelope> BuildSourceHandoffAsync(
        MigrationProductionAdoptionEntity plan,
        MigrationCutoverContext context,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> checkpoints,
        string executionId,
        CancellationToken ct)
    {
        var sources = await db.MigrationSources.AsNoTracking()
            .Where(x => x.MigrationIntakeEntityId == plan.MigrationIntakeEntityId)
            .OrderBy(x => x.SourceId)
            .ToArrayAsync(ct);
        if (sources.Length != 1 ||
            !IsSha256(sources[0].SourceFingerprint))
        {
            throw new InvalidDataException(
                "The Migration Session must contain exactly one durable source identity with a source fingerprint before source restoration handoff.");
        }
        var source = sources[0];
        var packageRevisionSha256 = context.PackageRevision.DecryptedArchiveSha256;
        if (!IsSha256(packageRevisionSha256))
        {
            throw new InvalidDataException(
                "The authoritative final package revision hash is missing from source restoration authority.");
        }

        var routes = new List<MigrationProductionSourceRestorationRouteEvidence>();
        foreach (var checkpoint in checkpoints)
        {
            var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
            if (!MigrationProductionCutoverService.RouteCheckpointMatches(checkpoint, current))
            {
                throw new InvalidOperationException(
                    $"Cannot issue source-restoration authority because route '{checkpoint.PublicHost}' no longer matches its pre-cutover checkpoint.");
            }

            var snapshot = current is null ? null : NpmProxyHostService.CaptureSnapshot(current);
            routes.Add(new MigrationProductionSourceRestorationRouteEvidence(
                checkpoint.ServiceKey,
                checkpoint.PublicHost,
                snapshot is null ? "absent" : "restored",
                snapshot?.Id,
                snapshot?.ForwardScheme,
                snapshot?.ForwardHost,
                snapshot?.ForwardPort,
                snapshot?.Enabled,
                snapshot is null ? Sha256("absent") : SnapshotSha256(snapshot)));
        }

        var containers = await CaptureTargetContainersAsync(
            plan,
            requireRunning: false,
            blockers: null,
            ct);
        if (containers.Any(x => x.Running))
        {
            throw new InvalidOperationException(
                "Cannot issue source-restoration authority while a migrated target container is still running.");
        }

        var now = DateTime.UtcNow;
        var handoffId = string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffId)
            ? CreateId("mpsh", now)
            : plan.RollbackSourceHandoffId;
        var payload = new MigrationProductionSourceRestorationHandoffPayload(
            HandoffId: handoffId,
            CreatedAtUtc: now,
            MigrationId: plan.MigrationIntake.IntakeId,
            AdoptionPlanId: plan.AdoptionPlanId,
            CutoverExecutionId: plan.CutoverExecutionId!,
            TargetRollbackExecutionId: executionId,
            PlanSha256: plan.PlanSha256,
            PackageRevisionId: context.PackageRevision.PackageRevisionId,
            PackageRevisionSha256: packageRevisionSha256,
            SourceMigrationId: context.Capture.SourceMigrationId,
            SourceId: source.SourceId,
            SourceFingerprint: source.SourceFingerprint,
            SourceStackSlug: context.Capture.SourceStackSlug,
            MatrixServerName: context.Capture.MatrixServerName,
            SourceFrozen: true,
            MigrationAccepted: false,
            TargetRoutesRestored: true,
            TargetRuntimeRoutesRemoved: true,
            TargetContainersStopped: true,
            Routes: routes,
            TargetContainers: containers.Select(x =>
                new MigrationProductionSourceRestorationContainerEvidence(
                    x.ServiceKey,
                    x.ContainerName,
                    x.ContainerId,
                    Stopped: !x.Running)).ToArray());

        return new MigrationProductionSourceRestorationHandoffEnvelope(
            SourceHandoffSchemaVersion,
            ComputePayloadSha256(payload),
            payload);
    }

    private async Task<RuntimeOwnership> LoadRuntimeAsync(
        MigrationProductionAdoptionEntity plan,
        bool tracking,
        CancellationToken ct)
    {
        IQueryable<RuntimeStackEntity> query = db.RuntimeStacks
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        var stack = await query.SingleOrDefaultAsync(x => x.Id == plan.RuntimeStackId, ct)
            ?? throw new InvalidOperationException(
                "The migrated normal MEM Runtime Stack record was not found.");
        var matrix = stack.ServiceInstances.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix)
            ?? throw new InvalidOperationException("The migrated Matrix service record was not found.");
        var element = stack.ServiceInstances.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb)
            ?? throw new InvalidOperationException("The migrated Element service record was not found.");

        if (matrix.InstanceId != plan.MatrixInstanceId ||
            element.InstanceId != plan.ElementInstanceId ||
            !string.Equals(matrix.ContainerName, plan.MatrixContainerName, StringComparison.Ordinal) ||
            !string.Equals(element.ContainerName, plan.ElementContainerName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Migrated runtime ownership no longer matches the durable production adoption plan.");
        }

        return new RuntimeOwnership(stack, matrix, element);
    }

    private async Task<MigrationProductionAdoptionEntity?> LoadPlanAsync(
        string migrationId,
        bool tracking,
        CancellationToken ct)
    {
        IQueryable<MigrationProductionAdoptionEntity> query = db.MigrationProductionAdoptions
            .Include(x => x.MigrationIntake)
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .Include(x => x.StagingRun)
            .AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            x => x.MigrationIntake.IntakeId == migrationId,
            ct);
    }

    private static CutoverRollbackCheckpointEvidence DeserializeCutoverRollbackCheckpoint(
        string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The durable cutover rollback checkpoint is missing.");
        }

        try
        {
            return JsonSerializer.Deserialize<CutoverRollbackCheckpointEvidence>(json, JsonOptions)
                ?? throw new InvalidDataException("The durable cutover rollback checkpoint is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The durable cutover rollback checkpoint is invalid.", ex);
        }
    }

    private static RollbackPreviewEvidence DeserializeRollbackPreview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("The durable target rollback preview evidence is missing.");
        }

        try
        {
            return JsonSerializer.Deserialize<RollbackPreviewEvidence>(json, JsonOptions)
                ?? throw new InvalidDataException("The durable target rollback preview evidence is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The durable target rollback preview evidence is invalid.", ex);
        }
    }

    private static int ResolveTargetRouteId(
        MigrationProductionAdoptionEntity plan,
        string serviceKey)
    {
        var raw = string.Equals(serviceKey, ServiceKeys.Matrix, StringComparison.OrdinalIgnoreCase)
            ? plan.MatrixNpmRouteId
            : string.Equals(serviceKey, ServiceKeys.ElementWeb, StringComparison.OrdinalIgnoreCase)
                ? plan.ElementNpmRouteId
                : null;
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : throw new InvalidDataException(
                $"The completed cutover does not contain a valid target route id for '{serviceKey}'.");
    }

    private static RuntimeRouteCheckpoint ToRuntimeRouteCheckpoint(RuntimeRouteEntity route) =>
        new(
            route.ServiceKey,
            route.PublicHost,
            route.ForwardHost,
            route.ForwardPort,
            route.ProviderRouteId,
            route.Status);

    private static string ComputePreviewSha256(
        string planSha256,
        string cutoverExecutionId,
        IReadOnlyList<TargetRouteSnapshot> targetRoutes,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> restoreCheckpoints,
        IReadOnlyList<RuntimeRouteCheckpoint> runtimeRoutes,
        IReadOnlyList<TargetContainerSnapshot> containers)
    {
        var json = JsonSerializer.Serialize(new
        {
            planSha256,
            cutoverExecutionId,
            targetRoutes,
            restoreCheckpoints,
            runtimeRoutes,
            containers,
        }, JsonOptions);
        return Sha256(json);
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string SnapshotSha256(NpmProxyHostSnapshot snapshot) =>
        Sha256(JsonSerializer.Serialize(snapshot, JsonOptions));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string NormalizeScheme(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "http" : value.Trim().ToLowerInvariant();

    private static string CreateId(string prefix, DateTime utc) =>
        $"{prefix}_{utc:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}";

    private static Dictionary<string, string?> DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions)
                ?? new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private static RollbackFailure ClassifyFailure(Exception exception) =>
        exception switch
        {
            DockerApiException => new(
                "migration_production_rollback_target_runtime_unavailable",
                "The migrated target containers could not be inspected or stopped during pre-acceptance rollback."),
            HttpRequestException => new(
                "migration_production_rollback_npm_failed",
                "NPM could not restore and verify the exact pre-cutover Matrix and Element route snapshots."),
            InvalidDataException => new(
                "migration_production_rollback_evidence_invalid",
                "Durable cutover or rollback evidence was incomplete or invalid; source restoration remains blocked."),
            _ => new(
                "migration_production_rollback_failed",
                "Target rollback did not complete. The legacy source remains frozen; review route, runtime, and handoff evidence before retrying."),
        };

    private sealed record CutoverRollbackCheckpointEvidence(
        string MigrationId,
        string AdoptionPlanId,
        string ExecutionId,
        string PlanSha256,
        bool SourceFrozen,
        string SourceMigrationId,
        string PreviewId,
        string PreviewSha256,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> RouteCheckpoints,
        CutoverTargetRouteIds TargetRoutes,
        DateTime TargetPublicAtUtc);

    private sealed record CutoverTargetRouteIds(
        string MatrixRouteId,
        string ElementRouteId);

    private sealed record RuntimeOwnership(
        RuntimeStackEntity Stack,
        RuntimeServiceInstanceEntity Matrix,
        RuntimeServiceInstanceEntity Element);

    internal sealed record TargetRouteSnapshot(
        string ServiceKey,
        string PublicHost,
        int ExpectedTargetRouteId,
        NpmProxyHostSnapshot? Snapshot);

    internal sealed record RuntimeRouteCheckpoint(
        string ServiceKey,
        string PublicHost,
        string ForwardHost,
        int ForwardPort,
        string? ProviderRouteId,
        string Status);

    internal sealed record TargetContainerSnapshot(
        string ServiceKey,
        string ContainerName,
        string? ContainerId,
        bool Running);

    internal sealed record RollbackPreviewEvidence(
        string AdoptionPlanId,
        string PlanSha256,
        string CutoverExecutionId,
        string SourceMigrationId,
        IReadOnlyList<TargetRouteSnapshot> TargetRoutes,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> RestoreCheckpoints,
        IReadOnlyList<RuntimeRouteCheckpoint> RuntimeRoutes,
        IReadOnlyList<TargetContainerSnapshot> TargetContainers,
        IReadOnlyList<MigrationProductionRollbackRouteState> Routes,
        IReadOnlyList<string> Blockers);

    private sealed record RollbackFailure(string Code, string Summary);
}
