using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Imports the source-host completion envelope after target rollback, independently revalidates
/// both trust-boundary records and the still-rolled-back target, and closes coordinated rollback.
/// It never accepts source evidence as authority for target runtime or route mutation.
/// </summary>
public sealed class MigrationProductionRollbackCompletionService(
    MemDbContext db,
    MigrationProductionAdoptionService adoptionService,
    NpmProxyHostService npmProxyHostService,
    DockerClient docker,
    ILogger<MigrationProductionRollbackCompletionService> logger)
{
    internal const string CompletionSchemaVersion =
        "mem.migration.source-restoration-completion.v1";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<MigrationProductionAdoptionStateResponse> ImportAsync(
        string migrationId,
        MigrationProductionSourceRestorationCompletionEnvelope envelope,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateCompletionEnvelope(envelope);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new FileNotFoundException(
                $"Migration Session '{migrationId}' has no production adoption plan.");

        if (string.Equals(
                plan.RollbackCompletionStatus,
                "coordinated-rollback-complete",
                StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    plan.RollbackSourceCompletionSha256,
                    envelope.PayloadSha256,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    plan.RollbackSourceCompletionAttemptId,
                    envelope.Payload.RestorationAttemptId,
                    StringComparison.Ordinal))
            {
                return await adoptionService.GetStateAsync(migrationId, ct);
            }

            throw new InvalidOperationException(
                "Coordinated rollback is already closed with different source-restoration evidence.");
        }

        ValidateTargetRollbackState(plan);
        await EnsureNotAcceptedAsync(plan, ct);
        var handoff = ReadAndValidateHandoff(plan);
        ValidateCompletionBindings(envelope.Payload, handoff.Payload);
        ValidateSourceRestorationProof(envelope.Payload);
        await EnsureTargetRollbackStillIntactAsync(plan, handoff.Payload, ct);

        var now = DateTime.UtcNow;
        plan.RollbackCompletionStatus = "coordinated-rollback-complete";
        plan.RollbackSourceCompletionAttemptId = envelope.Payload.RestorationAttemptId;
        plan.RollbackSourceCompletionSha256 = envelope.PayloadSha256.ToLowerInvariant();
        plan.RollbackSourceCompletionJson = JsonSerializer.Serialize(envelope, JsonOptions);
        plan.RollbackSourceCompletionImportedAtUtc = now;
        plan.CoordinatedRollbackCompletedAtUtc = now;
        plan.RollbackSourceRestored = envelope.Payload.SourceRestored;
        plan.RollbackRestartPoliciesRestored = envelope.Payload.RestartPoliciesRestored;
        plan.RollbackOriginalRunningStatesRestored = envelope.Payload.OriginalRunningStatesRestored;
        plan.RollbackMatrixVerified = envelope.Payload.MatrixVerified;
        plan.RollbackElementVerified = envelope.Payload.ElementVerified;
        plan.RollbackTargetAuthorityVerified = envelope.Payload.TargetRollbackAuthorityVerified;
        plan.RollbackTargetIntegrityVerified = true;
        plan.RollbackDevelopmentExternalControlPlane = envelope.Payload.DevelopmentExternalControlPlane;
        plan.RollbackStatus = "coordinated-rollback-complete";
        plan.CutoverStatus = "coordinated-rollback-complete";
        plan.Status = "coordinated-rollback-complete";
        plan.PublicRoutesCreated = false;
        plan.RuntimePromotionCompleted = false;
        plan.BlockerSummary = envelope.Payload.DevelopmentExternalControlPlane
            ? "Coordinated rollback is complete for the development coexistence fixture. The external legacy API/Web verification remains development-only evidence and is not production qualification."
            : null;
        plan.UpdatedAtUtc = now;
        plan.RollbackEvidenceJson = JsonSerializer.Serialize(new
        {
            migrationId,
            plan.AdoptionPlanId,
            plan.RollbackExecutionId,
            plan.RollbackSourceHandoffId,
            sourceRestorationAttemptId = envelope.Payload.RestorationAttemptId,
            sourceRestorationCompletionSha256 = envelope.PayloadSha256,
            envelope.Payload.FreezeAttemptId,
            envelope.Payload.FreezePlanId,
            envelope.Payload.FreezePlanHash,
            envelope.Payload.SourceFingerprint,
            envelope.Payload.SourceRestored,
            envelope.Payload.RestartPoliciesRestored,
            envelope.Payload.OriginalRunningStatesRestored,
            envelope.Payload.MatrixVerified,
            envelope.Payload.ElementVerified,
            envelope.Payload.TargetRollbackAuthorityVerified,
            targetRollbackStillIntact = true,
            envelope.Payload.DevelopmentExternalControlPlane,
            coordinatedRollbackCompletedAtUtc = now,
        }, JsonOptions);

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Coordinated migration rollback closed. MigrationId={MigrationId} AdoptionPlanId={AdoptionPlanId} RestorationAttemptId={RestorationAttemptId}",
            migrationId,
            plan.AdoptionPlanId,
            envelope.Payload.RestorationAttemptId);
        return await adoptionService.GetStateAsync(migrationId, ct);
    }

    internal static string ComputeCompletionPayloadSha256(
        MigrationProductionSourceRestorationCompletionPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    internal static void ValidateCompletionEnvelope(
        MigrationProductionSourceRestorationCompletionEnvelope envelope)
    {
        if (!string.Equals(
                envelope.SchemaVersion,
                CompletionSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported source-restoration completion schema '{envelope.SchemaVersion}'.");
        }

        var actual = ComputeCompletionPayloadSha256(envelope.Payload);
        if (!IsSha256(envelope.PayloadSha256) ||
            !string.Equals(actual, envelope.PayloadSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The source-restoration completion payload hash is missing or invalid.");
        }
    }

    internal static void ValidateCompletionBindings(
        MigrationProductionSourceRestorationCompletionPayload completion,
        MigrationProductionSourceRestorationHandoffPayload handoff)
    {
        var mismatches = new List<string>();
        AddMismatch(mismatches, "migration", completion.MigrationId, handoff.MigrationId);
        AddMismatch(mismatches, "source migration", completion.SourceMigrationId, handoff.SourceMigrationId);
        AddMismatch(mismatches, "target rollback execution", completion.TargetRollbackExecutionId, handoff.TargetRollbackExecutionId);
        AddMismatch(mismatches, "source handoff", completion.SourceHandoffId, handoff.HandoffId);
        AddMismatch(mismatches, "source handoff hash", completion.SourceHandoffSha256, MigrationProductionRollbackService.ComputePayloadSha256(handoff));
        AddMismatch(mismatches, "source fingerprint", completion.SourceFingerprint, handoff.SourceFingerprint);
        AddMismatch(mismatches, "source stack", completion.SourceStackSlug, handoff.SourceStackSlug);
        AddMismatch(mismatches, "Matrix server name", completion.MatrixServerName, handoff.MatrixServerName);

        if (!RouteEvidenceMatches(completion.TargetRouteEvidence, handoff.Routes))
        {
            mismatches.Add("target route evidence");
        }

        if (mismatches.Count > 0)
        {
            throw new InvalidDataException(
                $"Source-restoration completion does not bind to the target rollback handoff: {string.Join(", ", mismatches)}.");
        }
    }

    internal static void ValidateSourceRestorationProof(
        MigrationProductionSourceRestorationCompletionPayload completion)
    {
        if (string.IsNullOrWhiteSpace(completion.RestorationAttemptId) ||
            string.IsNullOrWhiteSpace(completion.FreezeAttemptId) ||
            string.IsNullOrWhiteSpace(completion.FreezePlanId) ||
            !IsSha256(completion.FreezePlanHash) ||
            !IsSha256(completion.SourceFingerprint))
        {
            throw new InvalidDataException(
                "Source-restoration completion identity or freeze authority is incomplete.");
        }

        if (!completion.SourceRestored ||
            !completion.RestartPoliciesRestored ||
            !completion.OriginalRunningStatesRestored ||
            !completion.MatrixVerified ||
            !completion.ElementVerified ||
            !completion.TargetRollbackAuthorityVerified)
        {
            throw new InvalidDataException(
                "Source-restoration completion does not prove full source restoration and verification.");
        }

        foreach (var role in new[] { "matrix", "element" })
        {
            var matches = completion.Containers.Where(container =>
                    string.Equals(container.Role, role, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length != 1 ||
                !matches[0].RestartPolicyRestored ||
                !matches[0].RunningStateRestored ||
                !matches[0].ServiceVerified ||
                string.IsNullOrWhiteSpace(matches[0].ContainerId) ||
                string.IsNullOrWhiteSpace(matches[0].ImageId))
            {
                throw new InvalidDataException(
                    $"Source-restoration completion does not contain one fully verified '{role}' container result.");
            }
        }

        if (completion.Containers.Any(container =>
                container.WasRunning && !container.RunningStateRestored) ||
            completion.Containers.Any(container => !container.RestartPolicyRestored))
        {
            throw new InvalidDataException(
                "One or more source containers did not restore their captured running state or restart policy.");
        }
    }

    private async Task EnsureTargetRollbackStillIntactAsync(
        MigrationProductionAdoptionEntity plan,
        MigrationProductionSourceRestorationHandoffPayload handoff,
        CancellationToken ct)
    {
        if (await db.RuntimeRoutes.AsNoTracking()
            .AnyAsync(route => route.RuntimeStackId == plan.RuntimeStackId, ct))
        {
            throw new InvalidOperationException(
                "Target rollback is no longer intact because normal RuntimeRoute ownership reappeared.");
        }

        foreach (var target in handoff.TargetContainers)
        {
            var inspect = await docker.Containers.InspectContainerAsync(target.ContainerName, ct);
            if (inspect.State?.Running is true ||
                (!string.IsNullOrWhiteSpace(target.ContainerId) &&
                 !string.Equals(inspect.ID, target.ContainerId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Target rollback is no longer intact for container '{target.ContainerName}'.");
            }
        }

        foreach (var route in handoff.Routes)
        {
            var current = await npmProxyHostService.GetByDomainAsync(route.PublicHost, ct);
            if (string.Equals(route.RestoredState, "absent", StringComparison.Ordinal))
            {
                if (current is not null ||
                    !string.Equals(route.SnapshotSha256, Sha256("absent"), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Target rollback route '{route.PublicHost}' is no longer absent as recorded.");
                }
                continue;
            }

            if (current is null)
            {
                throw new InvalidOperationException(
                    $"Target rollback route '{route.PublicHost}' disappeared after source handoff.");
            }

            var snapshot = NpmProxyHostService.CaptureSnapshot(current);
            if (route.RouteId != snapshot.Id ||
                !string.Equals(route.ForwardScheme, snapshot.ForwardScheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(route.ForwardHost, snapshot.ForwardHost, StringComparison.OrdinalIgnoreCase) ||
                route.ForwardPort != snapshot.ForwardPort ||
                route.Enabled != snapshot.Enabled ||
                !string.Equals(route.SnapshotSha256, SnapshotSha256(snapshot), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Target rollback route '{route.PublicHost}' drifted after the source handoff was issued.");
            }
        }
    }

    private async Task EnsureNotAcceptedAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        if (await db.MigrationAcceptances.AsNoTracking()
                .AnyAsync(item => item.MigrationIntakeEntityId == plan.MigrationIntakeEntityId, ct))
        {
            throw new InvalidOperationException(
                "A migration that crossed the acceptance boundary cannot close pre-acceptance rollback.");
        }
    }

    private static void ValidateTargetRollbackState(MigrationProductionAdoptionEntity plan)
    {
        if (!string.Equals(plan.Status, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.RollbackStatus, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) ||
            !plan.RollbackRoutesRestored ||
            !plan.RollbackRuntimeRoutesRemoved ||
            !plan.RollbackTargetContainersStopped ||
            string.IsNullOrWhiteSpace(plan.RollbackExecutionId) ||
            string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffId) ||
            string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffSha256) ||
            string.IsNullOrWhiteSpace(plan.RollbackSourceHandoffJson))
        {
            throw new InvalidOperationException(
                "Target rollback and its source-restoration handoff must be complete before importing source completion evidence.");
        }
    }

    private static MigrationProductionSourceRestorationHandoffEnvelope ReadAndValidateHandoff(
        MigrationProductionAdoptionEntity plan)
    {
        MigrationProductionSourceRestorationHandoffEnvelope handoff;
        try
        {
            handoff = JsonSerializer.Deserialize<MigrationProductionSourceRestorationHandoffEnvelope>(
                          plan.RollbackSourceHandoffJson!, JsonOptions)
                      ?? throw new InvalidDataException(
                          "The stored source-restoration handoff is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "The stored source-restoration handoff is invalid JSON.", ex);
        }

        var actual = MigrationProductionRollbackService.ComputePayloadSha256(handoff.Payload);
        if (!string.Equals(handoff.SchemaVersion, MigrationProductionRollbackService.SourceHandoffSchemaVersion, StringComparison.Ordinal) ||
            !string.Equals(actual, handoff.PayloadSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actual, plan.RollbackSourceHandoffSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(handoff.Payload.HandoffId, plan.RollbackSourceHandoffId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The stored source-restoration handoff no longer matches its durable identity and hash.");
        }

        return handoff;
    }

    private async Task<MigrationProductionAdoptionEntity?> LoadPlanAsync(
        string migrationId,
        bool tracking,
        CancellationToken ct)
    {
        IQueryable<MigrationProductionAdoptionEntity> query = db.MigrationProductionAdoptions
            .Include(item => item.MigrationIntake);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }
        return await query.SingleOrDefaultAsync(
            item => item.MigrationIntake.IntakeId == migrationId,
            ct);
    }

    private static bool RouteEvidenceMatches(
        IReadOnlyList<MigrationProductionSourceRestorationRouteEvidence> left,
        IReadOnlyList<MigrationProductionSourceRestorationRouteEvidence> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var expected = right.ToDictionary(item => item.ServiceKey, StringComparer.OrdinalIgnoreCase);
        foreach (var item in left)
        {
            if (!expected.TryGetValue(item.ServiceKey, out var match) ||
                !string.Equals(item.PublicHost, match.PublicHost, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.RestoredState, match.RestoredState, StringComparison.Ordinal) ||
                item.RouteId != match.RouteId ||
                !string.Equals(item.ForwardScheme, match.ForwardScheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(item.ForwardHost, match.ForwardHost, StringComparison.OrdinalIgnoreCase) ||
                item.ForwardPort != match.ForwardPort ||
                item.Enabled != match.Enabled ||
                !string.Equals(item.SnapshotSha256, match.SnapshotSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static void AddMismatch(
        ICollection<string> mismatches,
        string label,
        string? actual,
        string? expected)
    {
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            mismatches.Add(label);
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string SnapshotSha256(NpmProxyHostSnapshot snapshot) =>
        Sha256(JsonSerializer.Serialize(snapshot, JsonOptions));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
