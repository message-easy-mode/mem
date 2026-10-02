namespace HostAgent.Runtime.Backups.Artifacts.LocalBackups;

public sealed record LocalBackupCaptureRequest(
    string SlugOrId);

public sealed record LocalBackupCaptureResult(
    Guid OperationId,
    Guid RuntimeStackId,
    string StackSlug,
    string BackupId,
    string BackupRootPath,
    string ManifestPath,
    string DatabaseDumpPath,
    string? MatrixConfigPath,
    string? MatrixSigningKeyPath,
    string? MatrixMediaBackupPath,
    string? ElementConfigPath,
    LocalBackupStats Stats,
    IReadOnlyList<string> Warnings,
    DateTime CreatedAtUtc,
    /// <summary>
    /// Stable Backup Catalog identity when registration completed. A null value
    /// means the payload was captured but catalog registration must be retried
    /// through the explicit local backfill command.
    /// </summary>
    string? CatalogEntryId = null);

public sealed record LocalBackupManifest(
    string BackupVersion,
    string BackupId,
    DateTime CreatedAtUtc,
    Guid RuntimeStackId,
    string StackSlug,
    LocalBackupDatabaseManifest Database,
    LocalBackupMatrixManifest Matrix,
    LocalBackupElementManifest? Element,
    LocalBackupStats Stats,
    IReadOnlyList<string> Warnings)
{
    // These snapshots are captured at backup creation time. They avoid exporting
    // route or TURN metadata from whatever the live runtime happens to look like later.
    public LocalBackupRouteManifest? Routes { get; init; }

    public LocalBackupCoturnManifest? Coturn { get; init; }

    /// <summary>
    /// Exact MEM product version that created this backup. Older backup manifests
    /// may not contain this snapshot and must remain unknown rather than being
    /// attributed to the version performing a later catalog backfill.
    /// </summary>
    public string? MemVersion { get; init; }

    /// <summary>
    /// Matrix federation server_name captured from the runtime service at backup
    /// creation time. This is distinct from the public Matrix route hostname.
    /// </summary>
    public string? MatrixServerName { get; init; }

    /// <summary>
    /// Optional custom stack logo captured with the backup payload. The file path is
    /// always relative to the backup root so the manifest remains portable.
    /// </summary>
    public LocalBackupLogoManifest? Logo { get; init; }
}

public sealed record LocalBackupLogoManifest(
    string File,
    string Sha256,
    long Bytes,
    int Width,
    int Height,
    bool Present = true);

public sealed record LocalBackupDatabaseManifest(
    string Engine,
    string Host,
    int Port,
    string DatabaseName,
    string DatabaseUsername,
    string PasswordSecretKind,
    string DumpPath);

public sealed record LocalBackupMatrixManifest(
    string DataPath,
    string? HomeserverYamlPath,
    string? SigningKeyPath,
    string? MediaStorePath,
    string BackupPath);

public sealed record LocalBackupElementManifest(
    string? DataPath,
    string? ConfigPath,
    string? BackupPath);

public sealed record LocalBackupRouteManifest(
    string? MatrixHost,
    string? MatrixPublicUrl,
    string? ElementHost,
    string? ElementPublicUrl,
    bool RequiresDns);

public sealed record LocalBackupCoturnManifest(
    bool Configured,
    string? PublicHost,
    string? Realm,
    IReadOnlyList<string> TurnUris,
    bool SharedSecretPresent,
    string? UserLifetime,
    bool? AllowGuests,
    string State = "unknown",
    string Management = "unknown",
    string? ConfigurationSource = null,
    string? ConfigurationSha256 = null);

public sealed record LocalBackupStats(
    LocalBackupFileStats DatabaseDump,
    LocalBackupFileStats HomeserverConfig,
    LocalBackupFileStats SigningKey,
    LocalBackupDirectoryStats MediaStore,
    LocalBackupFileStats ElementConfig,
    long TotalBytes,
    long TotalFiles);

public sealed record LocalBackupFileStats(
    bool Included,
    string? Path,
    long Bytes);

public sealed record LocalBackupDirectoryStats(
    bool Included,
    string? Path,
    long Bytes,
    long Files);

/// <summary>
/// Trusted server-resolved runtime material used to capture the first native
/// MEM backup after an accepted migration. Browser input must never populate
/// this contract directly.
/// </summary>
public sealed record LocalBackupMigrationCandidateSource(
    Guid RuntimeIdentity,
    string StackSlug,
    string MatrixServerName,
    string PostgresContainerName,
    string DatabaseName,
    string DatabaseUsername,
    string MatrixDataPath,
    string? ElementDataPath,
    string? MatrixPublicUrl,
    string? ElementPublicUrl);
