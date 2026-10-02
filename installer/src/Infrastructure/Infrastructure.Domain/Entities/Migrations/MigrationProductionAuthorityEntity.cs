namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Immutable Migration-owned evidence that one exact package, candidate, and retained verified
/// staging run may authorize production adoption. The original package capture semantics remain
/// unchanged: a preview package authorized by an operator attestation is never relabelled as a
/// final-frozen package.
/// </summary>
public sealed class MigrationProductionAuthorityEntity
{
    public Guid Id { get; set; }
    public string ProductionAuthorityId { get; set; } = default!;

    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;

    public Guid MigrationPackageRevisionEntityId { get; set; }
    public MigrationPackageRevisionEntity PackageRevision { get; set; } = default!;

    public Guid MigrationCandidateArtifactEntityId { get; set; }
    public MigrationCandidateArtifactEntity CandidateArtifact { get; set; } = default!;

    public Guid MigrationStagingRunEntityId { get; set; }
    public MigrationStagingRunEntity StagingRun { get; set; } = default!;

    public string AuthorityType { get; set; } = default!;
    public string Status { get; set; } = default!;

    /// <summary>
    /// Equals the durable migration intake ID while this authority is active. It is cleared when
    /// the authority is superseded or revoked. A unique nullable index preserves authority history
    /// while enforcing at most one active production authority per Migration Session.
    /// </summary>
    public string? ActiveMigrationKey { get; set; }

    public string EncryptedPackageSha256 { get; set; } = default!;
    public string DecryptedArchiveSha256 { get; set; } = default!;
    public string SourceMigrationId { get; set; } = default!;
    public string SourceFingerprint { get; set; } = default!;
    public string MatrixServerName { get; set; } = default!;
    public string SigningKeyIdentitySha256 { get; set; } = default!;
    public string CaptureKind { get; set; } = default!;
    public bool SourceFrozen { get; set; }
    public bool RehearsalOnly { get; set; }
    public DateTime? CapturedAtUtc { get; set; }

    public string EvidenceSchemaVersion { get; set; } = default!;
    public string EvidenceJson { get; set; } = default!;
    public string EvidenceSha256 { get; set; } = default!;

    public string AcknowledgementsSchemaVersion { get; set; } = default!;
    public string AcknowledgementsJson { get; set; } = default!;
    public string AcknowledgementsSha256 { get; set; } = default!;

    public Guid CreatedByOperatorId { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SupersededAtUtc { get; set; }
    public Guid? SupersededByOperatorId { get; set; }
    public string? SupersessionReason { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByOperatorId { get; set; }
    public string? RevocationReason { get; set; }
}

public static class MigrationProductionAuthorityTypes
{
    public const string FinalFrozen = "final-frozen";
    public const string OperatorAttestedSnapshot = "operator-attested-snapshot";

    public static bool IsKnown(string? value) =>
        string.Equals(value, FinalFrozen, StringComparison.Ordinal) ||
        string.Equals(value, OperatorAttestedSnapshot, StringComparison.Ordinal);
}

public static class MigrationProductionAuthorityStatuses
{
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Revoked = "revoked";

    public static bool IsKnown(string? value) =>
        string.Equals(value, Active, StringComparison.Ordinal) ||
        string.Equals(value, Superseded, StringComparison.Ordinal) ||
        string.Equals(value, Revoked, StringComparison.Ordinal);
}
