using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Runtime.Migrations.BaselineBackup;

namespace HostAgent.Runtime.Migrations.Acceptance;

public sealed record FinishMigrationRequest(
    int RetentionDays,
    bool ConfirmVerifiedServerIsAuthoritative,
    bool ConfirmRecoveryBoundaryChanges,
    bool ConfirmRetainOldServerAndNoAutomaticDeletion);

public sealed record AcceptMigrationRequest(
    string? Note,
    int RetentionDays,
    bool AcknowledgeFreshPublicVerification,
    bool AcknowledgeTargetWriteDivergence,
    bool AcknowledgeRollbackBoundaryChanges,
    bool AcknowledgeLegacySourceResourcesRetained,
    bool AcknowledgeNoAutomaticLegacyDeletion);

public sealed record MigrationPublicVerificationSummary(
    string Status,
    bool Attempted,
    bool Passed,
    int CheckCount,
    int FailedCheckCount,
    string? EvidenceSha256,
    string Detail);

public sealed record MigrationAcceptanceRecord(
    string AcceptanceId,
    string ExecutionId,
    string CandidateArtifactId,
    string StagingRunId,
    DateTime PublicCutoverAtUtc,
    DateTime AcceptedAtUtc,
    string AcceptedBy,
    string? Note,
    MigrationPublicVerificationSummary PublicVerification,
    bool FreshPublicVerificationAcknowledged,
    bool TargetWriteDivergenceAcknowledged,
    bool RollbackBoundaryAcknowledged,
    bool LegacyRetentionAcknowledged,
    bool NoAutomaticLegacyDeletionAcknowledged);

public sealed record MigrationLegacyRetentionSummary(
    string RetentionRecordId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime RetainUntilUtc,
    DateTime CleanupEligibleAtUtc,
    string SourceMigrationId,
    string SourceProduct,
    string? SourceVersion,
    bool SourcePackageRetained,
    bool CandidateArtifactRetained,
    bool PrivateStagingEvidenceRetained,
    bool LegacySourceResourcesRetained,
    bool AutomaticDeletionAllowed,
    string Summary);

public sealed record MigrationAcceptanceStateResponse(
    string Source,
    string Status,
    string MigrationId,
    bool AcceptanceEligible,
    bool Accepted,
    bool PublicVerificationRequired,
    MigrationAssuranceSummary? Assurance,
    MigrationAcceptanceRecord? Acceptance,
    MigrationLegacyRetentionSummary? LegacyRetention,
    MigrationBaselineBackupSummary? BaselineBackup,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    string Detail);

public sealed record MigrationAcceptanceCompletionEvidencePayload(
    string ReportId,
    DateTime GeneratedAtUtc,
    string MemVersion,
    string MigrationId,
    MigrationAssuranceSummary Assurance,
    string AdoptionPlanId,
    Guid RuntimeStackId,
    string TargetStackSlug,
    string ProductionVerificationId,
    string ProductionVerificationEvidenceSha256,
    MigrationAcceptanceRecord Acceptance,
    MigrationLegacyRetentionSummary LegacyRetention,
    MigrationBaselineBackupSummary BaselineBackup);

public sealed record MigrationAcceptanceCompletionEvidenceEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    MigrationAcceptanceCompletionEvidencePayload Payload);
