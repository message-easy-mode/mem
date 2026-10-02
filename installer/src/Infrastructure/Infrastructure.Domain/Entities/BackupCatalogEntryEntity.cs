namespace Infrastructure.Data.Entities;

/// <summary>
/// Durable catalogue metadata for one usable MEM recovery payload.
///
/// The backup payload itself remains on the filesystem. This row owns the
/// stable public catalogue identity, provenance, integrity summary, lifecycle
/// state, and links to restore attempts.
/// </summary>
public sealed class BackupCatalogEntryEntity
{
    /// <summary>Internal database primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Stable public/operator identity, for example <c>bkp_...</c>.
    /// This is intentionally distinct from validation and restore-session IDs.
    /// </summary>
    public string CatalogEntryId { get; set; } = default!;

    /// <summary><c>local-captured</c> or <c>imported-zip</c>.</summary>
    public string OriginKind { get; set; } = default!;

    /// <summary>Operator-facing backup name captured at registration time.</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>
    /// <c>available</c>, <c>materialising</c>, <c>failed</c>, or <c>removed</c>.
    /// Only <c>available</c> entries may be used as new restore sources.
    /// </summary>
    public string PayloadState { get; set; } = default!;

    /// <summary>
    /// <c>local-backup-directory</c> or <c>catalog-managed-directory</c>.
    /// </summary>
    public string PayloadStorageKind { get; set; } = default!;

    /// <summary>
    /// Internal-only canonical payload directory. Normal API DTOs must never
    /// expose this host filesystem path.
    /// </summary>
    public string PayloadDirectoryPath { get; set; } = default!;

    /// <summary>
    /// Local-capture provenance when available. Imported ZIPs may also retain
    /// these values from their export manifest without becoming local captures.
    /// </summary>
    public string? SourceStackSlug { get; set; }
    public string? SourceBackupId { get; set; }

    /// <summary>
    /// Import-validation/audit receipt identity for imported ZIPs only.
    /// This must never be treated as the catalogue identity.
    /// </summary>
    public string? ValidationId { get; set; }

    public int? ManifestVersion { get; set; }
    public string? MemVersion { get; set; }
    public string? MatrixServerName { get; set; }
    public string? MatrixHost { get; set; }
    public string? ElementHost { get; set; }

    /// <summary><c>valid</c>, <c>warning</c>, <c>invalid</c>, or <c>unknown</c>.</summary>
    public string IntegrityStatus { get; set; } = default!;
    public string? IntegritySummary { get; set; }
    public int WarningCount { get; set; }
    public long? PayloadBytes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CapturedAtUtc { get; set; }
    public DateTime? ImportedAtUtc { get; set; }
    public DateTime? MaterialisedAtUtc { get; set; }

    /// <summary>
    /// Historical metadata from the former payload-only removal action. New
    /// operator deletion permanently removes the catalog row; these values are
    /// retained only for legacy rows awaiting explicit purge.
    /// </summary>
    public DateTime? PayloadRemovedAtUtc { get; set; }
    public string? PayloadRemovedBy { get; set; }

    public ICollection<RestoreAttemptEntity> RestoreAttempts { get; set; } =
        new List<RestoreAttemptEntity>();
}
