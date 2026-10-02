using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;

/// <summary>
/// Explicit acknowledgement input for a catalog-native, candidate-backed
/// Public Cutover confirmation evaluation. The route supplies catalogEntryId;
/// previewId and candidateId must match the durable catalog-linked records.
/// </summary>
public sealed record CatalogPublicCutoverConfirmationRequest(
    string PreviewId,
    string CandidateId,
    string? Operator,
    string? Note,
    bool AcknowledgePreviewReviewed,
    bool AcknowledgeCandidateIsPrivateAndHealthy,
    bool AcknowledgePublicRouteExposureRisk,
    bool AcknowledgeNoAutomaticRollback,
    bool AcknowledgeFinalBackupRequired,
    bool AcknowledgeExecutionStillLocked);

/// <summary>
/// Catalog-native response wrapper. The contained confirmation deliberately
/// identifies the recovery source by catalogEntryId, not a legacy validation
/// receipt.
/// </summary>
public sealed record CatalogPublicCutoverConfirmationResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string PreviewId,
    string CandidateId,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    CatalogPublicCutoverConfirmationResult Confirmation,
    string Detail);

/// <summary>
/// Durable, read-only gate evaluation for a catalog-backed private candidate.
/// ExecutionAvailable means the saved gates can be consumed by the separate
/// recently stepped-up executor; this confirmation endpoint itself never mutates.
/// </summary>
public sealed record CatalogPublicCutoverConfirmationResult(
    string Source,
    string Status,
    string ConfirmationId,
    string CatalogEntryId,
    string SourceKind,
    string RestoreSessionId,
    string PreviewId,
    string CandidateId,
    string? FreshPlanId,
    string RestoreMode,
    DateTimeOffset CreatedAtUtc,
    string? Operator,
    string? Note,
    bool ProductionExecutionLocked,
    bool ExecutionAvailable,
    RuntimeStackBackupProductionRestoreMutationSummary Mutations,
    CatalogPublicCutoverConfirmationPreviewSummary Preview,
    CatalogPublicCutoverConfirmationCandidateSummary Candidate,
    CatalogPublicCutoverConfirmationCertificateSummary Certificates,
    CatalogPublicCutoverConfirmationRouteSummary Routes,
    IReadOnlyList<CatalogPublicCutoverConfirmationAcknowledgement> Acknowledgements,
    IReadOnlyList<CatalogPublicCutoverConfirmationCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationPreviewSummary(
    bool PreviewFound,
    string PreviewId,
    string Status,
    string? SourceKind,
    string? CatalogEntryId,
    string CandidateId,
    string? RestoreSessionId,
    DateTimeOffset CreatedAtUtc,
    bool ProductionExecutionLocked,
    bool MutationFlagsClear,
    bool HasNoBlockers,
    bool HasNoErrors,
    bool ReadyForReview,
    bool CatalogEntryMatches,
    bool CandidateMatches,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationCandidateSummary(
    bool CandidateFound,
    string CandidateId,
    string Status,
    string? SourceKind,
    string? CatalogEntryId,
    string? RestoreSessionId,
    string PrivateRuntimeStatus,
    bool PrivateOnly,
    bool Destroyed,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool ElementHealthPassed,
    string? MatrixServerName,
    string? SynapseContainerName,
    string? ElementContainerName,
    bool CatalogEntryMatches,
    bool RestoreSessionMatches,
    string? Detail);

/// <summary>
/// First-class certificate readiness output. This prevents consumers from
/// inferring certificate state from route actions alone.
/// </summary>
public sealed record CatalogPublicCutoverConfirmationCertificateSummary(
    CatalogPublicCutoverConfirmationCertificateGate Matrix,
    CatalogPublicCutoverConfirmationCertificateGate Element,
    bool AllRequiredCertificatesReady,
    IReadOnlyList<string> Notes);

public sealed record CatalogPublicCutoverConfirmationCertificateGate(
    string Component,
    string? Host,
    bool Required,
    bool CertificateFound,
    Guid? CertificateEntityId,
    string? CommonName,
    int? NpmCertificateId,
    bool ImportedToNpm,
    bool IsStaging,
    bool Passed,
    string Status,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationRouteSummary(
    CatalogPublicCutoverConfirmationRouteGate Matrix,
    CatalogPublicCutoverConfirmationRouteGate Element,
    bool AllRequiredRoutesEligible,
    bool LiveNpmStateMatchesPreview,
    IReadOnlyList<string> Notes);

public sealed record CatalogPublicCutoverConfirmationRouteGate(
    string Component,
    string? Host,
    bool Required,
    string PreviewAction,
    bool PreviewAvailableForFutureExecution,
    bool PreviewRouteCurrentlyExists,
    bool FreshRouteCurrentlyExists,
    bool LiveStateMatchesPreview,
    bool CertificateReady,
    bool ExistingRouteAlreadyTargetsCandidate,
    string? UpstreamContainerName,
    int? UpstreamPort,
    bool Passed,
    string Status,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationAcknowledgement(
    string Code,
    string Label,
    string Description,
    string Severity,
    bool Required,
    bool Acknowledged,
    string Status);

public sealed record CatalogPublicCutoverConfirmationCheck(
    string Code,
    string Category,
    string Severity,
    string Status,
    bool Passed,
    string Message,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalConfirmations,
    IReadOnlyList<CatalogPublicCutoverConfirmationSummary> Confirmations,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    CatalogPublicCutoverConfirmationResult? Confirmation,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record CatalogPublicCutoverConfirmationSummary(
    string ConfirmationId,
    string CatalogEntryId,
    string SourceKind,
    string RestoreSessionId,
    string PreviewId,
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
    bool CertificatesPassed,
    bool AcknowledgementsPassed,
    int BlockerCount,
    int WarningCount,
    int ErrorCount,
    string? Detail);
