using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using HostAgent.Runtime.Backups.Catalog;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Readiness;

public sealed class CatalogPreCutoverReadinessService
{
    private readonly BackupCatalogStore _catalogStore;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly RuntimeStackBackupPublicCutoverPreviewHistoryService _previewHistory;
    private readonly CatalogPublicCutoverConfirmationHistoryService _confirmationHistory;
    private readonly CatalogPublicCutoverExecutionHistoryService _executionHistory;

    public CatalogPreCutoverReadinessService(
        BackupCatalogStore catalogStore,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
        CatalogPublicCutoverConfirmationHistoryService confirmationHistory,
        CatalogPublicCutoverExecutionHistoryService executionHistory)
    {
        _catalogStore = catalogStore;
        _candidateHistory = candidateHistory;
        _previewHistory = previewHistory;
        _confirmationHistory = confirmationHistory;
        _executionHistory = executionHistory;
    }

    public async Task<CatalogPreCutoverReadinessResponse> GetAsync(
        string catalogEntryId,
        string candidateId,
        string previewId,
        string confirmationId,
        CancellationToken ct)
    {
        var catalog = await _catalogStore.FindByCatalogEntryIdAsync(catalogEntryId, ct)
            ?? throw new CatalogRestoreSourceNotFoundException($"Backup Catalog entry '{catalogEntryId}' was not found.");
        var candidate = await _candidateHistory.GetCandidateAsync(candidateId, ct);
        var preview = await _previewHistory.GetPreviewAsync(previewId, ct);
        var confirmation = await _confirmationHistory.GetConfirmationAsync(catalogEntryId, confirmationId, ct);
        var executions = await _executionHistory.ListExecutionsAsync(catalogEntryId, confirmationId, candidateId, null, 1, ct);

        var blockers = new List<string>();
        var warnings = new List<string>();
        var linkage = candidate?.Candidate is not null && preview?.Preview is not null && confirmation?.Confirmation is not null;
        if (!linkage) blockers.Add("Candidate, preview, and confirmation records must all exist before cutover can be considered ready.");
        if (preview?.Preview?.CatalogEntryId != catalogEntryId) blockers.Add("Saved cutover preview is not linked to this Backup Catalog entry.");
        if (confirmation?.Confirmation?.CatalogEntryId != catalogEntryId) blockers.Add("Saved confirmation is not linked to this Backup Catalog entry.");
        if (candidate?.Candidate?.CatalogEntryId != catalogEntryId) blockers.Add("Private candidate is not linked to this Backup Catalog entry.");
        if (candidate?.Candidate?.PrivateRuntimeStatus != "ready" ||
     candidate.Candidate.Runtime.SynapseHealthPassed != true ||
     candidate.Candidate.Runtime.ElementHealthPassed != true)
        {
            blockers.Add("Private candidate is not currently healthy and ready.");
        }
        if (confirmation?.Confirmation?.ExecutionAvailable != true)
            blockers.Add("Confirmation gates do not currently permit execution.");
        if (preview?.Preview?.ProductionExecutionLocked != true || confirmation?.Confirmation?.ProductionExecutionLocked != true)
            blockers.Add("Readiness requires a saved locked preview and locked confirmation audit record.");
        if (catalog.WarningCount > 0) warnings.Add($"Backup Catalog retains {catalog.WarningCount} validation warning(s) for operator review.");

        var latest = executions.Executions.FirstOrDefault();
        var expectedOldOwnership = latest is not null && latest.OldRuntimeRetainedForRollback == false;
        var ready = blockers.Count == 0;
        return new CatalogPreCutoverReadinessResponse(
            "control-plane", ready ? "ready" : "blocked", catalogEntryId, catalog, candidate?.Candidate,
            preview?.Preview, confirmation?.Confirmation, latest,
            new CatalogPreCutoverReadinessRouteState(
                ExpectedOldRuntimeOwnsMatrixRoute: expectedOldOwnership,
                MatrixRouteAlreadyTargetsCandidate: false,
                Detail: expectedOldOwnership
                    ? "The nominated old runtime owns the Matrix route. This is an expected pre-cutover condition, not a route-conflict failure."
                    : "No latest execution-gate record is available to evidence expected old-runtime route ownership."),
            ready, blockers, warnings,
            ready ? "Pre-cutover readiness is satisfied. A separately confirmed execution request is still required." : "Pre-cutover readiness is blocked. No mutation was performed.");
    }
}

public sealed record CatalogPreCutoverReadinessRouteState(bool ExpectedOldRuntimeOwnsMatrixRoute, bool MatrixRouteAlreadyTargetsCandidate, string Detail);
public sealed record CatalogPreCutoverReadinessResponse(
    string Source, string Status, string CatalogEntryId, BackupCatalogDetailResponse Catalog,
    RuntimeStackBackupProductionCandidateResult? Candidate,
    RuntimeStackBackupPublicCutoverPreviewResult? Preview,
    CatalogPublicCutoverConfirmationResult? Confirmation,
    CatalogPublicCutoverExecutionSummary? LatestExecutionGate,
    CatalogPreCutoverReadinessRouteState RouteState,
    bool ExecutionReady, IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings, string Detail);
