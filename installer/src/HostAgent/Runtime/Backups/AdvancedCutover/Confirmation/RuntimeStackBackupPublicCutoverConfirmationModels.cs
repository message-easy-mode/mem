using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;

public sealed record RuntimeStackBackupPublicCutoverConfirmationRequest(
    string? Operator,
    string? Note,
    bool AcknowledgePreviewReviewed,
    bool AcknowledgeCandidateIsPrivateAndHealthy,
    bool AcknowledgePublicRouteExposureRisk,
    bool AcknowledgeNoAutomaticRollback,
    bool AcknowledgeFinalBackupRequired,
    bool AcknowledgeExecutionStillLocked);

public sealed record RuntimeStackBackupPublicCutoverConfirmationResult(
    string Source,
    string Status,
    string ConfirmationId,
    string PreviewId,
    string ValidationId,
    string CandidateId,
    string? PreviewPlanId,
    string? FreshPlanId,
    string RestoreMode,
    DateTimeOffset CreatedAtUtc,
    string? Operator,
    string? Note,
    bool ProductionExecutionLocked,
    bool ExecutionAvailable,
    RuntimeStackBackupProductionRestoreMutationSummary Mutations,
    RuntimeStackBackupPublicCutoverConfirmationPreviewSummary Preview,
    RuntimeStackBackupPublicCutoverConfirmationCandidateSummary Candidate,
    RuntimeStackBackupPublicCutoverConfirmationRouteSummary Routes,
    IReadOnlyList<RuntimeStackBackupPublicCutoverConfirmationAcknowledgement> Acknowledgements,
    IReadOnlyList<RuntimeStackBackupPublicCutoverConfirmationCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationPreviewSummary(
    string PreviewId,
    string Status,
    DateTimeOffset CreatedAtUtc,
    bool ProductionExecutionLocked,
    bool MutationFlagsClear,
    bool HasNoBlockers,
    bool HasNoErrors,
    bool ReadyForReview,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationCandidateSummary(
    bool CandidateFound,
    string CandidateId,
    string Status,
    string PrivateRuntimeStatus,
    bool PrivateOnly,
    bool Destroyed,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool ElementHealthPassed,
    string? MatrixServerName,
    string? SynapseContainerName,
    string? ElementContainerName,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationRouteSummary(
    RuntimeStackBackupPublicCutoverConfirmationRouteGate Matrix,
    RuntimeStackBackupPublicCutoverConfirmationRouteGate Element,
    bool AllRequiredRoutesEligible,
    bool LiveNpmStateMatchesPreview,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupPublicCutoverConfirmationRouteGate(
    string Component,
    string? Host,
    string PreviewAction,
    bool PreviewAvailableForFutureExecution,
    bool PreviewRouteCurrentlyExists,
    bool FreshRouteCurrentlyExists,
    bool LiveStateMatchesPreview,
    bool CertificateReady,
    bool ExistingRouteAlreadyTargetsCandidate,
    string? UpstreamContainerName,
    int? UpstreamPort,
    string Status,
    bool Passed,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationAcknowledgement(
    string Code,
    string Label,
    string Description,
    string Severity,
    bool Required,
    bool Acknowledged,
    string Status);

public sealed record RuntimeStackBackupPublicCutoverConfirmationCheck(
    string Code,
    string Category,
    string Severity,
    string Status,
    bool Passed,
    string Message,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalConfirmations,
    IReadOnlyList<RuntimeStackBackupPublicCutoverConfirmationSummary> Confirmations,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    RuntimeStackBackupPublicCutoverConfirmationResult? Confirmation,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupPublicCutoverConfirmationSummary(
    string ConfirmationId,
    string PreviewId,
    string ValidationId,
    string CandidateId,
    string? FreshPlanId,
    string RestoreMode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    string? Operator,
    bool ProductionExecutionLocked,
    bool ExecutionAvailable,
    bool CandidateReady,
    bool MatrixPassed,
    bool ElementPassed,
    bool AcknowledgementsPassed,
    int BlockerCount,
    int WarningCount,
    int ErrorCount,
    string? Detail);
