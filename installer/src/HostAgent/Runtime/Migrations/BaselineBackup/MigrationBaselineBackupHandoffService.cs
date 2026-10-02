using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Migrations.Staging;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Migrations.BaselineBackup;

public sealed class MigrationBaselineBackupHandoffService(
    MemDbContext db,
    LocalBackupCaptureService backupCaptureService,
    RuntimeStackManifestStore manifestStore,
    MigrationCompletedStagingCleanupService stagingCleanupService,
    TimeProvider timeProvider,
    ILogger<MigrationBaselineBackupHandoffService> logger)
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<MigrationBaselineBackupStateResponse> GetStateAsync(
        string migrationId,
        CancellationToken ct)
    {
        var intake = await LoadAsync(migrationId, tracking: false, ct);
        if (intake.Acceptance is null)
        {
            return new MigrationBaselineBackupStateResponse(
                "control-plane",
                "blocked",
                migrationId,
                false,
                false,
                null,
                ["Migration acceptance is required before the first native baseline backup can be created."],
                "Baseline Backup handoff remains unavailable until the migration is accepted.");
        }

        var handoff = intake.BaselineBackupHandoff ?? intake.Acceptance.BaselineBackupHandoff;
        if (handoff is null)
        {
            return new MigrationBaselineBackupStateResponse(
                "control-plane",
                "pending",
                migrationId,
                true,
                true,
                null,
                Array.Empty<string>(),
                "Migration is accepted. The first native MEM baseline backup of the adopted Runtime Stack is pending.");
        }

        return BuildState(migrationId, handoff);
    }

    public async Task<MigrationBaselineBackupStateResponse> EnsureAndAttemptAsync(
        string migrationId,
        CancellationToken ct)
    {
        var intake = await LoadAsync(migrationId, tracking: true, ct);
        var acceptance = intake.Acceptance
            ?? throw new InvalidOperationException(
                "Migration acceptance is required before baseline backup handoff.");
        var plan = intake.ProductionAdoption
            ?? throw new InvalidOperationException(
                "The accepted normal-runtime production adoption plan could not be resolved.");
        if (!string.Equals(plan.Status, "accepted-baseline-backup-pending", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(plan.Status, "accepted-baseline-backup-failed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(plan.Status, "accepted-baseline-backup-created", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production adoption plan '{plan.AdoptionPlanId}' is not in the accepted baseline-backup lifecycle.");
        }

        var handoff = intake.BaselineBackupHandoff ?? acceptance.BaselineBackupHandoff;
        if (handoff is null)
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            handoff = new MigrationBaselineBackupHandoffEntity
            {
                Id = Guid.NewGuid(),
                HandoffId = CreateId("mbh", now),
                MigrationIntakeEntityId = intake.Id,
                MigrationAcceptanceEntityId = acceptance.Id,
                Status = "pending",
                AttemptCount = 0,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                TargetStackSlug = plan.TargetStackSlug,
                CandidateId = plan.CandidateArtifact.CandidateArtifactId,
                // Retained schema name for backwards compatibility. This now records the
                // authoritative normal Runtime Stack identity rather than private staging.
                PrivateRuntimeId = plan.RuntimeStackId.ToString("D"),
            };
            intake.BaselineBackupHandoff = handoff;
            acceptance.BaselineBackupHandoff = handoff;
            db.MigrationBaselineBackupHandoffs.Add(handoff);
            await db.SaveChangesAsync(ct);
        }

        if (string.Equals(handoff.Status, "created", StringComparison.OrdinalIgnoreCase))
        {
            await TryCleanupCompletedStagingAsync(
                migrationId,
                plan.StagingRun.StagingRunId);
            return BuildState(migrationId, handoff);
        }

        var started = timeProvider.GetUtcNow().UtcDateTime;
        handoff.Status = "pending";
        handoff.AttemptCount++;
        handoff.StartedAtUtc = started;
        handoff.CompletedAtUtc = null;
        handoff.UpdatedAtUtc = started;
        handoff.FailureCode = null;
        handoff.FailureSummary = null;
        plan.Status = "accepted-baseline-backup-pending";
        plan.UpdatedAtUtc = started;
        await db.SaveChangesAsync(ct);

        try
        {
            var capture = await backupCaptureService.BackupAsync(
                plan.RuntimeStackId.ToString("D"),
                ct);

            if (capture.RuntimeStackId != plan.RuntimeStackId ||
                !string.Equals(capture.StackSlug, plan.TargetStackSlug, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The native baseline backup did not resolve the accepted normal Runtime Stack identity.");
            }
            if (string.IsNullOrWhiteSpace(capture.CatalogEntryId))
            {
                throw new InvalidOperationException(
                    "The native baseline backup payload was captured, but Backup Catalog registration did not complete.");
            }

            var completed = timeProvider.GetUtcNow().UtcDateTime;
            handoff.Status = "created";
            handoff.CompletedAtUtc = completed;
            handoff.UpdatedAtUtc = completed;
            handoff.BackupId = capture.BackupId;
            handoff.CatalogEntryId = capture.CatalogEntryId;
            handoff.BackupCreatedAtUtc = capture.CreatedAtUtc;
            handoff.BackupTotalBytes = capture.Stats.TotalBytes;
            handoff.BackupTotalFiles = capture.Stats.TotalFiles;
            handoff.BackupWarningCount = capture.Warnings.Count;
            plan.Status = "accepted-baseline-backup-created";
            plan.BlockerSummary = null;
            plan.UpdatedAtUtc = completed;
            await MarkNormalLifecycleAsync(
                plan,
                handoff,
                "ready",
                completed,
                "The accepted migrated Runtime Stack owns the normal MEM Backup/Restore lifecycle.",
                ct);
            await db.SaveChangesAsync(ct);

            // The verified staging runtime remains authoritative through production
            // materialisation, cutover, verification, acceptance, and the first native
            // baseline backup. Only after that complete recovery boundary is durable is
            // the disposable private-test runtime removed.
            await TryCleanupCompletedStagingAsync(
                migrationId,
                plan.StagingRun.StagingRunId);
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            await MarkFailureAsync(plan, handoff, "baseline_backup_cancelled", ex.Message, CancellationToken.None);
        }
        catch (Exception ex)
        {
            await MarkFailureAsync(plan, handoff, "baseline_backup_failed", ex.Message, CancellationToken.None);
        }

        return BuildState(migrationId, handoff);
    }

    private async Task TryCleanupCompletedStagingAsync(
        string migrationId,
        string stagingRunId)
    {
        try
        {
            await stagingCleanupService.CleanupAsync(
                migrationId,
                stagingRunId,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Acceptance and the first native baseline backup are already durable. Cleanup is
            // deliberately best-effort and must never rewind or misclassify that recovery boundary.
            logger.LogWarning(
                exception,
                "Completed migration private-test cleanup failed outside its normal evidence path. MigrationId={MigrationId} StagingRunId={StagingRunId}",
                migrationId,
                stagingRunId);
        }
    }

    private async Task MarkFailureAsync(
        MigrationProductionAdoptionEntity plan,
        MigrationBaselineBackupHandoffEntity handoff,
        string failureCode,
        string failureSummary,
        CancellationToken ct)
    {
        var failed = timeProvider.GetUtcNow().UtcDateTime;
        handoff.Status = "failed";
        handoff.CompletedAtUtc = failed;
        handoff.UpdatedAtUtc = failed;
        handoff.FailureCode = failureCode;
        handoff.FailureSummary = RestoreDiagnosticRedactor.RedactText(failureSummary, 1000);
        plan.Status = "accepted-baseline-backup-failed";
        plan.BlockerSummary = "Migration acceptance is durable, but the first native baseline backup must be retried.";
        plan.UpdatedAtUtc = failed;
        await MarkNormalLifecycleAsync(
            plan,
            handoff,
            "migration-accepted-baseline-failed",
            failed,
            "Migration acceptance remains durable. The first native baseline backup failed and is retryable.",
            ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task MarkNormalLifecycleAsync(
        MigrationProductionAdoptionEntity plan,
        MigrationBaselineBackupHandoffEntity handoff,
        string status,
        DateTime observedAtUtc,
        string warning,
        CancellationToken ct)
    {
        var stack = await db.RuntimeStacks
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .SingleOrDefaultAsync(x => x.Id == plan.RuntimeStackId, ct)
            ?? throw new InvalidOperationException("The accepted normal Runtime Stack record was not found.");
        stack.Status = status;
        stack.LastVerifiedStatus = status;
        stack.LastVerifiedAtUtc = plan.ProductionVerificationCompletedAtUtc;
        stack.UpdatedAtUtc = observedAtUtc;
        stack.LastError = string.Equals(status, "ready", StringComparison.Ordinal) ? null : handoff.FailureSummary;
        foreach (var service in stack.ServiceInstances)
        {
            service.Status = status;
            service.LastObservedAtUtc = observedAtUtc;
            service.UpdatedAtUtc = observedAtUtc;
            service.LastError = stack.LastError;
        }
        foreach (var route in stack.Routes)
        {
            route.Status = "verified";
            route.LastVerifiedAtUtc = plan.ProductionVerificationCompletedAtUtc;
            route.LastError = null;
        }

        var manifest = await manifestStore.FindAsync(plan.RuntimeStackId.ToString(), ct)
            ?? throw new InvalidOperationException("The accepted normal runtime manifest was not found.");
        var metadata = manifest.Metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        metadata["normalBackupLifecycle"] = handoff.Status;
        metadata["migrationBaselineHandoffId"] = handoff.HandoffId;
        metadata["migrationBaselineBackupId"] = handoff.BackupId;
        metadata["migrationBaselineCatalogEntryId"] = handoff.CatalogEntryId;
        var matrixMetadata = manifest.Matrix.RuntimeMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        matrixMetadata["normalBackupLifecycle"] = handoff.Status;
        RuntimeStackServiceManifest? element = null;
        if (manifest.Element is not null)
        {
            var elementMetadata = manifest.Element.RuntimeMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            elementMetadata["normalBackupLifecycle"] = handoff.Status;
            element = manifest.Element with { RuntimeMetadata = elementMetadata };
        }
        var updated = manifest with
        {
            LastVerifiedStatus = status,
            LastVerifiedAtUtc = new DateTimeOffset(observedAtUtc, TimeSpan.Zero),
            Matrix = manifest.Matrix with { RuntimeMetadata = matrixMetadata },
            Element = element,
            Warnings = [warning],
            Metadata = metadata,
        };
        var path = Path.GetFullPath(plan.ManifestPath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The accepted normal runtime manifest file was not found.", path);
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(updated, ManifestJsonOptions), ct);
    }

    private async Task<MigrationIntakeEntity> LoadAsync(
        string migrationId,
        bool tracking,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new FileNotFoundException("Migration Session was not found.");
        }

        IQueryable<MigrationIntakeEntity> query = db.MigrationIntakes
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.BaselineBackupHandoff)
            .Include(x => x.BaselineBackupHandoff)
            .Include(x => x.ProductionAdoption)!
                .ThenInclude(x => x!.CandidateArtifact)
            .Include(x => x.ProductionAdoption)!
                .ThenInclude(x => x!.StagingRun);
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(x => x.IntakeId == migrationId, ct)
            ?? throw new FileNotFoundException("Migration Session was not found.");
    }

    internal static MigrationBaselineBackupStateResponse BuildState(
        string migrationId,
        MigrationBaselineBackupHandoffEntity handoff)
    {
        var created = string.Equals(handoff.Status, "created", StringComparison.OrdinalIgnoreCase);
        var failed = string.Equals(handoff.Status, "failed", StringComparison.OrdinalIgnoreCase);
        var detail = created
            ? "The first native MEM baseline backup of the accepted Runtime Stack was created and entered the Backup Catalog. Normal Backup/Restore lifecycle is now available."
            : failed
                ? "Migration acceptance remains durable, but the first native baseline backup failed. Retry is available and acceptance is not rolled back."
                : "Migration acceptance is durable and the first native MEM baseline backup of the accepted Runtime Stack is pending.";

        return new MigrationBaselineBackupStateResponse(
            "control-plane",
            handoff.Status,
            migrationId,
            true,
            !created,
            ToSummary(handoff, detail),
            Array.Empty<string>(),
            detail);
    }

    internal static MigrationBaselineBackupSummary ToSummary(
        MigrationBaselineBackupHandoffEntity handoff,
        string? detail = null) =>
        new(
            handoff.HandoffId,
            handoff.Status,
            handoff.AttemptCount,
            handoff.CreatedAtUtc,
            handoff.UpdatedAtUtc,
            handoff.StartedAtUtc,
            handoff.CompletedAtUtc,
            handoff.TargetStackSlug,
            handoff.CandidateId,
            handoff.PrivateRuntimeId,
            handoff.BackupId,
            handoff.CatalogEntryId,
            handoff.BackupCreatedAtUtc,
            handoff.BackupTotalBytes,
            handoff.BackupTotalFiles,
            handoff.BackupWarningCount,
            handoff.FailureCode,
            handoff.FailureSummary,
            detail ?? handoff.Status);

    private static string CreateId(string prefix, DateTime utc) =>
        $"{prefix}_{utc:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}";
}
