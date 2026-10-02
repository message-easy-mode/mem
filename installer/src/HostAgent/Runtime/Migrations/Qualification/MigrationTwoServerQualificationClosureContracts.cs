using HostAgent.Runtime.Migrations.Assurance;

namespace HostAgent.Runtime.Migrations.Qualification;

public sealed record MigrationTwoServerQualificationClosureRequest(
    bool QualificationEvidenceReviewed,
    bool DistinctHostEvidenceReviewed,
    bool NormalLifecycleReviewed,
    bool SourceRetentionReviewed,
    bool ReleaseHandoffAcknowledged,
    string? Note);

public sealed record MigrationTwoServerQualificationClosureRoute(
    string ServiceKey,
    string PublicHost,
    string? PublicBaseUrl,
    string Provider,
    string ProviderRouteId,
    string ForwardScheme,
    string ForwardHost,
    int ForwardPort,
    bool IsPublic,
    bool SslExpected,
    bool SslConfigured,
    bool ForceSsl,
    string Status,
    DateTime? LastVerifiedAtUtc);

public sealed record MigrationTwoServerQualificationClosureEvidencePayload(
    string ClosureId,
    DateTime ClosedAtUtc,
    string MemVersion,
    string MigrationId,
    MigrationAssuranceSummary Assurance,
    string SourceMigrationId,
    string PackageRevisionId,
    string EncryptedPackageSha256,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    string QualificationId,
    string QualificationEvidenceSha256,
    string SourceEvidenceAttemptId,
    string SourceEvidenceSha256,
    string AdoptionPlanId,
    Guid RuntimeStackId,
    string RuntimeStackSlug,
    string ProductionVerificationId,
    string ProductionVerificationEvidenceSha256,
    DateTime ProductionVerificationCompletedAtUtc,
    string AcceptanceId,
    string AcceptanceEvidenceSha256,
    DateTime AcceptedAtUtc,
    string BaselineBackupHandoffId,
    string BaselineBackupId,
    string BaselineCatalogEntryId,
    string BaselineCatalogPayloadState,
    string BaselineCatalogIntegrityStatus,
    DateTime BaselineCompletedAtUtc,
    long? BaselineBackupBytes,
    long? BaselineBackupFiles,
    int? BaselineBackupWarnings,
    MigrationTwoServerHostIdentity SourceHost,
    MigrationTwoServerHostIdentity TargetHost,
    bool DistinctMachineIdentity,
    bool DistinctDockerEngineIdentity,
    IReadOnlyList<MigrationTwoServerSourceContainerEvidence> SourceContainers,
    IReadOnlyList<MigrationTwoServerQualificationClosureRoute> PublicRoutes,
    bool QualificationEvidenceReviewed,
    bool DistinctHostEvidenceReviewed,
    bool NormalLifecycleReviewed,
    bool SourceRetentionReviewed,
    bool ReleaseHandoffAcknowledged,
    string? Note);

public sealed record MigrationTwoServerQualificationClosureEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    MigrationTwoServerQualificationClosureEvidencePayload Payload);

public sealed record MigrationTwoServerQualificationClosureStateResponse(
    string Source,
    string Status,
    string MigrationId,
    bool ClosureEligible,
    bool Closed,
    MigrationAssuranceSummary? Assurance,
    MigrationTwoServerQualificationClosureEnvelope? Closure,
    IReadOnlyList<string> Blockers,
    string Detail);
