namespace HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;

public sealed record LocalBackupCatalogResponse(
    string Source,
    string Status,
    string BackupsRootPath,
    IReadOnlyList<LocalBackupCatalogStack> Stacks,
    string? Detail);



/// <summary>
/// Server-side query contract for the flat local-backup inventory used by the
/// paged "By date" operator table. Page numbering is one-based at the HTTP
/// boundary so browser URLs remain readable.
/// </summary>
public sealed record LocalBackupEntryQuery(
    int Page,
    int PageSize,
    string? Search,
    string? StackSlug,
    string? SortBy,
    string? SortDirection);

/// <summary>
/// Aggregate values for the current filtered local-backup entry result set.
/// The latest backup is computed from the filtered inventory rather than just
/// the current page, so summary cards remain correct while paging.
/// </summary>
public sealed record LocalBackupEntryListSummary(
    int TotalBackups,
    int TotalStacks,
    long TotalBytes,
    long TotalFiles,
    LocalBackupCatalogEntry? LatestBackup);

/// <summary>
/// Compact paged response for the flat local-backup inventory. The existing
/// stack-group catalog response remains separate so the "By stack" UI can
/// preserve its grouped-card projection without loading a table-shaped view.
/// </summary>
public sealed record LocalBackupEntryListResponse(
    string Source,
    string Status,
    string BackupsRootPath,
    LocalBackupEntryQuery Query,
    LocalBackupEntryListSummary Summary,
    int TotalBackups,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<string> AvailableStacks,
    IReadOnlyList<LocalBackupCatalogEntry> Backups,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record LocalBackupStackCatalogResponse(
    string Source,
    string Status,
    string BackupsRootPath,
    LocalBackupCatalogStack? Stack,
    string? Detail);

public sealed record LocalBackupDetailResponse(
    string Source,
    string Status,
    string StackSlug,
    string BackupId,
    string BackupRootPath,
    string? ManifestPath,
    bool ManifestPresent,
    DateTime? CreatedAtUtc,
    LocalBackupManifest? Manifest,
    LocalBackupCatalogComponents Components,
    long TotalBytes,
    long TotalFiles,
    IReadOnlyList<string> Warnings,
    string? Detail);


/// <summary>
/// Result of intentionally deleting one local backup artifact. This operation
/// removes only the local backup directory; it does not alter portable exports,
/// validated imports, or restore-session/audit history.
/// </summary>
public sealed record LocalBackupDeleteResponse(
    string Source,
    string Status,
    string StackSlug,
    string BackupId,
    string BackupRootPath,
    long DeletedBytes,
    long DeletedFiles,
    bool StackDirectoryRemoved,
    DateTimeOffset DeletedAtUtc,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record LocalBackupCatalogStack(
    string StackSlug,
    int BackupCount,
    string? LatestBackupId,
    DateTime? LatestCreatedAtUtc,
    long TotalBytes,
    long TotalFiles,
    IReadOnlyList<LocalBackupCatalogEntry> Backups,
    IReadOnlyList<string> Warnings);

public sealed record LocalBackupCatalogEntry(
    string StackSlug,
    string BackupId,
    string BackupRootPath,
    string? ManifestPath,
    bool ManifestPresent,
    DateTime? CreatedAtUtc,
    LocalBackupCatalogComponents Components,
    long TotalBytes,
    long TotalFiles,
    IReadOnlyList<string> Warnings);

public sealed record LocalBackupCatalogComponents(
    LocalBackupCatalogFileComponent DatabaseDump,
    LocalBackupCatalogFileComponent MatrixConfig,
    LocalBackupCatalogFileComponent MatrixSigningKey,
    LocalBackupCatalogDirectoryComponent MatrixMediaStore,
    LocalBackupCatalogFileComponent ElementConfig);

public sealed record LocalBackupCatalogFileComponent(
    bool Present,
    string RelativePath,
    string AbsolutePath,
    long Bytes);

public sealed record LocalBackupCatalogDirectoryComponent(
    bool Present,
    string RelativePath,
    string AbsolutePath,
    long Bytes,
    long Files);