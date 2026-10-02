using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preview;

public sealed record RuntimeStackBackupPublicCutoverPreviewResult(
    string Source,
    string Status,
    string PreviewId,
    string ValidationId,
    string CandidateId,
    string? PlanId,
    string RestoreMode,
    DateTimeOffset CreatedAtUtc,
    bool ProductionExecutionLocked,
    RuntimeStackBackupProductionRestoreMutationSummary Mutations,
    RuntimeStackBackupPublicCutoverPreviewCandidateSummary Candidate,
    RuntimeStackBackupPublicCutoverPreviewRouteSummary Routes,
    IReadOnlyList<RuntimeStackBackupPublicCutoverPreviewCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null,
    string? RestoreSessionId = null);

public sealed record RuntimeStackBackupPublicCutoverPreviewCandidateSummary(
    bool CandidateFound,
    string CandidateId,
    string ValidationId,
    string Status,
    string RestoreMode,
    string TargetStackSlug,
    string? MatrixServerName,
    string PrivateRuntimeId,
    string PrivateRuntimeStatus,
    bool PrivateOnly,
    bool Destroyed,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    string? SynapseHealthResponse,
    string NetworkName,
    string PostgresContainerName,
    string SynapseContainerName,
    string? ElementDataPath,
    bool ElementConfigExtracted,
    bool ElementConfigPatched,
    string? ElementContainerName,
    string? ElementContainerId,
    bool ElementContainerStarted,
    bool ElementHealthPassed,
    string? ElementHealthResponse,
    bool ReadyForMatrixCutoverPreview,
    bool ReadyForElementCutoverPreview,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupPublicCutoverPreviewRouteSummary(
    RuntimeStackBackupPublicCutoverPreviewRouteAction Matrix,
    RuntimeStackBackupPublicCutoverPreviewRouteAction Element,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupPublicCutoverPreviewRouteAction(
    string Component,
    string? Host,
    string? UpstreamContainerName,
    int? UpstreamPort,
    string ForwardScheme,
    Guid? CertificateEntityId,
    int? NpmCertificateId,
    bool CertificateAvailable,
    bool CertificateImportedToNpm,
    bool CertificateIsStaging,
    bool RouteCurrentlyExists,
    RuntimeStackBackupProductionRestoreNpmRouteSummary? ExistingRoute,
    bool ExistingRouteAlreadyTargetsCandidate,
    string Action,
    bool AvailableForFutureExecution,
    bool WillMutateNow,
    IReadOnlyList<string> RequiredBeforeExecution,
    IReadOnlyList<string> Notes,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverPreviewCheck(
    string Code,
    string Category,
    string Severity,
    string Status,
    bool Passed,
    string Message,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverPreviewHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalPreviews,
    IReadOnlyList<RuntimeStackBackupPublicCutoverPreviewSummary> Previews,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverPreviewDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    RuntimeStackBackupPublicCutoverPreviewResult? Preview,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverPreviewSummary(
    string PreviewId,
    string ValidationId,
    string CandidateId,
    string? PlanId,
    string RestoreMode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    bool ProductionExecutionLocked,
    string TargetStackSlug,
    bool CandidateReady,
    bool ElementCandidateReady,
    string? MatrixHost,
    string MatrixAction,
    bool MatrixRouteCurrentlyExists,
    bool MatrixAvailableForFutureExecution,
    string? ElementHost,
    string ElementAction,
    bool ElementRouteCurrentlyExists,
    bool ElementAvailableForFutureExecution,
    int BlockerCount,
    int WarningCount,
    int ErrorCount,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null,
    string? RestoreSessionId = null);
