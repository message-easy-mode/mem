namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Stable values used by the durable Backup Catalog. These are persisted in
/// SQLite, so callers must not replace them casually with display labels.
/// </summary>
public static class BackupCatalogOriginKinds
{
    public const string LocalCaptured = "local-captured";
    public const string ImportedZip = "imported-zip";
}

public static class BackupCatalogPayloadStates
{
    public const string Available = "available";
    public const string Materialising = "materialising";
    public const string Failed = "failed";
    public const string Removed = "removed";
}

public static class BackupCatalogPayloadStorageKinds
{
    public const string LocalBackupDirectory = "local-backup-directory";
    public const string CatalogManagedDirectory = "catalog-managed-directory";
}

public static class BackupCatalogIntegrityStatuses
{
    public const string Valid = "valid";
    public const string Warning = "warning";
    public const string Invalid = "invalid";
    public const string Unknown = "unknown";
}

/// <summary>
/// Browser-safe non-blocking operator guidance retained for imported portable ZIPs.
/// These notes are intentionally distinct from payload integrity failures.
/// </summary>
public sealed record BackupCatalogAdvisory(
    string Category,
    string Title,
    string Message);

/// <summary>
/// Metadata required to register one existing local backup directory as a
/// durable Backup Catalog entry. The payload itself remains on disk.
/// </summary>
public sealed record BackupCatalogLocalRegistration(
    string StackSlug,
    string BackupId,
    string PayloadDirectoryPath,
    DateTime CapturedAtUtc,
    long PayloadBytes,
    int WarningCount,
    string IntegrityStatus,
    string IntegritySummary,
    string? MatrixHost,
    string? ElementHost)
{
    public int? ManifestVersion { get; init; }
    public string? MemVersion { get; init; }
    public string? MatrixServerName { get; init; }
}

/// <summary>
/// Result of an idempotent local-backup registration attempt.
/// </summary>
public sealed record BackupCatalogLocalRegistrationResult(
    string CatalogEntryId,
    string Action,
    string PayloadState,
    string IntegrityStatus);

/// <summary>
/// Pure policy output used when local backup inspection determines whether the
/// physical payload is usable, warning-bearing, or incomplete.
/// </summary>
public sealed record BackupCatalogIntegrityAssessment(
    string Status,
    string Summary);

/// <summary>
/// Safe operator response for an explicit local-backup catalog backfill.
/// Host filesystem paths are intentionally absent.
/// </summary>
public sealed record BackupCatalogLocalBackfillResponse(
    string Source,
    string Status,
    int Scanned,
    int Created,
    int Updated,
    int SkippedRemoved,
    int Failed,
    IReadOnlyList<BackupCatalogLocalBackfillItem> Entries,
    IReadOnlyList<BackupCatalogLocalBackfillFailure> Failures,
    string? Detail);

public sealed record BackupCatalogLocalBackfillItem(
    string StackSlug,
    string BackupId,
    string CatalogEntryId,
    string Action,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount);

public sealed record BackupCatalogLocalBackfillFailure(
    string StackSlug,
    string BackupId,
    string Error);

/// <summary>
/// Stable read-model response for catalog inventory.
///
/// This is deliberately a safe projection: it does not contain payload
/// directory paths, storage implementation details, secrets, raw manifests,
/// or archive locations.
/// </summary>
public sealed record BackupCatalogListResponse(
    int TotalCount,
    IReadOnlyList<BackupCatalogListItem> Entries);

/// <summary>
/// Compact catalog row used by operator, CLI, and future frontend inventory
/// views. Entries are returned newest-first by captured time, then creation
/// time, then public catalog identity.
/// </summary>
public sealed record BackupCatalogListItem(
    string CatalogEntryId,
    string OriginKind,
    string DisplayName,
    string? SourceStackSlug,
    string? SourceBackupId,
    DateTime? CapturedAtUtc,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    long? PayloadBytes,
    DateTime CreatedAtUtc,
    DateTime? ImportedAtUtc,
    DateTime? MaterialisedAtUtc,
    DateTime? PayloadRemovedAtUtc,
    int AdvisoryCount = 0);

/// <summary>
/// Safe detailed catalog projection. This is still metadata-only and must not
/// be treated as a payload access or restore execution contract.
/// </summary>
public sealed record BackupCatalogDetailResponse(
    string CatalogEntryId,
    string OriginKind,
    string DisplayName,
    string? SourceStackSlug,
    string? SourceBackupId,
    string? ValidationId,
    int? ManifestVersion,
    string? MemVersion,
    string? MatrixServerName,
    string? MatrixHost,
    string? ElementHost,
    DateTime? CapturedAtUtc,
    string PayloadState,
    string IntegrityStatus,
    string? IntegritySummary,
    int WarningCount,
    long? PayloadBytes,
    DateTime CreatedAtUtc,
    DateTime? ImportedAtUtc,
    DateTime? MaterialisedAtUtc,
    DateTime? PayloadRemovedAtUtc,
    string? PayloadRemovedBy,
    int AdvisoryCount = 0,
    IReadOnlyList<BackupCatalogAdvisory>? Advisories = null);

/// <summary>
/// Metadata used to create or resume an imported ZIP materialisation entry.
/// Validation receipt and catalog identity remain deliberately separate.
/// </summary>
public sealed record BackupCatalogImportedMaterialisationRegistration(
    string ValidationId,
    string DisplayName,
    string? SourceStackSlug,
    int? ManifestVersion,
    string? MemVersion,
    string? MatrixServerName,
    string? MatrixHost,
    string? ElementHost,
    DateTime ImportedAtUtc,
    string IntegrityStatus,
    string IntegritySummary,
    int WarningCount);

public sealed record BackupCatalogImportedMaterialisationStartResult(
    string CatalogEntryId,
    string Action,
    string PayloadState,
    string PayloadDirectoryPath);

public sealed record BackupCatalogImportedMaterialisationResponse(
    string Source,
    string Status,
    string ValidationId,
    string CatalogEntryId,
    string Action,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    long? PayloadBytes,
    DateTime? MaterialisedAtUtc,
    string? Detail,
    int AdvisoryCount = 0);


/// <summary>
/// Internal restore-facing representation of one durable Backup Catalog entry.
/// Filesystem details stay inside the Host Agent and are never returned through
/// normal catalog API projections.
/// </summary>
public sealed record BackupCatalogRestoreSource(
    Guid EntryId,
    string CatalogEntryId,
    string OriginKind,
    string DisplayName,
    string PayloadState,
    string PayloadDirectoryPath,
    string? SourceStackSlug,
    string? SourceBackupId,
    string? ValidationId,
    string IntegrityStatus,
    int WarningCount,
    string? ElementHost,
    DateTime? CapturedAtUtc,
    long? PayloadBytes);

/// <summary>
/// Safe lifecycle state for one catalog entry. The normal delete action is
/// permanent; an active restore remains the only supported blocker.
/// </summary>
public sealed record BackupCatalogLifecycleResponse(
    string CatalogEntryId,
    string PayloadState,
    bool PayloadPresent,
    bool HasActiveRestore,
    string? ActiveRestoreSessionId,
    bool CanDelete,
    string? DeleteBlockReason,
    BackupCatalogImportArchiveResponse? OriginalArchive);

public sealed record BackupCatalogPermanentDeleteRequest(string? Operator);

/// <summary>
/// Browser-safe summary of an irreversible catalog deletion. Host filesystem
/// paths and secrets are intentionally absent.
/// </summary>
public sealed record BackupCatalogPermanentDeleteResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string OriginKind,
    string? DeletedBy,
    bool PayloadDeleted,
    bool OriginalArchiveDeleted,
    int PortableExportsDeleted,
    int DetachedRestoreAttempts,
    string Detail);

public sealed record BackupCatalogImportArchiveResponse(string ValidationId,string ArchiveState,string? OriginalFileName,long? ArchiveBytes,bool CatalogEntryLinked,string? CatalogEntryId,string Detail);
public sealed record BackupCatalogImportArchiveDeleteRequest(string? Operator);
public sealed record BackupCatalogImportArchiveDeleteResponse(string Source,string Status,string ValidationId,string ArchiveState,string? CatalogEntryId,string Detail);
