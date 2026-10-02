using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;
using HostAgent.Runtime.Backups.Catalog;

namespace HostAgent.Runtime.Backups.AdvancedCutover.PostCutover;

/// <summary>
/// Safe, frontend-oriented projection of catalog-backed public cutover history.
/// It deliberately returns no host filesystem paths, container identifiers,
/// raw route configuration, or archived execution payloads.
/// </summary>
public sealed class CatalogPostCutoverProjectionService
{
    private readonly BackupCatalogStore _catalogStore;
    private readonly CatalogPublicCutoverExecutionHistoryService _executionHistory;

    public CatalogPostCutoverProjectionService(
        BackupCatalogStore catalogStore,
        CatalogPublicCutoverExecutionHistoryService executionHistory)
    {
        _catalogStore = catalogStore;
        _executionHistory = executionHistory;
    }

    public async Task<CatalogPostCutoverProjectionResponse?> GetAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var catalog = await _catalogStore.FindByCatalogEntryIdAsync(catalogEntryId, ct);
        if (catalog is null)
        {
            return null;
        }

        var history = await _executionHistory.ListExecutionsAsync(
            catalogEntryId,
            confirmationId: null,
            candidateId: null,
            status: null,
            max: 100,
            ct);

        var latest = history.Executions
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefault();

        var completed = history.Executions
            .Where(x => string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.FinishedAtUtc ?? x.StartedAtUtc)
            .FirstOrDefault();

        var rolledBack = latest is not null &&
            (latest.RollbackCompleted ||
             string.Equals(latest.Status, "failed_rolled_back", StringComparison.OrdinalIgnoreCase));

        var state = completed is not null
            ? "cutover_completed"
            : rolledBack
                ? "cutover_rolled_back"
                : latest is null
                    ? "not_started"
                    : "not_completed";

        var execution = completed ?? latest;
        var verificationPassed = completed?.PublicVerificationPassed == true;
        var routeTransitionCompleted = completed?.AnyRouteMutated == true;
        var finalBackupCaptured = completed?.FinalBackupCaptured == true;

        var warnings = new List<string>();
        if (catalog.WarningCount > 0)
        {
            warnings.Add($"Backup Catalog retains {catalog.WarningCount} validation warning(s) for operator review.");
        }

        if (latest is not null && latest.RollbackAttempted)
        {
            warnings.Add(latest.RollbackCompleted
                ? "The latest cutover attempt performed a rollback. Review execution history before further action."
                : "The latest cutover attempt attempted rollback but did not report completion. Operator investigation is required.");
        }

        return new CatalogPostCutoverProjectionResponse(
            Source: "control-plane",
            Status: "ok",
            CatalogEntryId: catalog.CatalogEntryId,
            RestoreSessionId: execution?.RestoreSessionId,
            Catalog: new CatalogPostCutoverCatalogSummary(
                catalog.CatalogEntryId,
                catalog.OriginKind,
                catalog.DisplayName,
                catalog.SourceStackSlug,
                catalog.PayloadState,
                catalog.IntegrityStatus,
                catalog.WarningCount),
            Cutover: new CatalogPostCutoverState(
                state,
                execution is not null,
                routeTransitionCompleted,
                verificationPassed,
                finalBackupCaptured,
                rolledBack,
                execution?.OldRuntimeRetainedForRollback == true,
                execution?.ExecutionId,
                execution?.CandidateId,
                execution?.ConfirmationId,
                execution?.StartedAtUtc,
                execution?.FinishedAtUtc,
                execution?.Detail),
            LatestExecution: latest is null ? null : ToSafeSummary(latest),
            LatestSuccessfulExecution: completed is null ? null : ToSafeSummary(completed),
            ExecutionCount: history.TotalExecutions,
            CanOpenRestoreWorkspace: !string.IsNullOrWhiteSpace(execution?.RestoreSessionId),
            Warnings: warnings,
            Detail: state switch
            {
                "cutover_completed" => "Catalog-backed public cutover completed and public verification passed. The restore workspace remains available for operator evidence and follow-up checks.",
                "cutover_rolled_back" => "The latest cutover attempt was rolled back. No post-cutover success state is presented.",
                "not_started" => "No catalog-backed public cutover execution has been recorded for this backup.",
                _ => "A catalog-backed cutover execution exists but has not reached a verified completed state."
            });
    }

    private static CatalogPostCutoverExecutionSummary ToSafeSummary(
        CatalogPublicCutoverExecutionSummary execution) =>
        new(
            execution.ExecutionId,
            execution.Status,
            execution.RestoreSessionId,
            execution.CandidateId,
            execution.ConfirmationId,
            execution.OldRuntimeStackSlug,
            execution.StartedAtUtc,
            execution.FinishedAtUtc,
            execution.ExecutionRequested,
            execution.FinalBackupCaptured,
            execution.OldRuntimeRetainedForRollback,
            execution.AnyRouteMutated,
            execution.PublicVerificationPassed,
            execution.RollbackAttempted,
            execution.RollbackCompleted,
            execution.BlockerCount,
            execution.WarningCount,
            execution.ErrorCount,
            execution.Detail);
}

public sealed record CatalogPostCutoverProjectionResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string? RestoreSessionId,
    CatalogPostCutoverCatalogSummary Catalog,
    CatalogPostCutoverState Cutover,
    CatalogPostCutoverExecutionSummary? LatestExecution,
    CatalogPostCutoverExecutionSummary? LatestSuccessfulExecution,
    int ExecutionCount,
    bool CanOpenRestoreWorkspace,
    IReadOnlyList<string> Warnings,
    string Detail);

public sealed record CatalogPostCutoverCatalogSummary(
    string CatalogEntryId,
    string OriginKind,
    string DisplayName,
    string? SourceStackSlug,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount);

public sealed record CatalogPostCutoverState(
    string State,
    bool ExecutionRecorded,
    bool RouteTransitionCompleted,
    bool PublicVerificationPassed,
    bool FinalBackupCaptured,
    bool RolledBack,
    bool OldRuntimeRetainedForRollback,
    string? ExecutionId,
    string? CandidateId,
    string? ConfirmationId,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? Detail);

public sealed record CatalogPostCutoverExecutionSummary(
    string ExecutionId,
    string Status,
    string RestoreSessionId,
    string CandidateId,
    string ConfirmationId,
    string OldRuntimeStackSlug,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    bool ExecutionRequested,
    bool FinalBackupCaptured,
    bool OldRuntimeRetainedForRollback,
    bool AnyRouteMutated,
    bool PublicVerificationPassed,
    bool RollbackAttempted,
    bool RollbackCompleted,
    int BlockerCount,
    int WarningCount,
    int ErrorCount,
    string? Detail);
