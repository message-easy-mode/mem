namespace HostAgent.Runtime.Migrations.Assurance;

public sealed record CreateOperatorAttestedSnapshotAuthorityRequest(
    bool AcknowledgeUsersWereInstructedNotToUseSource,
    bool AcknowledgePostCaptureWritesWillNotMigrate,
    bool AcknowledgeSelectedSnapshotBecomesAuthoritative,
    bool AcknowledgeSourceWillBeRetainedUntilVerification,
    bool AcknowledgeNoFormalSourceFreezeEvidence,
    bool AcknowledgeReducedRollbackAssurance);

public sealed record MigrationProductionAuthorityStateResponse(
    string Status,
    string MigrationId,
    bool AuthorizesProduction,
    MigrationProductionAuthorityDto? Authority,
    string Detail);

public sealed record MigrationProductionAuthorityDto(
    string ProductionAuthorityId,
    string AuthorityType,
    string Status,
    string PackageRevisionId,
    string CandidateArtifactId,
    string StagingRunId,
    string EncryptedPackageSha256,
    string DecryptedArchiveSha256,
    string SourceMigrationId,
    string SourceFingerprint,
    string MatrixServerName,
    string SigningKeyIdentitySha256,
    string CaptureKind,
    bool SourceFrozen,
    bool RehearsalOnly,
    DateTime? CapturedAtUtc,
    long? UsersCount,
    long? RoomsCount,
    long? EventsCount,
    string EvidenceSchemaVersion,
    string EvidenceSha256,
    string AcknowledgementsSchemaVersion,
    string AcknowledgementsSha256,
    DateTime CreatedAtUtc);

public enum MigrationProductionAuthorityFailureKind
{
    InvalidRequest,
    Conflict,
}

public sealed class MigrationProductionAuthorityException : Exception
{
    public MigrationProductionAuthorityException(
        string code,
        string message,
        MigrationProductionAuthorityFailureKind kind,
        Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        Kind = kind;
    }

    public string Code { get; }
    public MigrationProductionAuthorityFailureKind Kind { get; }
}
