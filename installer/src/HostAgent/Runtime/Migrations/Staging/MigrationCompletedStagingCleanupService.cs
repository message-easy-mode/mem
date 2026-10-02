using HostAgent.Runtime.Backups.Observability;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.Staging;

public sealed record MigrationCompletedStagingCleanupResult(
    string Status,
    string MigrationId,
    string StagingRunId,
    DateTime? DestroyedAtUtc,
    string? FailureCode,
    string? FailureSummary);

/// <summary>
/// Removes the disposable private-test runtime only after the migrated normal Runtime Stack has
/// been accepted and its first native MEM baseline backup has completed. Verification evidence
/// remains durable on the MigrationStagingRun record.
/// </summary>
public sealed class MigrationCompletedStagingCleanupService(
    MemDbContext db,
    IMigrationPrivateStagingRunner runner,
    TimeProvider timeProvider,
    ILogger<MigrationCompletedStagingCleanupService> logger,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    public async Task<MigrationCompletedStagingCleanupResult> CleanupAsync(
        string migrationId,
        string stagingRunId,
        CancellationToken cancellationToken)
    {
        var run = await db.MigrationStagingRuns
            .Include(x => x.MigrationIntake)
            .SingleOrDefaultAsync(
                x => x.MigrationIntake.IntakeId == migrationId &&
                     x.StagingRunId == stagingRunId,
                cancellationToken);

        if (run is null)
        {
            logger.LogWarning(
                "Completed migration staging cleanup could not resolve the bound run. MigrationId={MigrationId} StagingRunId={StagingRunId}",
                migrationId,
                stagingRunId);
            await RecordCleanupAsync(
                migrationId,
                stagingRunId,
                eventCode: "migration.staging.cleanup.not_found",
                severity: MemDiagnosticSeverities.Warning,
                message: "The completed migration staging run could not be resolved for cleanup.",
                details: new Dictionary<string, string?>
                {
                    ["failureCode"] = "staging_cleanup_run_not_found"
                });
            return new MigrationCompletedStagingCleanupResult(
                "not-found",
                migrationId,
                stagingRunId,
                null,
                "staging_cleanup_run_not_found",
                "The verified private-test record could not be resolved for cleanup.");
        }

        if (run.DestroyedAtUtc is not null)
        {
            if (run.ActiveMigrationKey is not null)
            {
                run.ActiveMigrationKey = null;
                run.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
                await db.SaveChangesAsync(cancellationToken);
            }

            return ToResult("already-removed", migrationId, run);
        }

        if (!string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Completed migration staging cleanup refused a non-verified run. MigrationId={MigrationId} StagingRunId={StagingRunId} Status={Status}",
                migrationId,
                stagingRunId,
                run.Status);
            await RecordCleanupAsync(
                migrationId,
                stagingRunId,
                eventCode: "migration.staging.cleanup.not_eligible",
                severity: MemDiagnosticSeverities.Warning,
                message: "Automatic staging cleanup was refused because the private test was not durably verified.",
                details: new Dictionary<string, string?>
                {
                    ["failureCode"] = "staging_cleanup_not_verified",
                    ["status"] = run.Status
                });
            return new MigrationCompletedStagingCleanupResult(
                "not-eligible",
                migrationId,
                stagingRunId,
                null,
                "staging_cleanup_not_verified",
                "Only a durably verified private-test runtime can be removed automatically.");
        }

        if (string.IsNullOrWhiteSpace(run.PrivateRuntimeStagingId))
        {
            return await MarkFailureAsync(
                migrationId,
                run,
                "staging_cleanup_identity_missing",
                "The private-test runtime identity is unavailable for automatic cleanup.",
                cancellationToken);
        }

        try
        {
            var destroyResult = await runner.DestroyAsync(
                run.PrivateRuntimeStagingId,
                cancellationToken);
            if (destroyResult.Destroy is null &&
                !string.Equals(destroyResult.Status, "destroyed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The private staging engine did not confirm runtime destruction.");
            }

            var destroyedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            run.DestroyedAtUtc = destroyedAtUtc;
            run.CurrentStep = "private-verification-complete-runtime-removed";
            run.UpdatedAtUtc = destroyedAtUtc;
            run.ActiveMigrationKey = null;
            run.FailureCode = null;
            run.FailureSummary = null;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Removed completed migration private-test runtime. MigrationId={MigrationId} StagingRunId={StagingRunId} PrivateRuntimeStagingId={PrivateRuntimeStagingId}",
                migrationId,
                stagingRunId,
                run.PrivateRuntimeStagingId);
            await RecordCleanupAsync(
                migrationId,
                stagingRunId,
                eventCode: "migration.staging.cleanup.completed",
                severity: MemDiagnosticSeverities.Information,
                message: "The completed migration private-test runtime was removed.",
                details: new Dictionary<string, string?>
                {
                    ["status"] = "removed"
                });
            return ToResult("removed", migrationId, run);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return await MarkFailureAsync(
                migrationId,
                run,
                "staging_cleanup_failed",
                RestoreDiagnosticRedactor.RedactText(exception.Message, 500),
                CancellationToken.None,
                exception);
        }
    }

    private async Task<MigrationCompletedStagingCleanupResult> MarkFailureAsync(
        string migrationId,
        Infrastructure.Data.Entities.Migrations.MigrationStagingRunEntity run,
        string failureCode,
        string failureSummary,
        CancellationToken cancellationToken,
        Exception? exception = null)
    {
        var updatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        run.CurrentStep = "private-verification-complete-cleanup-required";
        run.FailureCode = failureCode;
        run.FailureSummary = string.IsNullOrWhiteSpace(failureSummary)
            ? "The temporary private-test runtime could not be removed."
            : failureSummary;
        run.UpdatedAtUtc = updatedAtUtc;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            exception,
            "Completed migration private-test cleanup requires attention. MigrationId={MigrationId} StagingRunId={StagingRunId} FailureCode={FailureCode}",
            migrationId,
            run.StagingRunId,
            failureCode);
        await RecordCleanupAsync(
            migrationId,
            run.StagingRunId,
            eventCode: "migration.staging.cleanup.requires_attention",
            severity: MemDiagnosticSeverities.Warning,
            message: "The completed migration private-test runtime requires manual cleanup.",
            createIncident: true,
            exception: exception,
            details: new Dictionary<string, string?>
            {
                ["failureCode"] = failureCode,
                ["failureSummary"] = run.FailureSummary
            });
        return ToResult("cleanup-required", migrationId, run);
    }

    private Task<MemDiagnosticWriteResult?> RecordCleanupAsync(
        string migrationId,
        string stagingRunId,
        string eventCode,
        string severity,
        string message,
        bool createIncident = false,
        Exception? exception = null,
        IReadOnlyDictionary<string, string?>? details = null) =>
        diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "host-agent.migration-cleanup",
            Feature: "migration",
            Stage: "staging-cleanup",
            Message: message,
            CreateIncident: createIncident,
            Resource: new MemDiagnosticResource(
                Kind: "migration",
                Id: migrationId,
                DisplayName: "Migration session",
                Service: "synapse",
                WorkspacePath: $"/migrations/{Uri.EscapeDataString(migrationId)}"),
            Details: MergeDetails(stagingRunId, details),
            Exception: exception,
            SuggestedAction: createIncident
                ? "Open the Migration Workspace and review staging cleanup before removing resources manually."
                : null,
            Retryable: createIncident));

    private static IReadOnlyDictionary<string, string?> MergeDetails(
        string stagingRunId,
        IReadOnlyDictionary<string, string?>? details)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["stagingRunId"] = stagingRunId
        };
        if (details is not null)
        {
            foreach (var pair in details)
            {
                result[pair.Key] = pair.Value;
            }
        }

        return result;
    }

    private static MigrationCompletedStagingCleanupResult ToResult(
        string status,
        string migrationId,
        Infrastructure.Data.Entities.Migrations.MigrationStagingRunEntity run) =>
        new(
            status,
            migrationId,
            run.StagingRunId,
            run.DestroyedAtUtc,
            run.FailureCode,
            run.FailureSummary);
}
