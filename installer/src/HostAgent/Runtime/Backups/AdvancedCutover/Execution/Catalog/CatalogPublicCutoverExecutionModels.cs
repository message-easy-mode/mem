using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;

/// <summary>
/// Explicit, high-friction command for a catalog-backed public cutover.
/// The old runtime is stopped but never deleted in this operation. A final
/// local backup is captured before any public-route mutation.
/// </summary>
public sealed record CatalogPublicCutoverExecutionRequest(
    string ConfirmationId,
    string CandidateId,
    string OldRuntimeStackSlug,
    string? Operator,
    string? Note,
    bool Execute,
    bool AcknowledgeFinalApproval,
    bool AcknowledgeFinalBackupWillBeCaptured,
    bool AcknowledgeOldRuntimeWillBeStopped,
    bool AcknowledgePublicRouteMutation,
    bool AcknowledgeManualRollback,
    bool GateEvaluationOnly = false);

public sealed record CatalogPublicCutoverExecutionResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string ConfirmationId,
    string CandidateId,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    CatalogPublicCutoverExecutionResult Execution,
    string Detail);

public sealed record CatalogPublicCutoverExecutionResult(
    string Source,
    string Status,
    string ExecutionId,
    string CatalogEntryId,
    string SourceKind,
    string RestoreSessionId,
    string ConfirmationId,
    string PreviewId,
    string CandidateId,
    string OldRuntimeStackSlug,
    string? FreshPlanId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? Operator,
    string? Note,
    bool ExecutionRequested,
    bool ProductionExecutionLocked,
    bool RuntimePromotionLocked,
    bool DnsMutationLocked,
    bool CertificateMutationLocked,
    RuntimeStackBackupProductionRestoreMutationSummary Mutations,
    CatalogPublicCutoverExecutionBackupSummary FinalBackup,
    CatalogPublicCutoverExecutionCandidateSummary Candidate,
    CatalogPublicCutoverExecutionOldRuntimeSummary OldRuntime,
    CatalogPublicCutoverExecutionIngressSummary CutoverIngress,
    CatalogPublicCutoverExecutionRoutesSummary Routes,
    CatalogPublicCutoverExecutionPublicVerificationSummary PublicVerification,
    CatalogPublicCutoverExecutionRollbackSummary Rollback,
    IReadOnlyList<CatalogPublicCutoverExecutionAcknowledgement> Acknowledgements,
    IReadOnlyList<CatalogPublicCutoverExecutionCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionBackupSummary(
    bool Required,
    bool CaptureAttempted,
    bool Captured,
    string? BackupId,
    string? BackupCatalogEntryId,
    string? BackupRootPath,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionCandidateSummary(
    bool Found,
    bool CatalogEntryMatches,
    bool RestoreSessionMatches,
    bool SynapseRunning,
    bool ElementRunning,
    bool SynapseHealthPassed,
    bool ElementHealthPassed,
    bool PrivateOnly,
    string? SynapseContainerName,
    string? ElementContainerName,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionOldRuntimeSummary(
    bool Found,
    string? StackSlug,
    string? MatrixServerName,
    string? MatrixPublicHost,
    string? MatrixContainerName,
    string? ElementContainerName,
    bool MatrixRouteOwnedBeforeCutover,
    bool ElementRouteOwnedBeforeCutover,
    bool MatrixContainerStopped,
    bool ElementContainerStopped,
    bool RetainedForRollback,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionIngressSummary(
    string? NetworkName,
    bool NetworkCreated,
    bool NpmAttached,
    bool MatrixAttached,
    bool ElementAttached,
    bool PostgresAttached,
    bool Ready,
    string? MatrixAlias,
    string? ElementAlias,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionRoutesSummary(
    CatalogPublicCutoverExecutionRouteResult Matrix,
    CatalogPublicCutoverExecutionRouteResult Element,
    bool AllRequiredRoutesSucceeded,
    bool AnyRouteMutated,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionRouteResult(
    string Component,
    string? Host,
    string? PreviousForwardHost,
    int? PreviousForwardPort,
    int? PreviousNpmCertificateId,
    string? DesiredForwardHost,
    int? DesiredForwardPort,
    int? DesiredNpmCertificateId,
    bool Required,
    bool Mutated,
    bool Succeeded,
    string Status,
    string? RouteId,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionPublicVerificationSummary(
    bool Attempted,
    bool Passed,
    IReadOnlyList<CatalogPublicCutoverExecutionPublicCheck> Checks,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionPublicCheck(
    string Code,
    bool Passed,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionRollbackSummary(
    bool Required,
    bool Attempted,
    bool Completed,
    bool MatrixRouteRestored,
    bool ElementRouteRestored,
    bool OldMatrixStarted,
    bool OldElementStarted,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionAcknowledgement(
    string Code,
    string Label,
    bool Required,
    bool Acknowledged,
    string Status);

public sealed record CatalogPublicCutoverExecutionCheck(
    string Code,
    string Category,
    string Severity,
    bool Passed,
    string Status,
    string Message,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalExecutions,
    IReadOnlyList<CatalogPublicCutoverExecutionSummary> Executions,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    CatalogPublicCutoverExecutionResult? Execution,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record CatalogPublicCutoverExecutionSummary(
    string ExecutionId,
    string CatalogEntryId,
    string SourceKind,
    string RestoreSessionId,
    string ConfirmationId,
    string PreviewId,
    string CandidateId,
    string OldRuntimeStackSlug,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? Operator,
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
