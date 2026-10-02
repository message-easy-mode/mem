namespace Mem.Cli.Models;

/// <summary>
/// Stable, safe CLI result for portable ZIP ingestion. The control plane may
/// retain internal archive paths, but the CLI intentionally does not model or
/// emit those paths. A successful import returns the durable catalog identity
/// required for all subsequent restore work.
/// </summary>
public sealed record BackupCatalogImportCliResult(
    string Source,
    string Status,
    string? ValidationId,
    string UploadedFileName,
    long ZipBytes,
    int ZipEntryCount,
    long TotalUncompressedBytes,
    bool ManifestPresent,
    bool ChecksumsPresent,
    BackupCatalogImportManifestSummary? Manifest,
    BackupCatalogImportIntegritySummary Integrity,
    IReadOnlyList<BackupCatalogImportValidationCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail,
    string? CatalogEntryId,
    string? CatalogPayloadState,
    string? CatalogMaterialisationAction);

public sealed record BackupCatalogImportValidationCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

public sealed record BackupCatalogImportIntegritySummary(
    int ChecksumLines,
    int CheckedFiles,
    int MissingFiles,
    int FailedFiles,
    int PassedFiles);

/// <summary>
/// Deliberately small, operator-safe manifest projection for import output.
/// It excludes raw configuration data, host paths, database credentials, and
/// archive content paths while retaining useful identity and readiness detail.
/// </summary>
public sealed record BackupCatalogImportManifestSummary(
    int ManifestVersion,
    string? ExportKind,
    DateTimeOffset? CreatedAtUtc,
    string? MemVersion,
    BackupCatalogImportStackSummary Stack,
    BackupCatalogImportDatabaseSummary Database,
    BackupCatalogImportMatrixSummary Matrix,
    BackupCatalogImportElementSummary Element);

public sealed record BackupCatalogImportStackSummary(
    string? Slug,
    string? DisplayName,
    string? MatrixServerName);

public sealed record BackupCatalogImportDatabaseSummary(
    bool Present);

public sealed record BackupCatalogImportMatrixSummary(
    long MediaBytes,
    long MediaFiles,
    bool Present);

public sealed record BackupCatalogImportElementSummary(
    bool Present);

/// <summary>
/// Safe provenance projection for one retained Uploaded ZIP. validationId is
/// intentionally limited to archive/provenance operations and is not a restore
/// attempt identity.
/// </summary>
public sealed record UploadedZipArchiveDetailCliResult(
    string Source,
    string Status,
    string ValidationId,
    string SourceKind,
    string? UploadedFileName,
    DateTimeOffset? RecordedAtUtc,
    long? ArchiveBytes,
    string ArchiveState,
    UploadedZipArchiveValidationCliSummary Validation,
    UploadedZipArchiveManifestCliSummary? Manifest,
    UploadedZipArchiveRetentionCliSummary Retention,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record UploadedZipArchiveValidationCliSummary(
    string Status,
    string Summary,
    int ZipEntryCount,
    long TotalUncompressedBytes,
    bool ManifestPresent,
    bool ChecksumsPresent,
    int PassedChecks,
    int FailedChecks,
    int WarningCount,
    IReadOnlyList<string> Errors);

public sealed record UploadedZipArchiveManifestCliSummary(
    int? ManifestVersion,
    string? MemVersion,
    string? SourceStackSlug,
    string? SourceStackDisplayName,
    string? MatrixServerName,
    int IncludedFileCount);

public sealed record UploadedZipArchiveRetentionCliSummary(
    bool CanDelete,
    string? DeleteBlockReason,
    DateTimeOffset? RemovedAtUtc,
    string? RemovedBy);

public sealed record UploadedZipArchiveDeleteCliResult(
    string Source,
    string Status,
    string ValidationId,
    string ArchiveState,
    long DeletedBytes,
    DateTimeOffset? DeletedAtUtc,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Raw safe projection returned by GET /internal/host-agent/backups/catalog.
/// It intentionally contains no payload directory paths or storage details.
/// </summary>
public sealed record BackupCatalogListApiResponse(
    int TotalCount,
    IReadOnlyList<BackupCatalogListItem> Entries);

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
/// Raw safe projection returned by GET /internal/host-agent/backups/catalog/{catalogEntryId}.
/// This metadata is for provenance and operator inspection only; catalogEntryId
/// remains the only CLI identity for catalog-backed workflows.
/// </summary>
public sealed record BackupCatalogDetailApiResponse(
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

public sealed record BackupCatalogAdvisory(
    string Category,
    string Title,
    string Message);

/// <summary>
/// Stable CLI projection for catalog list output. Status is derived solely from
/// the actual Host Agent transport result; entries are server-projected data.
/// </summary>
public sealed record BackupCatalogListCliResult(
    string Source,
    string Status,
    int TotalCount,
    IReadOnlyList<BackupCatalogListItem> Entries,
    string? Detail);

/// <summary>
/// Stable CLI projection for catalog inspection output. Entry is null only when
/// the Host Agent request did not return a usable catalog detail projection.
/// </summary>
public sealed record BackupCatalogDetailCliResult(
    string Source,
    string Status,
    BackupCatalogDetailApiResponse? Entry,
    string? Detail);

/// <summary>
/// Safe server projection for one Backup Catalog lifecycle check. This does not
/// expose payload paths or turn the retained uploaded ZIP into a restore source.
/// </summary>
public sealed record BackupCatalogLifecycleApiResponse(
    string CatalogEntryId,
    string PayloadState,
    bool PayloadPresent,
    bool HasActiveRestore,
    string? ActiveRestoreSessionId,
    bool CanDelete,
    string? DeleteBlockReason,
    BackupCatalogOriginalArchiveApiResponse? OriginalArchive);

public sealed record BackupCatalogOriginalArchiveApiResponse(
    string ValidationId,
    string ArchiveState,
    string? OriginalFileName,
    long? ArchiveBytes,
    bool CatalogEntryLinked,
    string? CatalogEntryId,
    string Detail);

/// <summary>
/// Stable CLI lifecycle result. Lifecycle is null only when the control plane
/// did not return a usable lifecycle projection.
/// </summary>
public sealed record BackupCatalogLifecycleCliResult(
    string Source,
    string Status,
    BackupCatalogLifecycleApiResponse? Lifecycle,
    string? Detail);

/// <summary>
/// Safe CLI result for an irreversible catalog delete. A blocked result is
/// emitted when the lifecycle check identifies an active restore session. The
/// Host Agent remains the final authority and also rejects a racing delete.
/// </summary>
public sealed record BackupCatalogPermanentDeleteCliResult(
    string Source,
    string Status,
    string CatalogEntryId,
    string? OriginKind,
    string? DeletedBy,
    bool PayloadDeleted,
    bool OriginalArchiveDeleted,
    int PortableExportsDeleted,
    int DetachedRestoreAttempts,
    string? ActiveRestoreSessionId,
    string? Detail);

internal sealed record BackupCatalogPermanentDeleteRequest(
    string Operator);

/// <summary>
/// Server response for creating a portable export from a managed Backup Catalog
/// payload. It intentionally omits control-plane filesystem paths.
/// </summary>
public sealed record BackupCatalogPortableExportCreateCliResult(
    string Source,
    string Status,
    string CatalogEntryId,
    string? OriginKind,
    string? SourceStackSlug,
    string? ExportId,
    string? DownloadName,
    long? SizeBytes,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record PortableExportDownloadCliResult(
    string Source,
    string Status,
    string ExportId,
    string? DownloadName,
    string? ContentType,
    byte[] Bytes,
    string? Detail);

/// <summary>
/// Final operator-safe export outcome. OutputPath is the local path explicitly
/// chosen by the CLI operator; no control-plane storage path is included.
/// </summary>
public sealed record BackupCatalogPortableExportCliResult(
    string Source,
    string Status,
    string CatalogEntryId,
    string? OriginKind,
    string? SourceStackSlug,
    string? ExportId,
    string? DownloadName,
    string? OutputPath,
    long? SizeBytes,
    long BytesWritten,
    IReadOnlyList<string> Warnings,
    string? Detail);
