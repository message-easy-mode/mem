namespace HostAgent.Runtime.Backups.Artifacts.PortableExports;

public sealed record PortableExportResult(
    string Source,
    string Status,
    string ExportId,
    string StackSlug,
    string BackupId,
    string ExportPath,
    string DownloadName,
    string DownloadPath,
    long SizeBytes,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record PortableExportDownload(
    string ExportId,
    string ExportPath,
    string DownloadName,
    long SizeBytes);

public sealed record MemStackExportManifest(
    int ManifestVersion,
    string ExportKind,
    DateTime CreatedAtUtc,
    string CreatedBy,
    string MemVersion,
    MemStackExportStackManifest Stack,
    MemStackExportDatabaseManifest Database,
    MemStackExportMatrixManifest Matrix,
    MemStackExportElementManifest Element,
    MemStackExportRoutesManifest Routes,
    MemStackExportCoturnManifest Coturn,
    MemStackExportRestorePolicyManifest RestorePolicy,
    MemStackExportIntegrityManifest Integrity,
    IReadOnlyList<string> IncludedFiles,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Optional custom stack logo carried as bounded PNG recovery material.
    /// Older exports legitimately omit this extension.
    /// </summary>
    public MemStackExportLogoManifest? Logo { get; init; }
}

public sealed record MemStackExportLogoManifest(
    string File,
    string Sha256,
    long Bytes,
    int Width,
    int Height,
    bool Present);

public sealed record MemStackExportStackManifest(
    Guid? StackId,
    string Slug,
    string? DisplayName,
    string? MatrixServerName,
    string? MatrixPublicUrl,
    string? ElementPublicUrl);

public sealed record MemStackExportDatabaseManifest(
    string Engine,
    string DumpFile,
    string? DatabaseName,
    string? Username,
    bool Present);

public sealed record MemStackExportMatrixManifest(
    string HomeserverConfig,
    string SigningKey,
    string MediaStore,
    long MediaBytes,
    long MediaFiles,
    bool Present);

public sealed record MemStackExportElementManifest(
    string Config,
    bool Present);

public sealed record MemStackExportRoutesManifest(
    string? MatrixHost,
    string? ElementHost,
    bool RequiresDns);

public sealed record MemStackExportCoturnManifest(
    bool Configured,
    string? PublicHost,
    string? Realm,
    IReadOnlyList<string> TurnUris,
    bool SharedSecretPresent = false,
    string? UserLifetime = null,
    bool? AllowGuests = null,
    string State = "unknown",
    string Management = "unknown",
    string? ConfigurationSource = null,
    string? ConfigurationSha256 = null);

public sealed record MemStackExportRestorePolicyManifest(
    bool CanRestoreToFreshMemServer,
    bool RequiresPostgres,
    bool RequiresDomainMapping,
    bool RequiresSigningKey,
    bool RequiresOldServerStoppedForSameServerName);

public sealed record MemStackExportIntegrityManifest(
    string ChecksumsFile);