namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Immutable package-history boundary for one Migration Session. A revision owns the target-bound
/// encrypted-transfer identity and the validated source-package evidence for one preview or final
/// capture. Protected identities and server filesystem locations are never browser contracts.
/// </summary>
public sealed class MigrationPackageRevisionEntity
{
    public Guid Id { get; set; }

    public string PackageRevisionId { get; set; } = default!;

    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;

    public int RevisionNumber { get; set; }
    public string Purpose { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string RetentionState { get; set; } = default!;

    /// <summary>
    /// Server-generated unique key retained only while this is the current revision for its
    /// purpose. Nullable uniqueness permits one active preview and one active final revision for
    /// the same Migration Session while preserving superseded history.
    /// </summary>
    public string? ActivePurposeKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public DateTime? UploadedAtUtc { get; set; }
    public DateTime? ValidatedAtUtc { get; set; }
    public DateTime? SupersededAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }

    public string? AgeRecipient { get; set; }
    public string? ProtectedAgeIdentity { get; set; }
    public string? RecipientFingerprint { get; set; }

    public string? PackageFileName { get; set; }
    public long? PackageSizeBytes { get; set; }
    public string? EncryptedPackageSha256 { get; set; }
    public string? DecryptedArchiveSha256 { get; set; }

    public string? ArchiveMigrationId { get; set; }
    public string? ArchiveSourceProduct { get; set; }
    public string? ArchiveSourceVersion { get; set; }
    public int? ArchiveStackCount { get; set; }
    public string? CaptureKind { get; set; }
    public bool? SourceFrozen { get; set; }
    public bool? RehearsalOnly { get; set; }
    public int? VerifiedFileCount { get; set; }
    public long? VerifiedExpandedBytes { get; set; }

    public string? ValidationCode { get; set; }
    public string? ValidationSummary { get; set; }

    public ICollection<MigrationConversionAttemptEntity> ConversionAttempts { get; set; } =
        new List<MigrationConversionAttemptEntity>();
}
