using HostAgent.Runtime.Migrations.Assurance;

namespace HostAgent.Runtime.Migrations.Qualification;

public sealed record MigrationTwoServerHostIdentity(
    string MachineIdSha256,
    string MachineName,
    string OperatingSystem,
    string Architecture,
    string DockerEngineIdSha256,
    string DockerName,
    string DockerServerVersion);

public sealed record MigrationTwoServerSourceContainerEvidence(
    string Role,
    string ContainerId,
    string ContainerName,
    string ImageId,
    bool WriterContainer,
    bool WasRunningBeforeFreeze,
    string CurrentState,
    bool CurrentlyRunning,
    string CurrentRestartPolicy,
    bool IdentityMatched,
    bool FrozenStatePreserved);

public sealed record MigrationTwoServerSourceEvidencePayload(
    string QualificationAttemptId,
    DateTime GeneratedAtUtc,
    string MigrationId,
    string IntakeId,
    string PackageRevisionId,
    string EncryptedPackageSha256,
    long EncryptedPackageBytes,
    string SourceArchiveSha256,
    string FreezeAttemptId,
    string FreezePlanId,
    string FreezePlanSha256,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    bool SourceFrozen,
    bool PublicRoutingMutationOccurred,
    bool DevelopmentExternalControlPlane,
    MigrationTwoServerHostIdentity SourceHost,
    IReadOnlyList<MigrationTwoServerSourceContainerEvidence> Containers);

public sealed record MigrationTwoServerSourceEvidenceEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    MigrationTwoServerSourceEvidencePayload Payload);

public sealed record MigrationTwoServerQualificationEvidencePayload(
    string QualificationId,
    DateTime QualifiedAtUtc,
    string MigrationId,
    string SourceMigrationId,
    string PackageRevisionId,
    string EncryptedPackageSha256,
    string SourceEvidenceAttemptId,
    string SourceEvidenceSha256,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    string FreezeAttemptId,
    string FreezePlanId,
    string FreezePlanSha256,
    string AdoptionPlanId,
    Guid RuntimeStackId,
    string ProductionVerificationId,
    string AcceptanceId,
    string BaselineBackupHandoffId,
    string BaselineCatalogEntryId,
    MigrationTwoServerHostIdentity SourceHost,
    MigrationTwoServerHostIdentity TargetHost,
    bool DistinctMachineIdentity,
    bool DistinctDockerEngineIdentity,
    bool DevelopmentExternalControlPlane);

public sealed record MigrationTwoServerQualificationDto(
    string QualificationId,
    string Status,
    DateTime ImportedAtUtc,
    DateTime QualifiedAtUtc,
    string SourceEvidenceAttemptId,
    string SourceEvidenceSha256,
    string QualificationEvidenceSha256,
    string SourceMigrationId,
    string PackageRevisionId,
    string SourceStackSlug,
    string MatrixServerName,
    string AdoptionPlanId,
    Guid RuntimeStackId,
    string ProductionVerificationId,
    string AcceptanceId,
    string BaselineBackupHandoffId,
    string BaselineCatalogEntryId,
    MigrationTwoServerHostIdentity SourceHost,
    MigrationTwoServerHostIdentity TargetHost,
    bool DistinctMachineIdentity,
    bool DistinctDockerEngineIdentity,
    IReadOnlyList<MigrationTwoServerSourceContainerEvidence> SourceContainers);

public sealed record MigrationTwoServerQualificationStateResponse(
    string Source,
    string Status,
    string MigrationId,
    bool QualificationEligible,
    bool Qualified,
    MigrationAssuranceSummary? Assurance,
    MigrationTwoServerQualificationDto? Qualification,
    IReadOnlyList<string> Blockers,
    string Detail);

public interface IMigrationTargetHostIdentityProbe
{
    Task<MigrationTwoServerHostIdentity> ObserveAsync(CancellationToken cancellationToken);
}

internal sealed record MigrationTwoServerQualificationExpectedBindings(
    string MigrationId,
    string SourceMigrationId,
    string PackageRevisionId,
    string EncryptedPackageSha256,
    long EncryptedPackageBytes,
    string SourceArchiveSha256,
    string SourceFingerprint,
    string MatrixServerName);
