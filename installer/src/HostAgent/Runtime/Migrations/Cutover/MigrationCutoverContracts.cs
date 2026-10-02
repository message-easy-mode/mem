using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;

namespace HostAgent.Runtime.Migrations.Cutover;

public sealed record PrepareMigrationCutoverCandidateRequest(
    string? TargetStackSlug);

public sealed record CreateMigrationCutoverPreviewRequest(
    string? TargetStackSlug,
    string? RestoreMode,
    string? IntendedMatrixHost,
    string? IntendedElementHost);

public sealed record ConfirmMigrationCutoverRequest(
    string? Operator,
    string? Note,
    bool AcknowledgePreviewReviewed,
    bool AcknowledgeCandidateIsPrivateAndHealthy,
    bool AcknowledgePublicRouteExposureRisk,
    bool AcknowledgeNoAutomaticRollback,
    bool AcknowledgeFinalBackupRequired,
    bool AcknowledgeExecutionStillLocked);

public sealed record ExecuteMigrationCutoverRequest(
    string? Operator,
    string? Note,
    bool ExecuteNpmRouteMutation,
    bool ExecuteCutoverIngressNetworkMutation,
    bool AcknowledgeConfirmationReviewed,
    bool AcknowledgeDockerNetworkMutation,
    bool AcknowledgeNpmMustNotJoinPrivateRestoreNetwork,
    bool AcknowledgeCreatesPublicRoutes,
    bool AcknowledgeNpmRoutesWillChange,
    bool AcknowledgeMatrixFederationExposureMayChange,
    bool AcknowledgeNoDnsMutation,
    bool AcknowledgeNoCertificateMutation,
    bool AcknowledgeNoRuntimePromotion,
    bool AcknowledgeNoAutomaticRollback,
    bool AcknowledgePostCutoverVerificationRequired);

public sealed record MigrationCutoverCaptureSummary(
    string Kind,
    bool SourceFrozen,
    bool RehearsalOnly,
    bool FinalCutoverEligible,
    string SourceMigrationId,
    string SourceStackSlug,
    string MatrixServerName,
    string? MatrixPublicUrl,
    string? ElementPublicUrl);

public sealed record MigrationCutoverStagingSummary(
    string StagingRunId,
    string PrivateRuntimeStagingId,
    string Status,
    bool PrivateOnly,
    bool PublicRoutesCreated,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool ElementConfigPresent,
    long? UsersCount,
    long? RoomsCount,
    long? EventsCount);

public sealed record MigrationCutoverRouteSnapshot(
    RuntimeStackBackupPublicCutoverPreviewRouteAction? Matrix,
    RuntimeStackBackupPublicCutoverPreviewRouteAction? Element,
    DateTimeOffset? CapturedAtUtc,
    string Status);

public sealed record MigrationCutoverRollbackEvidence(
    bool RouteMutationOccurred,
    bool AutomaticRollbackAvailable,
    bool AutomaticRollbackAttempted,
    bool AutomaticRollbackCompleted,
    string Status,
    string Detail);

public sealed record MigrationCutoverReadiness(
    string Status,
    bool ExecutionReady,
    bool FinalCaptureRequired,
    bool CandidateReady,
    bool PreviewReady,
    bool ConfirmationReady,
    bool RoutesReady,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    string Detail);

public sealed record MigrationCutoverStateResponse(
    string Source,
    string Status,
    string MigrationId,
    MigrationCutoverCaptureSummary Capture,
    string CandidateArtifactId,
    MigrationCutoverStagingSummary Staging,
    RuntimeStackBackupProductionCandidateResult? ProductionCandidate,
    RuntimeStackBackupPublicCutoverPreviewResult? Preview,
    RuntimeStackBackupPublicCutoverConfirmationResult? Confirmation,
    MigrationCutoverReadiness Readiness,
    MigrationCutoverRouteSnapshot RouteSnapshot,
    RuntimeStackBackupPublicCutoverExecutionResult? LatestExecution,
    MigrationCutoverRollbackEvidence Rollback,
    bool BackupCatalogItemCreated,
    bool RestoreSessionCreated,
    string Detail);

public sealed record MigrationCutoverCandidateResponse(
    string Source,
    string Status,
    string MigrationId,
    bool CandidateCreated,
    bool CandidateResumed,
    RuntimeStackBackupProductionCandidateResult Candidate,
    string Detail);

public sealed record MigrationCutoverPreviewResponse(
    string Source,
    string Status,
    string MigrationId,
    RuntimeStackBackupPublicCutoverPreviewResult Preview,
    string Detail);

public sealed record MigrationCutoverConfirmationResponse(
    string Source,
    string Status,
    string MigrationId,
    RuntimeStackBackupPublicCutoverConfirmationResult Confirmation,
    string Detail);

public sealed record MigrationCutoverExecutionResponse(
    string Source,
    string Status,
    string MigrationId,
    RuntimeStackBackupPublicCutoverExecutionResult Execution,
    string Detail);
