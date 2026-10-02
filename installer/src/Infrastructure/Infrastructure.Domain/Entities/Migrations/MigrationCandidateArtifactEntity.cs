namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Immutable target-native artifact produced by a successful Migration conversion attempt.
/// This is a Migration business object, not a Backup Catalog entry. It enters the normal
/// Backup/Restore lifecycle only after migration acceptance creates a new native MEM backup.
/// </summary>
public sealed class MigrationCandidateArtifactEntity
{
    public Guid Id { get; set; }

    public string CandidateArtifactId { get; set; } = default!;

    public Guid MigrationConversionAttemptEntityId { get; set; }
    public MigrationConversionAttemptEntity ConversionAttempt { get; set; } = default!;

    public string ArtifactKind { get; set; } = default!;
    public string ArtifactSchemaVersion { get; set; } = default!;
    public string SourcePackageSha256 { get; set; } = default!;
    public string ArtifactSha256 { get; set; } = default!;
    public string ManifestSha256 { get; set; } = default!;
    public string ChecksumsSha256 { get; set; } = default!;
    public string ProvenanceJson { get; set; } = default!;

    public string VerificationStatus { get; set; } = default!;
    public string RetentionState { get; set; } = default!;
    public string StorageKind { get; set; } = default!;

    public string ArtifactPath { get; set; } = default!;
    public string? VerificationReportPath { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? VerifiedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
}
