namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

/// <summary>
/// Stable internal source kinds for retained private restore staging.
/// Private staging is catalog-bound: managed Backup Catalog material is the
/// only executable source.
/// </summary>
public static class PrivateStagingSourceKinds
{
    public const string BackupCatalog = "backup-catalog";
    public const string MigrationCandidate = "migration-candidate";
}

/// <summary>
/// Trusted database dump formats accepted by the shared private-staging engine.
/// The source adapter, not the browser, selects this value.
/// </summary>
public static class PrivateStagingDatabaseDumpFormats
{
    public const string PostgreSqlPlainSql = "postgresql-plain-sql";
    public const string PostgreSqlCustom = "postgresql-custom";
}

/// <summary>
/// Direct trusted material required to prepare a private Synapse staging runtime.
/// Backup/Restore and Migration own their source adapters while sharing this execution boundary. Paths are internal Host Agent paths and must never be
/// exposed by browser-safe projections. ValidationId, when present, is imported
/// ZIP ingestion provenance only; it is never an execution selector.
/// </summary>
public sealed record PrivateStagingSourceMaterial(
    string Kind,
    string? CatalogEntryId,
    string? ValidationId,
    string MatrixServerName,
    string? SourceStackSlug,
    string DatabaseDumpPath,
    string DatabaseDumpFormat,
    string HomeserverPath,
    string SigningKeyPath,
    string? MediaStorePath,
    string? ElementConfigPath);
