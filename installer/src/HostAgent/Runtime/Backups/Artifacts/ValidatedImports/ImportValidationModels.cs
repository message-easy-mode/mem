namespace HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

public sealed record ImportValidationResponse(
    string Source,
    string Status,
    string ValidationId,
    string UploadedFileName,
    string StoredZipPath,
    long ZipBytes,
    int ZipEntryCount,
    long TotalUncompressedBytes,
    bool ManifestPresent,
    bool ChecksumsPresent,
    MemStackExportManifestSummary? Manifest,
    ImportValidationIntegritySummary Integrity,
    IReadOnlyList<ImportValidationCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail,
    string? RestoreSessionId = null,
    bool RestoreAttemptCreated = false,
    bool RestoreAttemptResumed = false,
    string? CatalogEntryId = null,
    string? CatalogPayloadState = null,
    string? CatalogMaterialisationAction = null);

public sealed record ImportValidationCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

public sealed record ImportValidationIntegritySummary(
    int ChecksumLines,
    int CheckedFiles,
    int MissingFiles,
    int FailedFiles,
    int PassedFiles);

public sealed record MemStackExportManifestSummary(
    int ManifestVersion,
    string? ExportKind,
    DateTimeOffset? CreatedAtUtc,
    string? CreatedBy,
    string? MemVersion,
    MemStackExportStackSummary Stack,
    MemStackExportDatabaseSummary Database,
    MemStackExportMatrixSummary Matrix,
    MemStackExportElementSummary Element,
    MemStackExportRoutesSummary Routes,
    MemStackExportCoturnSummary Coturn,
    MemStackExportRestorePolicySummary RestorePolicy,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> Warnings);

public sealed record MemStackExportStackSummary(
    string? StackId,
    string? Slug,
    string? DisplayName,
    string? MatrixServerName,
    string? MatrixPublicUrl,
    string? ElementPublicUrl);

public sealed record MemStackExportDatabaseSummary(
    string? Engine,
    string? DumpFile,
    string? DatabaseName,
    string? Username,
    bool Present);

public sealed record MemStackExportMatrixSummary(
    string? HomeserverConfig,
    string? SigningKey,
    string? MediaStore,
    long MediaBytes,
    long MediaFiles,
    bool Present);

public sealed record MemStackExportElementSummary(
    string? Config,
    bool Present);

public sealed record MemStackExportRoutesSummary(
    string? MatrixHost,
    string? ElementHost,
    bool RequiresDns);

public sealed record MemStackExportCoturnSummary(
    bool Configured,
    string? PublicHost,
    string? Realm,
    IReadOnlyList<string> TurnUris,
    bool SharedSecretPresent = false,
    string? UserLifetime = null,
    bool? AllowGuests = null);

public sealed record MemStackExportRestorePolicySummary(
    bool CanRestoreToFreshMemServer,
    bool RequiresPostgres,
    bool RequiresDomainMapping,
    bool RequiresSigningKey,
    bool RequiresOldServerStoppedForSameServerName);