using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Commands;
using HostAgent.Runtime.Ingress;
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
/// Performs the migration-owned transition from a privately healthy normal MEM runtime to
/// public Matrix and Element routing. The service records a fresh NPM snapshot before mutation,
/// refuses route drift, persists normal RuntimeRoute ownership, and stops before acceptance.
/// Coordinated source/target rollback remains MIG-PRODUCTION-01C.
/// </summary>
public sealed class MigrationProductionCutoverService(
    MemDbContext db,
    MigrationCutoverContextResolver contextResolver,
    MigrationProductionAdoptionService adoptionService,
    MigrationProductionCertificateResolver certificateResolver,
    NpmProxyHostService npmProxyHostService,
    IRoutePublisher routePublisher,
    RuntimeStackManifestStore manifestStore,
    DockerClient docker,
    ILogger<MigrationProductionCutoverService> logger,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MigrationProductionAdoptionStateResponse> PreparePreviewAsync(
        string migrationId,
        PrepareMigrationProductionCutoverRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "Prepare and privately materialise the normal MEM production runtime before cutover preview.");

        ValidatePrivateRuntimeReady(plan, allowPreviewReady: true, allowCompensatedFailure: true);
        ValidatePreviewReplacementState(plan);
        _ = await contextResolver.ResolveAsync(migrationId, ct);

        if (await db.MigrationAcceptances.AsNoTracking()
            .AnyAsync(x => x.MigrationIntakeEntityId == plan.MigrationIntakeEntityId, ct))
        {
            throw new InvalidOperationException("Public cutover preview cannot be prepared after migration acceptance.");
        }

        var runtime = await LoadRuntimeAsync(plan, ct);
        var blockers = await BuildPreviewBlockersAsync(plan, runtime, ct);
        var routePlans = DeserializeRoutePlans(plan.RoutePlanJson);
        var certificate = await certificateResolver.ResolveAsync(
            routePlans.Select(x => x.PublicHost).ToArray(),
            ct);
        blockers.AddRange(certificate.Blockers);
        var checkpoints = new List<RouteCheckpoint>();
        var publicSnapshots = new List<MigrationProductionCutoverRouteSnapshot>();

        foreach (var route in routePlans)
        {
            var checkpoint = await CaptureRouteCheckpointAsync(route, certificate, ct);
            checkpoints.Add(checkpoint);
            publicSnapshots.Add(ToPublicSnapshot(checkpoint));
            if (checkpoint.AlreadyTargetsProductionRuntime)
            {
                blockers.Add($"NPM route '{checkpoint.PublicHost}' already targets the planned runtime without normal MEM route ownership.");
            }
            if (checkpoint.ExistingRouteFound &&
                (string.IsNullOrWhiteSpace(checkpoint.ExistingForwardHost) || checkpoint.ExistingForwardPort is null))
            {
                blockers.Add($"NPM route '{checkpoint.PublicHost}' has incomplete live forward-target evidence.");
            }
        }

        var status = blockers.Count == 0 ? "ready" : "blocked";
        if (CanReuseEquivalentPreview(
                plan,
                status,
                publicSnapshots,
                checkpoints,
                blockers))
        {
            logger.LogDebug(
                "Reused unchanged Migration cutover preview {PreviewId} for {MigrationId}; no durable Migration state changed.",
                plan.CutoverPreviewId,
                migrationId);
            return await adoptionService.GetStateAsync(migrationId, ct);
        }

        var now = DateTime.UtcNow;
        var previewId = CreateId("mpcv", now);
        var previewRefreshAncestry = CapturePreviewRefreshAncestry(plan, previewId, now);
        var retryAncestry = ResetFailedCutoverForFreshPreview(plan, previewId, now);
        var snapshotSha256 = ComputePreviewSha256(
            plan.PlanSha256,
            checkpoints,
            retryAncestry,
            previewRefreshAncestry);

        plan.CutoverPreviewId = previewId;
        plan.CutoverPreviewStatus = status;
        plan.CutoverPreviewCreatedAtUtc = now;
        plan.CutoverPreviewExpiresAtUtc = null;
        plan.CutoverPreviewSha256 = snapshotSha256;
        plan.CutoverPreviewJson = JsonSerializer.Serialize(new PreviewEvidence(
            plan.AdoptionPlanId,
            plan.PlanSha256,
            publicSnapshots,
            checkpoints,
            blockers,
            retryAncestry,
            previewRefreshAncestry), JsonOptions);
        plan.CutoverFailureCode = null;
        plan.CutoverFailureSummary = null;
        plan.BlockerSummary = blockers.Count == 0
            ? null
            : string.Join("; ", blockers);
        plan.Status = blockers.Count == 0
            ? "cutover-preview-ready"
            : "private-runtime-ready";
        plan.UpdatedAtUtc = now;

        await db.SaveChangesAsync(ct);
        return await adoptionService.GetStateAsync(migrationId, ct);
    }

    public async Task<MigrationProductionAdoptionStateResponse> ExecuteAsync(
        string migrationId,
        ExecuteMigrationProductionCutoverRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePreviewId(request);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "Prepare and privately materialise the normal MEM production runtime before public cutover.");

        if (string.Equals(plan.CutoverStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) &&
            plan.PublicRoutesCreated &&
            plan.RuntimePromotionCompleted)
        {
            return await adoptionService.GetStateAsync(migrationId, ct);
        }

        ValidatePrivateRuntimeReady(plan, allowPreviewReady: true);
        ValidatePreview(plan, request.PreviewId);
        var preview = DeserializePreview(plan.CutoverPreviewJson);
        if (preview.Blockers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Controlled public cutover remains blocked: {string.Join("; ", preview.Blockers)}");
        }

        var context = await contextResolver.ResolveAsync(migrationId, ct);
        ValidateAcknowledgements(request, context.Capture.SourceFrozen);
        if (!context.Capture.FinalCutoverEligible)
        {
            throw new InvalidOperationException(
                "Controlled public cutover requires current production authority.");
        }

        if (await db.MigrationAcceptances.AsNoTracking()
            .AnyAsync(x => x.MigrationIntakeEntityId == plan.MigrationIntakeEntityId, ct))
        {
            throw new InvalidOperationException("Controlled public cutover cannot execute after migration acceptance.");
        }

        var runtime = await LoadRuntimeAsync(plan, ct);
        await EnsureContainersRunningAsync(plan, ct);
        await EnsureRouteSnapshotUnchangedAsync(preview.Checkpoints, ct);

        var executionId = string.IsNullOrWhiteSpace(plan.CutoverExecutionId)
            ? CreateId("mpce", DateTime.UtcNow)
            : plan.CutoverExecutionId;
        var startedAtUtc = DateTime.UtcNow;
        plan.CutoverExecutionId = executionId;
        plan.CutoverStatus = "executing";
        plan.CutoverStartedAtUtc = startedAtUtc;
        plan.CutoverCompletedAtUtc = null;
        plan.CutoverFailureCode = null;
        plan.CutoverFailureSummary = null;
        plan.RouteCompensationAttempted = false;
        plan.RouteCompensationCompleted = false;
        plan.Status = "cutover-executing";
        plan.UpdatedAtUtc = startedAtUtc;
        await db.SaveChangesAsync(ct);
        await MigrationProductionDiagnosticEvents.RecordAsync(
            diagnostics,
            migrationId,
            eventCode: "migration.production.cutover.started",
            severity: MemDiagnosticSeverities.Information,
            stage: "public-cutover",
            message: "Controlled migration public cutover started.",
            targetStackSlug: plan.TargetStackSlug,
            service: "synapse",
            details: new Dictionary<string, string?>
            {
                ["adoptionPlanId"] = plan.AdoptionPlanId,
                ["executionId"] = executionId,
                ["previewId"] = request.PreviewId
            });

        RoutePublishResult? matrixRoute = null;
        RoutePublishResult? elementRoute = null;
        var mutationStarted = false;

        try
        {
            var matrixCheckpoint = RequireCheckpoint(preview.Checkpoints, ServiceKeys.Matrix);
            var elementCheckpoint = RequireCheckpoint(preview.Checkpoints, ServiceKeys.ElementWeb);

            matrixRoute = await PublishRouteAsync(matrixCheckpoint, RouteKind.Matrix, ct);
            mutationStarted = true;
            ValidatePublishedRoute(matrixRoute, matrixCheckpoint);

            elementRoute = await PublishRouteAsync(elementCheckpoint, RouteKind.ElementWeb, ct);
            ValidatePublishedRoute(elementRoute, elementCheckpoint);

            var publicResult = BuildPublicRuntimeResult(
                plan,
                runtime,
                matrixRoute,
                elementRoute,
                executionId);
            await manifestStore.SaveAsync(plan.TargetStackSlug, publicResult, ct);

            var routeRows = await db.RuntimeRoutes.AsNoTracking()
                .Where(x => x.RuntimeStackId == plan.RuntimeStackId)
                .ToArrayAsync(ct);
            var matrixRouteRow = routeRows.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix)
                ?? throw new InvalidOperationException(
                    "Normal MEM Matrix route ownership was not persisted after public cutover.");
            var elementRouteRow = routeRows.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb)
                ?? throw new InvalidOperationException(
                    "Normal MEM Element route ownership was not persisted after public cutover.");

            var completedAtUtc = DateTime.UtcNow;
            plan.MatrixNpmRouteId = matrixRoute.RouteId;
            plan.ElementNpmRouteId = elementRoute.RouteId;
            plan.RuntimePromotionCompleted = true;
            plan.PublicRoutesCreated = true;
            plan.TargetPublicAtUtc = completedAtUtc;
            plan.CutoverStatus = "public-awaiting-verification";
            plan.CutoverCompletedAtUtc = completedAtUtc;
            plan.Status = "public-awaiting-verification";
            plan.BlockerSummary = null;
            plan.UpdatedAtUtc = completedAtUtc;
            plan.CutoverRollbackCheckpointJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                executionId,
                plan.PlanSha256,
                sourceFrozen = context.Capture.SourceFrozen,
                sourceMigrationId = context.Capture.SourceMigrationId,
                previewId = plan.CutoverPreviewId,
                previewSha256 = plan.CutoverPreviewSha256,
                routeCheckpoints = preview.Checkpoints,
                targetRoutes = new
                {
                    matrixRouteId = matrixRoute.RouteId,
                    elementRouteId = elementRoute.RouteId,
                },
                targetPublicAtUtc = completedAtUtc,
            }, JsonOptions);
            plan.CutoverEvidenceJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                executionId,
                plan.RuntimeStackId,
                plan.TargetStackSlug,
                matrixRoute,
                elementRoute,
                runtimeRoutes = routeRows.Select(x => new
                {
                    x.ServiceKey,
                    x.PublicHost,
                    x.ForwardHost,
                    x.ForwardPort,
                    x.ProviderRouteId,
                    x.Status,
                }),
                runtimePromotionCompleted = true,
                acceptancePerformed = false,
                completedAtUtc,
            }, JsonOptions);

            await db.SaveChangesAsync(ct);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.cutover.completed",
                severity: MemDiagnosticSeverities.Information,
                stage: "public-cutover",
                message: "Controlled migration public cutover completed and now awaits production verification.",
                targetStackSlug: plan.TargetStackSlug,
                service: "synapse",
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.CutoverStatus,
                    ["publicRoutesCreated"] = plan.PublicRoutesCreated ? "true" : "false"
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["executionId"] = executionId
                });
            return await adoptionService.GetStateAsync(migrationId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Controlled Migration production cutover failed. MigrationId={MigrationId} AdoptionPlanId={AdoptionPlanId} ExecutionId={ExecutionId}",
                migrationId,
                plan.AdoptionPlanId,
                executionId);

            var compensationCompleted = false;
            if (mutationStarted)
            {
                plan.RouteCompensationAttempted = true;
                using var compensationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                compensationCompleted = await TryRestoreRouteCheckpointsAsync(
                    preview.Checkpoints,
                    compensationTimeout.Token);
                plan.RouteCompensationCompleted = compensationCompleted;

                if (compensationCompleted)
                {
                    try
                    {
                        await manifestStore.SaveAsync(
                            plan.TargetStackSlug,
                            BuildPrivateRuntimeResult(plan, runtime),
                            CancellationToken.None);
                    }
                    catch (Exception compensationEx)
                    {
                        compensationCompleted = false;
                        plan.RouteCompensationCompleted = false;
                        logger.LogError(
                            compensationEx,
                            "NPM route compensation completed, but normal runtime route ownership could not be returned to private state. MigrationId={MigrationId}",
                            migrationId);
                    }
                }
            }

            var failure = ClassifyFailure(ex, mutationStarted, compensationCompleted);
            var failedAtUtc = DateTime.UtcNow;
            plan.CutoverStatus = "failed";
            plan.CutoverCompletedAtUtc = failedAtUtc;
            plan.Status = "cutover-failed";
            plan.PublicRoutesCreated = !compensationCompleted && mutationStarted;
            plan.RuntimePromotionCompleted = false;
            plan.CutoverFailureCode = failure.Code;
            plan.CutoverFailureSummary = failure.Summary;
            plan.BlockerSummary = failure.Summary;
            plan.UpdatedAtUtc = failedAtUtc;
            plan.CutoverEvidenceJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                executionId,
                failure.Code,
                failure.Summary,
                matrixRoute,
                elementRoute,
                mutationStarted,
                routeCompensationAttempted = plan.RouteCompensationAttempted,
                routeCompensationCompleted = plan.RouteCompensationCompleted,
                failedAtUtc,
            }, JsonOptions);
            await db.SaveChangesAsync(CancellationToken.None);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.cutover.failed",
                severity: MemDiagnosticSeverities.Error,
                stage: "public-cutover",
                message: "Controlled migration public cutover failed.",
                targetStackSlug: plan.TargetStackSlug,
                createIncident: true,
                exception: ex,
                service: "synapse",
                expected: new Dictionary<string, string?>
                {
                    ["status"] = "public-awaiting-verification"
                },
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.CutoverStatus,
                    ["failureCode"] = failure.Code,
                    ["routeCompensationCompleted"] = plan.RouteCompensationCompleted ? "true" : "false"
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["executionId"] = executionId,
                    ["failureSummary"] = failure.Summary
                },
                retryable: plan.RouteCompensationCompleted,
                suggestedAction: plan.RouteCompensationCompleted
                    ? "Open the Migration Workspace, review the compensated cutover failure, and prepare a fresh preview before retrying."
                    : "Open the Migration Workspace immediately and review route recovery evidence before making further changes.");

            throw new InvalidOperationException(failure.Summary);
        }
    }

    internal static IReadOnlyList<string> GetMissingAcknowledgements(
        ExecuteMigrationProductionCutoverRequest request,
        bool sourceFrozen)
    {
        var missing = new List<string>();
        if (!request.ExecuteNpmRouteMutation) missing.Add("executeNpmRouteMutation");

        var authorityAcknowledged = request.AcknowledgeProductionAuthority ||
            sourceFrozen && request.AcknowledgeSourceFrozen;
        if (!authorityAcknowledged) missing.Add("acknowledgeProductionAuthority");

        if (!request.AcknowledgePrivateRuntimeHealthy) missing.Add("acknowledgePrivateRuntimeHealthy");
        if (!request.AcknowledgeRouteSnapshotReviewed) missing.Add("acknowledgeRouteSnapshotReviewed");
        if (!request.AcknowledgeCreatesPublicRoutes) missing.Add("acknowledgeCreatesPublicRoutes");
        if (!request.AcknowledgeNoDnsMutation) missing.Add("acknowledgeNoDnsMutation");
        if (!request.AcknowledgeNoCertificateMutation) missing.Add("acknowledgeNoCertificateMutation");
        if (!request.AcknowledgeRollbackIsNextSlice) missing.Add("acknowledgeRollbackIsNextSlice");
        if (!request.AcknowledgePostCutoverVerificationRequired) missing.Add("acknowledgePostCutoverVerificationRequired");
        return missing;
    }

    internal static bool RouteSnapshotMatches(
        MigrationProductionCutoverRouteSnapshot expected,
        NpmProxyHost? current)
    {
        if (!expected.ExistingRouteFound)
        {
            return current is null;
        }

        if (current is null)
        {
            return false;
        }

        return current.id == expected.ExistingRouteId &&
            string.Equals(NormalizeScheme(current.forward_scheme), NormalizeScheme(expected.ExistingForwardScheme), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(current.forward_host?.Trim(), expected.ExistingForwardHost?.Trim(), StringComparison.OrdinalIgnoreCase) &&
            current.forward_port == expected.ExistingForwardPort &&
            current.certificate_id == expected.ExistingCertificateId &&
            current.ssl_forced == expected.ExistingSslForced &&
            current.http2_support == expected.ExistingHttp2 &&
            (current.enabled ?? true) == (expected.ExistingEnabled ?? true) &&
            string.Equals(
                HashText(current.advanced_config),
                expected.ExistingAdvancedConfigSha256,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool RouteCheckpointMatches(
        RouteCheckpoint expected,
        NpmProxyHost? current)
    {
        if (!expected.ExistingRouteFound)
        {
            return current is null;
        }

        if (expected.ExistingSnapshot is not null)
        {
            return NpmProxyHostService.SnapshotMatches(
                expected.ExistingSnapshot,
                current,
                requireSameId: true);
        }

        return RouteSnapshotMatches(ToPublicSnapshot(expected), current);
    }

    private static void ValidatePreviewId(ExecuteMigrationProductionCutoverRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PreviewId))
        {
            throw new InvalidOperationException("A current server-generated cutover preview id is required.");
        }
    }

    private static void ValidateAcknowledgements(
        ExecuteMigrationProductionCutoverRequest request,
        bool sourceFrozen)
    {
        var missing = GetMissingAcknowledgements(request, sourceFrozen);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required acknowledgements: {string.Join(", ", missing)}");
        }
    }

    private static void ValidatePrivateRuntimeReady(
        MigrationProductionAdoptionEntity plan,
        bool allowPreviewReady = false,
        bool allowCompensatedFailure = false)
    {
        var validStatus = string.Equals(plan.Status, "private-runtime-ready", StringComparison.OrdinalIgnoreCase) ||
            (allowPreviewReady && string.Equals(plan.Status, "cutover-preview-ready", StringComparison.OrdinalIgnoreCase)) ||
            (allowCompensatedFailure && IsSafeCompensatedCutoverFailure(plan));
        if (!validStatus ||
            !string.Equals(plan.MaterializationStatus, "private-runtime-ready", StringComparison.OrdinalIgnoreCase) ||
            !plan.ProductionDatabaseImported ||
            !plan.MatrixProductionContainerStarted ||
            !plan.MatrixProductionHealthPassed ||
            !plan.ElementProductionContainerStarted ||
            !plan.ElementProductionHealthPassed ||
            !plan.RuntimeManifestSaved ||
            !plan.DatabaseOwnershipSaved ||
            !plan.RuntimeRecordsCreated ||
            plan.PublicRoutesCreated)
        {
            throw new InvalidOperationException(
                "Controlled public cutover requires a current privately healthy normal MEM runtime with no public route ownership.");
        }

        var stale = MigrationProductionAdoptionService.ResolveStaleReason(plan);
        if (stale is not null)
        {
            throw new InvalidOperationException(stale);
        }
    }


    internal static bool IsSafePreviewReplacement(MigrationProductionAdoptionEntity plan) =>
        string.Equals(plan.Status, "cutover-preview-ready", StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrWhiteSpace(plan.CutoverExecutionId) &&
        (string.IsNullOrWhiteSpace(plan.CutoverStatus) ||
            string.Equals(plan.CutoverStatus, "not-started", StringComparison.OrdinalIgnoreCase)) &&
        !plan.PublicRoutesCreated &&
        !plan.RuntimePromotionCompleted;

    private static void ValidatePreviewReplacementState(MigrationProductionAdoptionEntity plan)
    {
        if (string.Equals(plan.Status, "cutover-preview-ready", StringComparison.OrdinalIgnoreCase) &&
            !IsSafePreviewReplacement(plan))
        {
            throw new InvalidOperationException(
                "A fresh controlled public cutover preview cannot replace the active snapshot while cutover execution or public route ownership may exist.");
        }
    }

    internal static CutoverPreviewRefreshAncestry? CapturePreviewRefreshAncestry(
        MigrationProductionAdoptionEntity plan,
        string newPreviewId,
        DateTime preparedAtUtc)
    {
        if (!IsSafePreviewReplacement(plan) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewId))
        {
            return null;
        }

        const string reason = "operator-refresh";

        return new CutoverPreviewRefreshAncestry(
            PreviousPreviewId: plan.CutoverPreviewId,
            PreviousPreviewStatus: plan.CutoverPreviewStatus,
            PreviousCreatedAtUtc: plan.CutoverPreviewCreatedAtUtc,
            PreviousExpiresAtUtc: plan.CutoverPreviewExpiresAtUtc,
            PreviousSnapshotSha256: plan.CutoverPreviewSha256,
            PreviousEvidenceJson: plan.CutoverPreviewJson,
            ReplacementReason: reason,
            SupersededByPreviewId: newPreviewId,
            SupersededAtUtc: preparedAtUtc);
    }

    internal static bool CanReuseEquivalentPreview(
        MigrationProductionAdoptionEntity plan,
        string observedStatus,
        IReadOnlyList<MigrationProductionCutoverRouteSnapshot> observedRoutes,
        IReadOnlyList<RouteCheckpoint> observedCheckpoints,
        IReadOnlyList<string> observedBlockers)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(observedRoutes);
        ArgumentNullException.ThrowIfNull(observedCheckpoints);
        ArgumentNullException.ThrowIfNull(observedBlockers);

        var expectedPlanStatus = string.Equals(observedStatus, "ready", StringComparison.OrdinalIgnoreCase)
            ? "cutover-preview-ready"
            : string.Equals(observedStatus, "blocked", StringComparison.OrdinalIgnoreCase)
                ? "private-runtime-ready"
                : null;

        if (expectedPlanStatus is null ||
            !string.Equals(plan.Status, expectedPlanStatus, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.CutoverPreviewStatus, observedStatus, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewId) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewSha256) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewJson) ||
            !string.IsNullOrWhiteSpace(plan.CutoverExecutionId) ||
            (!string.IsNullOrWhiteSpace(plan.CutoverStatus) &&
                !string.Equals(plan.CutoverStatus, "not-started", StringComparison.OrdinalIgnoreCase)) ||
            plan.PublicRoutesCreated ||
            plan.RuntimePromotionCompleted ||
            !string.IsNullOrWhiteSpace(plan.CutoverFailureCode) ||
            !string.IsNullOrWhiteSpace(plan.CutoverFailureSummary))
        {
            return false;
        }

        PreviewEvidence existing;
        try
        {
            existing = DeserializePreview(plan.CutoverPreviewJson);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!string.Equals(existing.AdoptionPlanId, plan.AdoptionPlanId, StringComparison.Ordinal) ||
            !string.Equals(existing.PlanSha256, plan.PlanSha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedStoredSnapshotSha256 = ComputePreviewSha256(
            existing.PlanSha256,
            existing.Checkpoints,
            existing.RetryAncestry,
            existing.PreviewRefreshAncestry);
        if (!string.Equals(
                plan.CutoverPreviewSha256,
                expectedStoredSnapshotSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var existingObservationSha256 = ComputePreviewObservationSha256(
            existing.AdoptionPlanId,
            existing.PlanSha256,
            existing.Routes,
            existing.Checkpoints,
            existing.Blockers);
        var observedObservationSha256 = ComputePreviewObservationSha256(
            plan.AdoptionPlanId,
            plan.PlanSha256,
            observedRoutes,
            observedCheckpoints,
            observedBlockers);

        return string.Equals(
            existingObservationSha256,
            observedObservationSha256,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSafeCompensatedCutoverFailure(MigrationProductionAdoptionEntity plan) =>
        string.Equals(plan.Status, "cutover-failed", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(plan.CutoverStatus, "failed", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(plan.CutoverExecutionId) &&
        plan.CutoverCompletedAtUtc is not null &&
        !plan.PublicRoutesCreated &&
        !plan.RuntimePromotionCompleted &&
        (!plan.RouteCompensationAttempted || plan.RouteCompensationCompleted);

    internal static CutoverRetryAncestry? ResetFailedCutoverForFreshPreview(
        MigrationProductionAdoptionEntity plan,
        string newPreviewId,
        DateTime preparedAtUtc)
    {
        if (!IsSafeCompensatedCutoverFailure(plan))
        {
            return null;
        }

        var ancestry = new CutoverRetryAncestry(
            PreviousPreviewId: plan.CutoverPreviewId,
            PreviousPreviewSha256: plan.CutoverPreviewSha256,
            PreviousExecutionId: plan.CutoverExecutionId!,
            PreviousCutoverStatus: plan.CutoverStatus!,
            PreviousStartedAtUtc: plan.CutoverStartedAtUtc,
            PreviousCompletedAtUtc: plan.CutoverCompletedAtUtc,
            PreviousFailureCode: plan.CutoverFailureCode,
            PreviousFailureSummary: plan.CutoverFailureSummary,
            RouteCompensationAttempted: plan.RouteCompensationAttempted,
            RouteCompensationCompleted: plan.RouteCompensationCompleted,
            PreviousEvidenceJson: plan.CutoverEvidenceJson,
            SupersededByPreviewId: newPreviewId,
            SupersededAtUtc: preparedAtUtc);

        plan.CutoverExecutionId = null;
        plan.CutoverStatus = null;
        plan.CutoverStartedAtUtc = null;
        plan.CutoverCompletedAtUtc = null;
        plan.TargetPublicAtUtc = null;
        plan.MatrixNpmRouteId = null;
        plan.ElementNpmRouteId = null;
        plan.RuntimePromotionCompleted = false;
        plan.PublicRoutesCreated = false;
        plan.RouteCompensationAttempted = false;
        plan.RouteCompensationCompleted = false;
        plan.CutoverRollbackCheckpointJson = null;
        plan.CutoverFailureCode = null;
        plan.CutoverFailureSummary = null;

        return ancestry;
    }

    private static void ValidatePreview(MigrationProductionAdoptionEntity plan, string previewId)
    {
        if (!string.Equals(plan.CutoverPreviewStatus, "ready", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewId) ||
            !string.Equals(plan.CutoverPreviewId, previewId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewJson) ||
            string.IsNullOrWhiteSpace(plan.CutoverPreviewSha256))
        {
            throw new InvalidOperationException(
                "The controlled public cutover preview is missing, superseded, or not ready. Prepare a fresh preview.");
        }
    }

    private async Task<List<string>> BuildPreviewBlockersAsync(
        MigrationProductionAdoptionEntity plan,
        RuntimeOwnership runtime,
        CancellationToken ct)
    {
        var blockers = new List<string>();
        if (runtime.Stack.Routes.Count > 0 ||
            await db.RuntimeRoutes.AsNoTracking().AnyAsync(x => x.RuntimeStackId == plan.RuntimeStackId, ct))
        {
            blockers.Add("The normal MEM runtime already has route ownership; preview requires a private route-free runtime.");
        }

        var routePlans = DeserializeRoutePlans(plan.RoutePlanJson);
        if (routePlans.Count != 2 ||
            routePlans.All(x => x.ServiceKey != ServiceKeys.Matrix) ||
            routePlans.All(x => x.ServiceKey != ServiceKeys.ElementWeb))
        {
            blockers.Add("The adoption plan does not contain exactly one Matrix and one Element route intention.");
        }
        else
        {
            var publicHosts = routePlans.Select(x => x.PublicHost).ToArray();
            var conflictingOwners = await db.RuntimeRoutes.AsNoTracking()
                .Where(x => x.RuntimeStackId != plan.RuntimeStackId && publicHosts.Contains(x.PublicHost))
                .Select(x => x.PublicHost)
                .Distinct()
                .ToArrayAsync(ct);
            foreach (var host in conflictingOwners)
            {
                blockers.Add($"Normal MEM route ownership for '{host}' belongs to another Runtime Stack.");
            }
        }

        try
        {
            await EnsureContainersRunningAsync(plan, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            blockers.Add(ex.Message);
        }

        return blockers;
    }

    private async Task<RuntimeOwnership> LoadRuntimeAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        var stack = await db.RuntimeStacks
            .AsNoTracking()
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .SingleOrDefaultAsync(x => x.Id == plan.RuntimeStackId, ct)
            ?? throw new InvalidOperationException(
                "The planned normal MEM Runtime Stack record was not found.");

        var matrix = stack.ServiceInstances.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix)
            ?? throw new InvalidOperationException("The normal MEM Matrix service record was not found.");
        var element = stack.ServiceInstances.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb)
            ?? throw new InvalidOperationException("The normal MEM Element service record was not found.");

        if (matrix.InstanceId != plan.MatrixInstanceId ||
            element.InstanceId != plan.ElementInstanceId ||
            !string.Equals(matrix.ContainerName, plan.MatrixContainerName, StringComparison.Ordinal) ||
            !string.Equals(element.ContainerName, plan.ElementContainerName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Normal MEM runtime ownership no longer matches the durable production adoption plan.");
        }

        return new RuntimeOwnership(stack, matrix, element);
    }

    private async Task EnsureContainersRunningAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        foreach (var containerName in new[] { plan.MatrixContainerName, plan.ElementContainerName })
        {
            ContainerInspectResponse inspect;
            try
            {
                inspect = await docker.Containers.InspectContainerAsync(containerName, ct);
            }
            catch (DockerApiException ex)
            {
                throw new InvalidOperationException(
                    $"The private normal-runtime container '{containerName}' was not found.",
                    ex);
            }

            if (inspect.State?.Running is not true)
            {
                throw new InvalidOperationException(
                    $"The private normal-runtime container '{containerName}' is not running.");
            }

            if ((inspect.HostConfig?.PortBindings?.Count ?? 0) > 0)
            {
                throw new InvalidOperationException(
                    $"The private normal-runtime container '{containerName}' unexpectedly publishes a host port.");
            }
        }
    }

    private async Task<RouteCheckpoint> CaptureRouteCheckpointAsync(
        MigrationProductionAdoptionRoutePlan plan,
        MigrationProductionCertificateResolution certificate,
        CancellationToken ct)
    {
        var existing = await npmProxyHostService.GetByDomainAsync(plan.PublicHost, ct);
        var existingAdvancedConfig = existing?.advanced_config?.Trim() ?? string.Empty;
        var alreadyTargets = existing is not null &&
            string.Equals(existing.forward_host, plan.ForwardHost, StringComparison.OrdinalIgnoreCase) &&
            existing.forward_port == plan.ForwardPort &&
            (existing.enabled ?? true);

        return new RouteCheckpoint(
            ServiceKey: plan.ServiceKey,
            PublicHost: plan.PublicHost,
            DesiredForwardHost: plan.ForwardHost,
            DesiredForwardPort: plan.ForwardPort,
            ExistingRouteFound: existing is not null,
            ExistingRouteId: existing?.id,
            ExistingForwardScheme: NormalizeScheme(existing?.forward_scheme),
            ExistingForwardHost: existing?.forward_host,
            ExistingForwardPort: existing?.forward_port,
            ExistingCertificateId: existing?.certificate_id,
            ExistingSslForced: existing?.ssl_forced,
            ExistingHttp2: existing?.http2_support,
            ExistingEnabled: existing is null ? null : existing.enabled ?? true,
            ExistingAdvancedConfig: existingAdvancedConfig,
            ExistingAdvancedConfigSha256: HashText(existingAdvancedConfig),
            AlreadyTargetsProductionRuntime: alreadyTargets,
            Action: existing is null ? "create" : alreadyTargets ? "verify" : "update",
            ExistingSnapshot: existing is null ? null : NpmProxyHostService.CaptureSnapshot(existing),
            SelectedCertificateId: certificate.NpmCertificateId,
            SelectedCertificateRecordId: certificate.CertificateRecordId,
            SelectedCertificateName: certificate.CertificateName);
    }

    private async Task EnsureRouteSnapshotUnchangedAsync(
        IReadOnlyList<RouteCheckpoint> checkpoints,
        CancellationToken ct)
    {
        foreach (var checkpoint in checkpoints)
        {
            var current = await npmProxyHostService.GetByDomainAsync(checkpoint.PublicHost, ct);
            if (!RouteCheckpointMatches(checkpoint, current))
            {
                throw new InvalidOperationException(
                    $"NPM route '{checkpoint.PublicHost}' changed after preview. Prepare a fresh cutover preview before execution.");
            }
        }
    }

    private async Task<RoutePublishResult> PublishRouteAsync(
        RouteCheckpoint checkpoint,
        RouteKind kind,
        CancellationToken ct)
    {
        if (checkpoint.SelectedCertificateId is not > 0)
        {
            throw new InvalidOperationException(
                $"NPM route '{checkpoint.PublicHost}' has no approved certificate selected by the current cutover preview.");
        }

        return await routePublisher.EnsureAsync(
            new RoutePublishRequest(
                Domain: checkpoint.PublicHost,
                ForwardHost: checkpoint.DesiredForwardHost,
                ForwardPort: checkpoint.DesiredForwardPort,
                Kind: kind,
                ForwardScheme: "http",
                IsPublic: true,
                RequireSsl: true,
                CertificateId: checkpoint.SelectedCertificateId,
                ForceSsl: true,
                Http2: true),
            ct);
    }

    private static void ValidatePublishedRoute(RoutePublishResult route, RouteCheckpoint checkpoint)
    {
        if (!route.Ready ||
            !route.SslConfigured ||
            route.CertificateId != checkpoint.SelectedCertificateId ||
            !string.Equals(route.Domain, checkpoint.PublicHost, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(route.RouteId))
        {
            throw new InvalidOperationException(
                $"NPM route '{checkpoint.PublicHost}' was not created or updated into a ready SSL-enabled state with the preview-selected certificate.");
        }
    }

    private async Task<bool> TryRestoreRouteCheckpointsAsync(
        IReadOnlyList<RouteCheckpoint> checkpoints,
        CancellationToken ct)
    {
        var succeeded = true;
        foreach (var checkpoint in checkpoints.Reverse())
        {
            try
            {
                if (!checkpoint.ExistingRouteFound)
                {
                    await npmProxyHostService.DeleteByDomainIfExistsAsync(
                        checkpoint.PublicHost,
                        CancellationToken.None);
                    continue;
                }

                if (checkpoint.ExistingSnapshot is not null)
                {
                    await npmProxyHostService.RestoreSnapshotAsync(
                        checkpoint.ExistingSnapshot,
                        checkpoint.PublicHost,
                        CancellationToken.None);
                }
                else
                {
                    await npmProxyHostService.EnsureProxyHostAsync(
                        new CreateNpmProxyHostRequest(
                            Domain: checkpoint.PublicHost,
                            ForwardHost: checkpoint.ExistingForwardHost!,
                            ForwardPort: checkpoint.ExistingForwardPort!.Value,
                            ForceSsl: checkpoint.ExistingSslForced ?? false,
                            CertificateId: checkpoint.ExistingCertificateId ?? 0,
                            Http2: checkpoint.ExistingHttp2 ?? true,
                            AdvancedConfig: checkpoint.ExistingAdvancedConfig),
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                succeeded = false;
                logger.LogError(
                    ex,
                    "Could not compensate NPM route after failed Migration cutover. Host={Host}",
                    checkpoint.PublicHost);
            }
        }

        if (!succeeded)
        {
            return false;
        }

        foreach (var checkpoint in checkpoints)
        {
            try
            {
                var current = await npmProxyHostService.GetByDomainAsync(
                    checkpoint.PublicHost,
                    CancellationToken.None);
                if (!RouteCheckpointMatches(checkpoint, current))
                {
                    logger.LogError(
                        "NPM route compensation did not restore the exact preview snapshot. Host={Host}",
                        checkpoint.PublicHost);
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Could not verify NPM route compensation. Host={Host}",
                    checkpoint.PublicHost);
                return false;
            }
        }

        return true;
    }

    private CreateChatStackRuntimeResult BuildPublicRuntimeResult(
        MigrationProductionAdoptionEntity plan,
        RuntimeOwnership runtime,
        RoutePublishResult matrixRoute,
        RoutePublishResult elementRoute,
        string executionId) =>
        new(
            StackId: plan.RuntimeStackId,
            Status: "migration_public_awaiting_verification",
            Message: "The migrated normal MEM runtime owns the public Matrix and Element routes and awaits production verification.",
            Matrix: BuildServiceResult(
                plan,
                runtime.Matrix,
                plan.MatrixPublicHost,
                plan.MatrixPublicBaseUrl,
                matrixRoute.RouteId,
                matrixRoute.CertificateId,
                executionId),
            Element: BuildServiceResult(
                plan,
                runtime.Element,
                plan.ElementPublicHost,
                plan.ElementPublicBaseUrl,
                elementRoute.RouteId,
                elementRoute.CertificateId,
                executionId),
            Warnings:
            [
                "Migration acceptance has not occurred.",
                "Coordinated pre-acceptance rollback is delivered by MIG-PRODUCTION-01C.",
                "External and semantic production verification remains required before acceptance."
            ],
            Evidence:
            [
                new HostAgentEvidence(
                    "migration-production.public-routes.created",
                    "Matrix and Element NPM routes now target the normal MEM runtime.",
                    executionId),
                new HostAgentEvidence(
                    "migration-production.acceptance.pending",
                    "The target is public but remains unaccepted pending production verification.")
            ]);

    private CreateChatStackRuntimeResult BuildPrivateRuntimeResult(
        MigrationProductionAdoptionEntity plan,
        RuntimeOwnership runtime) =>
        new(
            StackId: plan.RuntimeStackId,
            Status: "migration_private_production_ready",
            Message: "The normal MEM runtime remains privately healthy after route compensation.",
            Matrix: BuildServiceResult(plan, runtime.Matrix, null, null, null, null, plan.CutoverExecutionId),
            Element: BuildServiceResult(plan, runtime.Element, null, null, null, null, plan.CutoverExecutionId),
            Warnings: [],
            Evidence:
            [
                new HostAgentEvidence(
                    "migration-production.public-routes.absent",
                    "No normal MEM public route ownership remains after cutover compensation.")
            ]);

    private static HostAgentServiceRuntimeResult BuildServiceResult(
        MigrationProductionAdoptionEntity plan,
        RuntimeServiceInstanceEntity service,
        string? publicHost,
        string? publicBaseUrl,
        string? publicRouteId,
        int? npmCertificateId,
        string? executionId)
    {
        var metadata = DeserializeMetadata(service.RuntimeMetadataJson);
        metadata["migrationId"] = plan.MigrationIntake.IntakeId;
        metadata["adoptionPlanId"] = plan.AdoptionPlanId;
        metadata["cutoverExecutionId"] = executionId;
        metadata["publicRouteReady"] = publicRouteId is null ? "false" : "true";
        metadata["readinessVerified"] = "false";
        metadata["acceptancePending"] = publicRouteId is null ? "false" : "true";
        metadata["privateProductionMaterialization"] = "true";

        return new HostAgentServiceRuntimeResult(
            InstanceId: service.InstanceId,
            ServiceKey: service.ServiceKey,
            ContainerId: service.ContainerId ?? string.Empty,
            ContainerName: service.ContainerName ?? string.Empty,
            HostPort: 0,
            DataPath: service.DataPath,
            ServerName: service.ServerName,
            PublicHost: publicHost,
            PublicBaseUrl: publicBaseUrl,
            InternalHost: service.InternalHost,
            InternalBaseUrl: service.InternalBaseUrl,
            PublicRouteId: publicRouteId,
            InternalRouteId: null,
            NpmCertificateId: npmCertificateId,
            RuntimeMetadata: metadata);
    }

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

    private async Task<MigrationProductionAdoptionEntity?> LoadPlanAsync(
        string migrationId,
        bool tracking,
        CancellationToken ct)
    {
        var query = db.MigrationProductionAdoptions
            .Include(x => x.MigrationIntake)
                .ThenInclude(x => x.ProductionAuthorities)
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

    private static IReadOnlyList<MigrationProductionAdoptionRoutePlan> DeserializeRoutePlans(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<MigrationProductionAdoptionRoutePlan[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static PreviewEvidence DeserializePreview(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("The durable cutover preview evidence is missing.");
        }

        try
        {
            return JsonSerializer.Deserialize<PreviewEvidence>(json, JsonOptions)
                ?? throw new InvalidOperationException("The durable cutover preview evidence is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The durable cutover preview evidence is invalid.", ex);
        }
    }

    private static RouteCheckpoint RequireCheckpoint(
        IReadOnlyList<RouteCheckpoint> checkpoints,
        string serviceKey) =>
        checkpoints.SingleOrDefault(x => string.Equals(x.ServiceKey, serviceKey, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"The cutover preview does not contain route evidence for '{serviceKey}'.");

    private static MigrationProductionCutoverRouteSnapshot ToPublicSnapshot(RouteCheckpoint checkpoint) =>
        new(
            checkpoint.ServiceKey,
            checkpoint.PublicHost,
            checkpoint.DesiredForwardHost,
            checkpoint.DesiredForwardPort,
            checkpoint.ExistingRouteFound,
            checkpoint.ExistingRouteId,
            checkpoint.ExistingForwardScheme,
            checkpoint.ExistingForwardHost,
            checkpoint.ExistingForwardPort,
            checkpoint.ExistingCertificateId,
            checkpoint.ExistingSslForced,
            checkpoint.ExistingHttp2,
            checkpoint.ExistingEnabled,
            checkpoint.ExistingAdvancedConfigSha256,
            checkpoint.AlreadyTargetsProductionRuntime,
            checkpoint.Action,
            checkpoint.SelectedCertificateId,
            checkpoint.SelectedCertificateRecordId,
            checkpoint.SelectedCertificateName);

    internal static string ComputePreviewSha256(
        string planSha256,
        IReadOnlyList<RouteCheckpoint> checkpoints,
        CutoverRetryAncestry? retryAncestry,
        CutoverPreviewRefreshAncestry? previewRefreshAncestry)
    {
        var json = JsonSerializer.Serialize(
            new { planSha256, checkpoints, retryAncestry, previewRefreshAncestry },
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string ComputePreviewObservationSha256(
        string adoptionPlanId,
        string planSha256,
        IReadOnlyList<MigrationProductionCutoverRouteSnapshot> routes,
        IReadOnlyList<RouteCheckpoint> checkpoints,
        IReadOnlyList<string> blockers)
    {
        var canonicalRoutes = routes
            .OrderBy(x => x.ServiceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PublicHost, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var canonicalCheckpoints = checkpoints
            .OrderBy(x => x.ServiceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PublicHost, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var canonicalBlockers = blockers
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var json = JsonSerializer.Serialize(
            new
            {
                adoptionPlanId,
                planSha256,
                routes = canonicalRoutes,
                checkpoints = canonicalCheckpoints,
                blockers = canonicalBlockers,
            },
            JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string HashText(string? value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value?.Trim() ?? string.Empty)))
            .ToLowerInvariant();

    private static string NormalizeScheme(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "http" : value.Trim().ToLowerInvariant();

    private static string CreateId(string prefix, DateTime utc) =>
        $"{prefix}_{utc:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}";

    internal static CutoverFailure ClassifyFailure(
        Exception exception,
        bool mutationStarted,
        bool compensationCompleted) =>
        exception switch
        {
            OperationCanceledException when !mutationStarted => new(
                "migration_production_cutover_cancelled",
                "Controlled public cutover ended before any public route was changed. Review the current route preview and try again."),
            OperationCanceledException when compensationCompleted => new(
                "migration_production_cutover_cancelled",
                "Controlled public cutover did not complete. MEM restored the previous public route state and kept the new server private."),
            OperationCanceledException => new(
                "migration_production_cutover_cancelled",
                "Controlled public cutover did not complete and MEM could not verify restoration of the previous public route state. Review recovery evidence immediately."),
            DockerApiException => new(
                "migration_production_cutover_runtime_unavailable",
                "The privately materialised normal MEM runtime was not available for controlled public cutover."),
            HttpRequestException => new(
                "migration_production_cutover_npm_failed",
                "NPM could not complete controlled Matrix and Element route cutover."),
            _ => new(
                "migration_production_cutover_failed",
                "Controlled public cutover did not complete. Review route compensation and rollback checkpoint evidence before retrying."),
        };

    private sealed record RuntimeOwnership(
        RuntimeStackEntity Stack,
        RuntimeServiceInstanceEntity Matrix,
        RuntimeServiceInstanceEntity Element);

    internal sealed record RouteCheckpoint(
        string ServiceKey,
        string PublicHost,
        string DesiredForwardHost,
        int DesiredForwardPort,
        bool ExistingRouteFound,
        int? ExistingRouteId,
        string ExistingForwardScheme,
        string? ExistingForwardHost,
        int? ExistingForwardPort,
        int? ExistingCertificateId,
        bool? ExistingSslForced,
        bool? ExistingHttp2,
        bool? ExistingEnabled,
        string ExistingAdvancedConfig,
        string ExistingAdvancedConfigSha256,
        bool AlreadyTargetsProductionRuntime,
        string Action,
        NpmProxyHostSnapshot? ExistingSnapshot = null,
        int? SelectedCertificateId = null,
        string? SelectedCertificateRecordId = null,
        string? SelectedCertificateName = null);

    internal sealed record PreviewEvidence(
        string AdoptionPlanId,
        string PlanSha256,
        IReadOnlyList<MigrationProductionCutoverRouteSnapshot> Routes,
        IReadOnlyList<RouteCheckpoint> Checkpoints,
        IReadOnlyList<string> Blockers,
        CutoverRetryAncestry? RetryAncestry = null,
        CutoverPreviewRefreshAncestry? PreviewRefreshAncestry = null);

    internal sealed record CutoverPreviewRefreshAncestry(
        string PreviousPreviewId,
        string? PreviousPreviewStatus,
        DateTime? PreviousCreatedAtUtc,
        DateTime? PreviousExpiresAtUtc,
        string? PreviousSnapshotSha256,
        string? PreviousEvidenceJson,
        string ReplacementReason,
        string SupersededByPreviewId,
        DateTime SupersededAtUtc);

    internal sealed record CutoverRetryAncestry(
        string? PreviousPreviewId,
        string? PreviousPreviewSha256,
        string PreviousExecutionId,
        string PreviousCutoverStatus,
        DateTime? PreviousStartedAtUtc,
        DateTime? PreviousCompletedAtUtc,
        string? PreviousFailureCode,
        string? PreviousFailureSummary,
        bool RouteCompensationAttempted,
        bool RouteCompensationCompleted,
        string? PreviousEvidenceJson,
        string SupersededByPreviewId,
        DateTime SupersededAtUtc);

    internal sealed record CutoverFailure(string Code, string Summary);
}
