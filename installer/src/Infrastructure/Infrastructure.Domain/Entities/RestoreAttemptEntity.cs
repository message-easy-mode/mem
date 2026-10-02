namespace Infrastructure.Data.Entities;

/// <summary>
/// Durable, SQLite-backed coordination record for one restore attempt.
/// A restore attempt is always created from one managed Backup Catalog entry.
/// Filesystem evidence and artifacts remain outside this table; this row owns
/// active-source uniqueness, current workflow state, and concise UI summaries.
/// </summary>
public sealed class RestoreAttemptEntity
{
    public Guid Id { get; set; }

    public string RestoreSessionId { get; set; } = default!;

    /// <summary>
    /// Always <c>backup-catalog</c> for new restore attempts. Kept as a string
    /// to make persisted records self-describing and to avoid coupling the
    /// storage schema to an enum.
    /// </summary>
    public string SourceKind { get; set; } = default!;

    /// <summary>
    /// Canonical source uniqueness key. Equals the active source key while the
    /// attempt is active.
    /// </summary>
    public string SourceKey { get; set; } = default!;

    /// <summary>
    /// Equals <see cref="SourceKey"/> while this attempt is active and is cleared
    /// when the attempt becomes terminal. A unique index on this nullable column
    /// provides one-active-attempt-per-catalog-entry semantics in SQLite.
    /// </summary>
    public string? ActiveSourceKey { get; set; }

    /// <summary>
    /// Immutable catalog-source snapshots retained after a terminal attempt's
    /// catalog FK has been deliberately detached by permanent catalog deletion.
    /// They are source identity, not uploaded-ZIP validation identity.
    /// </summary>
    public string SourceCatalogEntryIdSnapshot { get; set; } = default!;
    public string SourceDisplayNameSnapshot { get; set; } = default!;
    public string SourceOriginKindSnapshot { get; set; } = default!;
    public string? SourceStackSlugSnapshot { get; set; }
    public string? SourceBackupIdSnapshot { get; set; }

    /// <summary>
    /// Canonical managed backup source. It is required for every newly created
    /// attempt. It becomes nullable only after permanent catalog deletion has
    /// intentionally detached completed/cancelled history.
    /// </summary>
    public Guid? BackupCatalogEntryId { get; set; }
    public BackupCatalogEntryEntity? BackupCatalogEntry { get; set; }

    public string Status { get; set; } = default!;
    public string CurrentStage { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? TerminalAtUtc { get; set; }

    public DateTime? LastEventAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorSummary { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }

    public string SessionDirectoryPath { get; set; } = default!;
    public string? LogDirectoryPath { get; set; }
    public string? SupportReportPath { get; set; }

    /// <summary>
    /// Optional pointer to the current/high-level operation. Runtime operations
    /// also carry their own nullable RestoreAttemptId association for history.
    /// </summary>
    public Guid? RuntimeOperationId { get; set; }

    public ICollection<RestoreTargetClaimEntity> TargetClaims { get; set; } =
        new List<RestoreTargetClaimEntity>();

    public ICollection<RuntimeOperationEntity> RuntimeOperations { get; set; } =
        new List<RuntimeOperationEntity>();
}
