using System.Security.Cryptography;
using System.Text.Json;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Resolves restore-facing material exclusively from the managed Backup Catalog
/// payload. Callers must not use ValidationId as a fallback to the uploaded-ZIP
/// store once they have entered this boundary.
/// </summary>
public sealed class BackupCatalogPayloadResolver
{
    private const string StandardRecreateManifestFileName = "mem-export-manifest.json";
    private const string LocalBackupManifestFileName = "backup-manifest.json";

    private const string PrivateStagingDatabaseDumpRelativePath = "database/synapse.sql";
    private const string PrivateStagingHomeserverRelativePath = "matrix/homeserver.yaml";
    private const string PrivateStagingSigningKeyRelativePath = "matrix/signing.key";
    private const string PrivateStagingMediaStoreRelativePath = "matrix/media_store";
    private const string PrivateStagingElementConfigRelativePath = "element/config.json";
    private const string StackLogoRelativePath = "identity/logo.png";

    private readonly BackupCatalogStore _catalogStore;

    public BackupCatalogPayloadResolver(BackupCatalogStore catalogStore)
    {
        _catalogStore = catalogStore;
    }

    public async Task<BackupCatalogResolvedPayload> ResolveDatabaseDumpAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var payload = await ResolvePayloadAsync(catalogEntryId, ct);
        var databaseDumpPath = Path.Combine(payload.PayloadRootPath, "database", "synapse.sql");

        if (!File.Exists(databaseDumpPath))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_database_dump_not_found",
                $"Backup Catalog entry '{catalogEntryId}' does not contain database/synapse.sql.");
        }

        return payload with { DatabaseDumpPath = databaseDumpPath };
    }

    /// <summary>
    /// Resolves direct private-staging source material from one managed Backup
    /// Catalog payload. This is intentionally independent of the legacy
    /// validated-import ZIP store and does not manufacture an archive.
    /// </summary>
    public async Task<PrivateStagingSourceMaterial>
        ResolvePrivateStagingSourceMaterialAsync(
            string catalogEntryId,
            CancellationToken ct)
    {
        var payload = await ResolvePayloadAsync(catalogEntryId, ct);

        var databaseDumpPath = RequirePrivateStagingFile(
            payload,
            PrivateStagingDatabaseDumpRelativePath,
            "backup_catalog_private_staging_payload_incomplete");

        var homeserverPath = RequirePrivateStagingFile(
            payload,
            PrivateStagingHomeserverRelativePath,
            "backup_catalog_private_staging_homeserver_config_missing");

        var homeserverMatrixServerName = await TryReadHomeserverServerNameAsync(
            payload.CatalogEntryId,
            homeserverPath,
            ct);

        var manifest = string.Equals(
                payload.OriginKind,
                BackupCatalogOriginKinds.ImportedZip,
                StringComparison.Ordinal)
            ? await TryReadPortableManifestAsync(payload.PayloadRootPath, ct)
            : null;

        var manifestMatrixServerName = TrimToNull(
            manifest?.Stack?.MatrixServerName);

        var matrixServerName = manifestMatrixServerName ?? homeserverMatrixServerName;

        if (string.IsNullOrWhiteSpace(matrixServerName))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_private_staging_matrix_identity_missing",
                $"Backup Catalog entry '{catalogEntryId}' does not expose a usable Matrix server_name in {PrivateStagingHomeserverRelativePath}.");
        }

        var signingKeyPath = ResolvePrivateStagingSigningKeyPath(
            payload,
            homeserverMatrixServerName ?? matrixServerName);

        var manifestSourceStackSlug = TrimToNull(manifest?.Stack?.Slug);
        var mediaStorePath = Path.Combine(
            payload.PayloadRootPath,
            PrivateStagingMediaStoreRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var elementConfigPath = Path.Combine(
            payload.PayloadRootPath,
            PrivateStagingElementConfigRelativePath.Replace('/', Path.DirectorySeparatorChar));

        return new PrivateStagingSourceMaterial(
            Kind: PrivateStagingSourceKinds.BackupCatalog,
            CatalogEntryId: payload.CatalogEntryId,
            ValidationId: payload.ValidationId,
            MatrixServerName: matrixServerName,
            SourceStackSlug: manifestSourceStackSlug ?? payload.SourceStackSlug,
            DatabaseDumpPath: databaseDumpPath,
            DatabaseDumpFormat: PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql,
            HomeserverPath: homeserverPath,
            SigningKeyPath: signingKeyPath,
            MediaStorePath: Directory.Exists(mediaStorePath)
                ? mediaStorePath
                : null,
            ElementConfigPath: File.Exists(elementConfigPath)
                ? elementConfigPath
                : null);
    }

    /// <summary>
    /// Resolves direct source material for a catalog-native Standard Recreate
    /// execution. The returned paths are managed catalog payload paths and must
    /// only be used inside HostAgent; no caller may fall back to a legacy uploaded
    /// ZIP using ValidationId after this boundary.
    /// </summary>
    public async Task<BackupCatalogStandardRecreateExecutionMaterial>
        ResolveStandardRecreateExecutionMaterialAsync(
            string catalogEntryId,
            CancellationToken ct)
    {
        var resolvedPayload = await ResolvePayloadAsync(
            catalogEntryId,
            ct);
        var privateMaterial = await ResolvePrivateStagingSourceMaterialAsync(
            catalogEntryId,
            ct);
        var recreateSource = await ResolveStandardRecreateSourceAsync(
            catalogEntryId,
            ct);

        if (string.IsNullOrWhiteSpace(recreateSource.MatrixServerName))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_standard_recreate_matrix_identity_missing",
                $"Backup Catalog entry '{catalogEntryId}' does not expose the Matrix server identity required for a standard restore.");
        }

        var stackLogo = await ResolveStackLogoMaterialAsync(
            resolvedPayload,
            recreateSource.BackupLogo,
            ct);

        return new BackupCatalogStandardRecreateExecutionMaterial(
            CatalogEntryId: recreateSource.CatalogEntryId,
            CatalogEntryDatabaseId: resolvedPayload.EntryId,
            OriginKind: recreateSource.OriginKind,
            IntegrityStatus: recreateSource.IntegrityStatus,
            WarningCount: recreateSource.WarningCount,
            SourceStackSlug: privateMaterial.SourceStackSlug,
            MatrixServerName: recreateSource.MatrixServerName,
            SourceElementHost: recreateSource.SourceElementHost,
            DeclaresCoturnConfigured: recreateSource.DeclaresCoturnConfigured,
            BackupCoturn: recreateSource.BackupCoturn,
            StackLogo: stackLogo,
            DatabaseDumpPath: privateMaterial.DatabaseDumpPath,
            HomeserverPath: privateMaterial.HomeserverPath,
            SigningKeyPath: privateMaterial.SigningKeyPath,
            MediaStorePath: privateMaterial.MediaStorePath,
            ElementConfigPath: privateMaterial.ElementConfigPath);
    }

    /// <summary>
    /// Resolves the source identity and safe recovery metadata required for a
    /// catalog-native Standard Recreate preflight. Imported ZIP material prefers
    /// its portable manifest; local captures recover the Matrix server identity
    /// directly from matrix/homeserver.yaml. This method never reads the legacy
    /// uploaded-ZIP store and does not require a portable manifest.
    /// </summary>
    public async Task<BackupCatalogStandardRecreateSource>
        ResolveStandardRecreateSourceAsync(
            string catalogEntryId,
            CancellationToken ct)
    {
        var payload = await ResolvePayloadAsync(catalogEntryId, ct);
        var manifest = string.Equals(
                payload.OriginKind,
                BackupCatalogOriginKinds.ImportedZip,
                StringComparison.Ordinal)
            ? await TryReadPortableManifestAsync(
                payload.PayloadRootPath,
                ct)
            : null;
        var localManifest = string.Equals(
                payload.OriginKind,
                BackupCatalogOriginKinds.LocalCaptured,
                StringComparison.Ordinal)
            ? await TryReadLocalBackupManifestAsync(
                payload.PayloadRootPath,
                ct)
            : null;
        var backupCoturn = manifest?.Coturn ??
                           ToPortableCoturn(localManifest?.Coturn);
        var backupLogo = manifest?.Logo ??
                         ToPortableLogo(localManifest?.Logo);

        var homeserverPath = Path.Combine(
            payload.PayloadRootPath,
            PrivateStagingHomeserverRelativePath.Replace('/', Path.DirectorySeparatorChar));

        var homeserverMatrixServerName = File.Exists(homeserverPath)
            ? await TryReadHomeserverServerNameAsync(
                payload.CatalogEntryId,
                homeserverPath,
                ct)
            : null;

        var matrixServerName = FirstKnownValue(
            manifest?.Stack?.MatrixServerName,
            manifest?.Routes?.MatrixHost,
            manifest?.Stack?.MatrixPublicUrl,
            localManifest?.Routes?.MatrixHost,
            localManifest?.Routes?.MatrixPublicUrl,
            homeserverMatrixServerName);


        var sourceElementHost = FirstKnownValue(
            manifest?.Routes?.ElementHost,
            manifest?.Stack?.ElementPublicUrl,
            localManifest?.Routes?.ElementHost,
            localManifest?.Routes?.ElementPublicUrl,
            payload.ElementHost);

        return new BackupCatalogStandardRecreateSource(
            CatalogEntryId: payload.CatalogEntryId,
            OriginKind: payload.OriginKind,
            IntegrityStatus: payload.IntegrityStatus,
            WarningCount: payload.WarningCount,
            MatrixServerName: matrixServerName,
            SourceElementHost: sourceElementHost,
            RequiresOldServerStoppedForSameServerName:
                manifest?.RestorePolicy?.RequiresOldServerStoppedForSameServerName ?? true,
            DeclaresCoturnConfigured:
                backupCoturn?.Configured == true,
            BackupCoturn: backupCoturn,
            BackupLogo: backupLogo);
    }


    /// <summary>
    /// Resolves the immutable export manifest directly from the managed catalog
    /// payload for a read-only Standard Recreate preflight. This method never
    /// reads the original uploaded ZIP or calls the validated-import store.
    /// </summary>
    public async Task<BackupCatalogStandardRecreatePreflightPayload>
        ResolveStandardRecreatePreflightPayloadAsync(
            string catalogEntryId,
            CancellationToken ct)
    {
        var payload = await ResolvePayloadAsync(catalogEntryId, ct);
        var manifestPath = Path.Combine(
            payload.PayloadRootPath,
            StandardRecreateManifestFileName);

        if (!File.Exists(manifestPath))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_standard_recreate_manifest_not_found",
                $"Backup Catalog entry '{catalogEntryId}' does not contain {StandardRecreateManifestFileName}.");
        }

        MemStackExportManifest? manifest;

        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            manifest = await JsonSerializer.DeserializeAsync<MemStackExportManifest>(
                manifestStream,
                JsonOptions,
                ct);
        }
        catch (JsonException ex)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_standard_recreate_manifest_invalid",
                $"Backup Catalog entry '{catalogEntryId}' contains an invalid {StandardRecreateManifestFileName}: {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_standard_recreate_manifest_unreadable",
                $"Backup Catalog entry '{catalogEntryId}' could not read {StandardRecreateManifestFileName}: {ex.Message}");
        }

        if (manifest is null ||
            manifest.Stack is null ||
            manifest.Routes is null ||
            manifest.RestorePolicy is null)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_standard_recreate_manifest_incomplete",
                $"Backup Catalog entry '{catalogEntryId}' contains an incomplete {StandardRecreateManifestFileName}.");
        }

        return new BackupCatalogStandardRecreatePreflightPayload(
            payload.CatalogEntryId,
            payload.OriginKind,
            payload.IntegrityStatus,
            payload.WarningCount,
            payload.PayloadRootPath,
            manifestPath,
            manifest);
    }


    /// <summary>
    /// Resolves all material needed by the read-only Production Restore plan
    /// directly from the managed Backup Catalog payload. This is deliberately
    /// independent of the legacy validated-import ZIP store.
    /// </summary>
    public async Task<BackupCatalogProductionRestorePlanPayload>
        ResolveProductionRestorePlanPayloadAsync(
            string catalogEntryId,
            CancellationToken ct)
    {
        var preflightPayload = await ResolveStandardRecreatePreflightPayloadAsync(
            catalogEntryId,
            ct);

        var requiredPaths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["database/synapse.sql"] = Path.Combine(
                preflightPayload.PayloadRootPath,
                "database",
                "synapse.sql"),
            ["matrix/homeserver.yaml"] = Path.Combine(
                preflightPayload.PayloadRootPath,
                "matrix",
                "homeserver.yaml"),
            ["matrix/signing.key"] = Path.Combine(
                preflightPayload.PayloadRootPath,
                "matrix",
                "signing.key")
        };

        var missing = requiredPaths
            .Where(pair => !File.Exists(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_production_restore_payload_incomplete",
                $"Backup Catalog entry '{catalogEntryId}' does not contain required Production Restore material: {string.Join(", ", missing)}.");
        }

        var mediaStorePath = Path.Combine(
            preflightPayload.PayloadRootPath,
            "matrix",
            "media_store");

        var mediaFiles = 0L;
        var mediaBytes = 0L;

        if (Directory.Exists(mediaStorePath))
        {
            foreach (var mediaFile in Directory.EnumerateFiles(
                         mediaStorePath,
                         "*",
                         SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                var info = new FileInfo(mediaFile);
                mediaFiles++;
                mediaBytes += info.Length;
            }
        }

        var elementConfigPath = Path.Combine(
            preflightPayload.PayloadRootPath,
            "element",
            "config.json");

        return new BackupCatalogProductionRestorePlanPayload(
            CatalogEntryId: preflightPayload.CatalogEntryId,
            OriginKind: preflightPayload.OriginKind,
            IntegrityStatus: preflightPayload.IntegrityStatus,
            WarningCount: preflightPayload.WarningCount,
            PayloadRootPath: preflightPayload.PayloadRootPath,
            ManifestPath: preflightPayload.ManifestPath,
            Manifest: preflightPayload.Manifest,
            DatabaseDumpPath: requiredPaths["database/synapse.sql"],
            HomeserverConfigPath: requiredPaths["matrix/homeserver.yaml"],
            SigningKeyPath: requiredPaths["matrix/signing.key"],
            MediaStorePresent: Directory.Exists(mediaStorePath),
            MediaFiles: mediaFiles,
            MediaBytes: mediaBytes,
            ElementConfigPresent: File.Exists(elementConfigPath),
            ElementConfigPath: File.Exists(elementConfigPath)
                ? elementConfigPath
                : null);
    }

    private static string ResolvePrivateStagingSigningKeyPath(
        BackupCatalogResolvedPayload payload,
        string homeserverMatrixServerName)
    {
        var portableSigningKeyPath = Path.Combine(
            payload.PayloadRootPath,
            PrivateStagingSigningKeyRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(portableSigningKeyPath))
        {
            return portableSigningKeyPath;
        }

        if (string.Equals(
                payload.OriginKind,
                BackupCatalogOriginKinds.LocalCaptured,
                StringComparison.Ordinal) &&
            IsSafeLocalCaptureSigningKeyName(homeserverMatrixServerName))
        {
            var localCaptureSigningKeyPath = Path.Combine(
                payload.PayloadRootPath,
                "matrix",
                homeserverMatrixServerName + ".signing.key");

            if (File.Exists(localCaptureSigningKeyPath))
            {
                return localCaptureSigningKeyPath;
            }
        }

        throw new BackupCatalogPayloadResolutionException(
            "backup_catalog_private_staging_payload_incomplete",
            $"Backup Catalog entry '{payload.CatalogEntryId}' does not contain required private staging material: {PrivateStagingSigningKeyRelativePath} or matrix/<server_name>.signing.key.");
    }

    private static bool IsSafeLocalCaptureSigningKeyName(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            Path.IsPathRooted(value) ||
            value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        return !string.Equals(value, ".", StringComparison.Ordinal) &&
               !string.Equals(value, "..", StringComparison.Ordinal);
    }

    private static string RequirePrivateStagingFile(
        BackupCatalogResolvedPayload payload,
        string relativePath,
        string errorCode)
    {
        var absolutePath = Path.Combine(
            payload.PayloadRootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolutePath))
        {
            throw new BackupCatalogPayloadResolutionException(
                errorCode,
                $"Backup Catalog entry '{payload.CatalogEntryId}' does not contain required private staging material: {relativePath}.");
        }

        return absolutePath;
    }

    private static async Task<LocalBackupManifest?> TryReadLocalBackupManifestAsync(
        string payloadRootPath,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(
            payloadRootPath,
            LocalBackupManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);
            return await JsonSerializer.DeserializeAsync<LocalBackupManifest>(
                manifestStream,
                JsonOptions,
                ct);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static MemStackExportLogoManifest? ToPortableLogo(
        LocalBackupLogoManifest? logo) =>
        logo is null
            ? null
            : new MemStackExportLogoManifest(
                File: logo.File,
                Sha256: logo.Sha256,
                Bytes: logo.Bytes,
                Width: logo.Width,
                Height: logo.Height,
                Present: logo.Present);

    private static async Task<BackupCatalogStackLogoMaterial?> ResolveStackLogoMaterialAsync(
        BackupCatalogResolvedPayload payload,
        MemStackExportLogoManifest? logo,
        CancellationToken ct)
    {
        if (logo is null || !logo.Present)
        {
            return null;
        }

        var normalizedFile = (logo.File ?? string.Empty)
            .Replace('\\', '/')
            .TrimStart('/');
        if (!string.Equals(normalizedFile, StackLogoRelativePath, StringComparison.Ordinal))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_stack_logo_path_invalid",
                $"Backup Catalog entry '{payload.CatalogEntryId}' declares an unsupported stack logo path.");
        }

        if (logo.Sha256 is null ||
            logo.Sha256.Length != 64 ||
            !logo.Sha256.All(Uri.IsHexDigit) ||
            logo.Bytes <= 0 ||
            logo.Width <= 0 ||
            logo.Height <= 0)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_stack_logo_metadata_invalid",
                $"Backup Catalog entry '{payload.CatalogEntryId}' contains invalid stack logo metadata.");
        }

        var path = Path.Combine(
            payload.PayloadRootPath,
            "identity",
            "logo.png");

        if (!File.Exists(path))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_stack_logo_missing",
                $"Backup Catalog entry '{payload.CatalogEntryId}' declares a custom stack logo but identity/logo.png is missing.");
        }

        var info = new FileInfo(path);
        if (info.Length != logo.Bytes)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_stack_logo_size_mismatch",
                $"Backup Catalog entry '{payload.CatalogEntryId}' stack logo size does not match its captured metadata.");
        }

        await using var stream = File.OpenRead(path);
        var actualSha256 = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, ct))
            .ToLowerInvariant();

        if (!string.Equals(actualSha256, logo.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_stack_logo_hash_mismatch",
                $"Backup Catalog entry '{payload.CatalogEntryId}' stack logo checksum does not match its captured metadata.");
        }

        return new BackupCatalogStackLogoMaterial(
            Path: path,
            Sha256: actualSha256,
            Bytes: logo.Bytes,
            Width: logo.Width,
            Height: logo.Height);
    }

    private static MemStackExportCoturnManifest? ToPortableCoturn(
        LocalBackupCoturnManifest? coturn) =>
        coturn is null
            ? null
            : new MemStackExportCoturnManifest(
                Configured: coturn.Configured,
                PublicHost: coturn.PublicHost,
                Realm: coturn.Realm,
                TurnUris: coturn.TurnUris,
                SharedSecretPresent: coturn.SharedSecretPresent,
                UserLifetime: coturn.UserLifetime,
                AllowGuests: coturn.AllowGuests,
                State: coturn.State,
                Management: coturn.Management,
                ConfigurationSource: coturn.ConfigurationSource,
                ConfigurationSha256: coturn.ConfigurationSha256);

    private static async Task<MemStackExportManifest?> TryReadPortableManifestAsync(
        string payloadRootPath,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(
            payloadRootPath,
            StandardRecreateManifestFileName);

        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            await using var manifestStream = File.OpenRead(manifestPath);

            return await JsonSerializer.DeserializeAsync<MemStackExportManifest>(
                manifestStream,
                JsonOptions,
                ct);
        }
        catch (JsonException)
        {
            // A catalog payload can still be privately staged from its direct
            // Synapse material. Fall back to homeserver.yaml identity.
            return null;
        }
        catch (IOException)
        {
            // Treat an unreadable optional portable manifest as unavailable and
            // fall back to homeserver.yaml rather than reintroducing a ZIP dependency.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string?> TryReadHomeserverServerNameAsync(
        string catalogEntryId,
        string homeserverPath,
        CancellationToken ct)
    {
        try
        {
            await foreach (var rawLine in File.ReadLinesAsync(homeserverPath, ct))
            {
                var candidate = TryReadTopLevelYamlScalar(rawLine, "server_name");

                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
        catch (IOException ex)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_private_staging_homeserver_config_unreadable",
                $"Backup Catalog entry '{catalogEntryId}' could not read {PrivateStagingHomeserverRelativePath}: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_private_staging_homeserver_config_unreadable",
                $"Backup Catalog entry '{catalogEntryId}' could not read {PrivateStagingHomeserverRelativePath}: {ex.Message}");
        }
    }

    private static string? TryReadTopLevelYamlScalar(
        string rawLine,
        string key)
    {
        if (string.IsNullOrWhiteSpace(rawLine) ||
            char.IsWhiteSpace(rawLine[0]))
        {
            return null;
        }

        var trimmed = rawLine.Trim();

        if (trimmed.StartsWith('#'))
        {
            return null;
        }

        var prefix = key + ":";

        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var value = trimmed[prefix.Length..].Trim();

        if (value.Length == 0)
        {
            return null;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1].Trim();
        }

        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);

        if (commentIndex >= 0)
        {
            value = value[..commentIndex].TrimEnd();
        }

        return TrimToNull(value);
    }

    private static string? FirstKnownValue(params string?[] candidates) =>
        candidates
            .Select(TrimToNull)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? null
            : trimmed;
    }

    private async Task<BackupCatalogResolvedPayload> ResolvePayloadAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var source = await _catalogStore.FindRestoreSourceAsync(catalogEntryId, ct);

        if (source is null)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_entry_not_found",
                $"Backup Catalog entry '{catalogEntryId}' was not found.");
        }

        if (!string.Equals(
                source.PayloadState,
                BackupCatalogPayloadStates.Available,
                StringComparison.Ordinal))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_payload_not_available",
                $"Backup Catalog entry '{catalogEntryId}' is not available for restore. Current payload state: '{source.PayloadState}'.");
        }

        if (string.Equals(
                source.IntegrityStatus,
                BackupCatalogIntegrityStatuses.Invalid,
                StringComparison.Ordinal))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_payload_invalid",
                $"Backup Catalog entry '{catalogEntryId}' has invalid integrity status and cannot be used for restore.");
        }

        var payloadRoot = Path.GetFullPath(source.PayloadDirectoryPath);

        if (!Directory.Exists(payloadRoot))
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_payload_not_found",
                $"Backup Catalog payload directory for '{catalogEntryId}' was not found.");
        }

        return new BackupCatalogResolvedPayload(
            source.EntryId,
            source.CatalogEntryId,
            source.ValidationId,
            source.OriginKind,
            source.IntegrityStatus,
            source.WarningCount,
            payloadRoot,
            source.SourceStackSlug,
            source.ElementHost,
            null);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}

public sealed record BackupCatalogResolvedPayload(
    Guid EntryId,
    string CatalogEntryId,
    string? ValidationId,
    string OriginKind,
    string IntegrityStatus,
    int WarningCount,
    string PayloadRootPath,
    string? SourceStackSlug,
    string? ElementHost,
    string? DatabaseDumpPath);

/// <summary>
/// Catalog-native Standard Recreate source metadata. It is derived from the
/// managed payload and deliberately omits ValidationId so callers cannot
/// reintroduce legacy uploaded-ZIP coupling.
/// </summary>
public sealed record BackupCatalogStandardRecreateSource(
    string CatalogEntryId,
    string OriginKind,
    string IntegrityStatus,
    int WarningCount,
    string? MatrixServerName,
    string? SourceElementHost,
    bool RequiresOldServerStoppedForSameServerName,
    bool DeclaresCoturnConfigured,
    MemStackExportCoturnManifest? BackupCoturn,
    MemStackExportLogoManifest? BackupLogo);

/// <summary>
/// Direct internal material for catalog-native Standard Recreate execution.
/// These are HostAgent-only paths and are deliberately not included in browser
/// projections, operation summaries, or restore workspace evidence.
/// </summary>
public sealed record BackupCatalogStandardRecreateExecutionMaterial(
    string CatalogEntryId,
    Guid CatalogEntryDatabaseId,
    string OriginKind,
    string IntegrityStatus,
    int WarningCount,
    string? SourceStackSlug,
    string MatrixServerName,
    string? SourceElementHost,
    bool DeclaresCoturnConfigured,
    MemStackExportCoturnManifest? BackupCoturn,
    BackupCatalogStackLogoMaterial? StackLogo,
    string DatabaseDumpPath,
    string HomeserverPath,
    string SigningKeyPath,
    string? MediaStorePath,
    string? ElementConfigPath);

/// <summary>
/// Catalog-native input for Standard Recreate preflight. It intentionally
/// omits ValidationId so callers cannot accidentally fall back to the legacy
/// uploaded-ZIP store.
/// </summary>
public sealed record BackupCatalogStackLogoMaterial(
    string Path,
    string Sha256,
    long Bytes,
    int Width,
    int Height);

public sealed record BackupCatalogStandardRecreatePreflightPayload(
    string CatalogEntryId,
    string OriginKind,
    string IntegrityStatus,
    int WarningCount,
    string PayloadRootPath,
    string ManifestPath,
    MemStackExportManifest Manifest);


/// <summary>
/// Catalog-native source contract for read-only Production Restore planning.
/// All paths originate from the managed catalog payload. Callers must never
/// use ValidationId as a storage locator after resolving this contract.
/// </summary>
public sealed record BackupCatalogProductionRestorePlanPayload(
    string CatalogEntryId,
    string OriginKind,
    string IntegrityStatus,
    int WarningCount,
    string PayloadRootPath,
    string ManifestPath,
    MemStackExportManifest Manifest,
    string DatabaseDumpPath,
    string HomeserverConfigPath,
    string SigningKeyPath,
    bool MediaStorePresent,
    long MediaFiles,
    long MediaBytes,
    bool ElementConfigPresent,
    string? ElementConfigPath);

public sealed class BackupCatalogPayloadResolutionException : InvalidOperationException
{
    public BackupCatalogPayloadResolutionException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
