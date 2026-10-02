using System.Text.Json;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class BackupCatalogPrivateStagingPayloadResolverPersistenceTests
{
    [Fact]
    public async Task Local_catalog_payload_without_portable_manifest_resolves_direct_private_staging_material()
    {
        var databasePath = CreateDatabasePath();
        var payloadRoot = CreatePayloadRoot();

        try
        {
            const string matrixServerName = "matrix.cool-stack.test";
            const string signingKeyFileName = matrixServerName + ".signing.key";

            await WriteRequiredPrivateStagingPayloadAsync(
                payloadRoot,
                $"server_name: {matrixServerName}",
                signingKeyFileName);

            var mediaStorePath = Path.Combine(payloadRoot, "matrix", "media_store");
            Directory.CreateDirectory(mediaStorePath);
            await File.WriteAllTextAsync(Path.Combine(mediaStorePath, "media.txt"), "test");

            var elementDirectory = Path.Combine(payloadRoot, "element");
            Directory.CreateDirectory(elementDirectory);
            await File.WriteAllTextAsync(Path.Combine(elementDirectory, "config.json"), "{}");

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_local_staging_001",
                originKind: BackupCatalogOriginKinds.LocalCaptured,
                payloadRoot: payloadRoot,
                sourceStackSlug: "cool-stack",
                validationId: null);

            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var result = await resolver.ResolvePrivateStagingSourceMaterialAsync(
                "bkp_local_staging_001",
                CancellationToken.None);

            Assert.Equal(PrivateStagingSourceKinds.BackupCatalog, result.Kind);
            Assert.Equal("bkp_local_staging_001", result.CatalogEntryId);
            Assert.Null(result.ValidationId);
            Assert.Equal(matrixServerName, result.MatrixServerName);
            Assert.Equal("cool-stack", result.SourceStackSlug);
            Assert.Equal(Path.Combine(payloadRoot, "database", "synapse.sql"), result.DatabaseDumpPath);
            Assert.Equal(PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql, result.DatabaseDumpFormat);
            Assert.Equal(Path.Combine(payloadRoot, "matrix", "homeserver.yaml"), result.HomeserverPath);
            Assert.Equal(Path.Combine(payloadRoot, "matrix", signingKeyFileName), result.SigningKeyPath);
            Assert.Equal(mediaStorePath, result.MediaStorePath);
            Assert.Equal(Path.Combine(elementDirectory, "config.json"), result.ElementConfigPath);
        }
        finally
        {
            DeleteTestFiles(databasePath, payloadRoot);
        }
    }

    [Fact]
    public async Task Local_catalog_payload_without_supported_signing_key_returns_catalog_specific_reason()
    {
        var databasePath = CreateDatabasePath();
        var payloadRoot = CreatePayloadRoot();

        try
        {
            await WriteRequiredPrivateStagingPayloadAsync(
                payloadRoot,
                "server_name: matrix.cool-stack.test",
                signingKeyFileName: null);

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_local_staging_missing_signing_key",
                originKind: BackupCatalogOriginKinds.LocalCaptured,
                payloadRoot: payloadRoot,
                sourceStackSlug: "cool-stack",
                validationId: null);

            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
                () => resolver.ResolvePrivateStagingSourceMaterialAsync(
                    "bkp_local_staging_missing_signing_key",
                    CancellationToken.None));

            Assert.Equal("backup_catalog_private_staging_payload_incomplete", ex.ErrorCode);
            Assert.Contains("matrix/signing.key", ex.Message, StringComparison.Ordinal);
            Assert.Contains("matrix/<server_name>.signing.key", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestFiles(databasePath, payloadRoot);
        }
    }

    [Fact]
    public async Task Missing_homeserver_yaml_returns_catalog_specific_reason()
    {
        var databasePath = CreateDatabasePath();
        var payloadRoot = CreatePayloadRoot();

        try
        {
            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix"));
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, "database", "synapse.sql"), "-- test dump");
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, "matrix", "signing.key"), "ed25519 x test");

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_local_staging_missing_homeserver",
                originKind: BackupCatalogOriginKinds.LocalCaptured,
                payloadRoot: payloadRoot,
                sourceStackSlug: "cool-stack",
                validationId: null);

            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
                () => resolver.ResolvePrivateStagingSourceMaterialAsync(
                    "bkp_local_staging_missing_homeserver",
                    CancellationToken.None));

            Assert.Equal("backup_catalog_private_staging_homeserver_config_missing", ex.ErrorCode);
            Assert.Contains("matrix/homeserver.yaml", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestFiles(databasePath, payloadRoot);
        }
    }

    [Fact]
    public async Task Imported_catalog_payload_prefers_portable_manifest_identity_when_available()
    {
        var databasePath = CreateDatabasePath();
        var payloadRoot = CreatePayloadRoot();

        try
        {
            await WriteRequiredPrivateStagingPayloadAsync(
                payloadRoot,
                "server_name: matrix.fallback.test");
            await WritePortableManifestAsync(
                Path.Combine(payloadRoot, "mem-export-manifest.json"),
                stackSlug: "manifest-stack",
                matrixServerName: "matrix.manifest.test");

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_imported_staging_manifest",
                originKind: BackupCatalogOriginKinds.ImportedZip,
                payloadRoot: payloadRoot,
                sourceStackSlug: "catalog-stack",
                validationId: "20260629-010203Z-import001");

            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var result = await resolver.ResolvePrivateStagingSourceMaterialAsync(
                "bkp_imported_staging_manifest",
                CancellationToken.None);

            Assert.Equal(PrivateStagingSourceKinds.BackupCatalog, result.Kind);
            Assert.Equal("bkp_imported_staging_manifest", result.CatalogEntryId);
            Assert.Equal("20260629-010203Z-import001", result.ValidationId);
            Assert.Equal("matrix.manifest.test", result.MatrixServerName);
            Assert.Equal("manifest-stack", result.SourceStackSlug);
        }
        finally
        {
            DeleteTestFiles(databasePath, payloadRoot);
        }
    }

    [Fact]
    public async Task Imported_catalog_payload_without_portable_manifest_falls_back_to_homeserver_identity()
    {
        var databasePath = CreateDatabasePath();
        var payloadRoot = CreatePayloadRoot();

        try
        {
            await WriteRequiredPrivateStagingPayloadAsync(
                payloadRoot,
                "server_name: matrix.import-fallback.test");

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_imported_staging_fallback",
                originKind: BackupCatalogOriginKinds.ImportedZip,
                payloadRoot: payloadRoot,
                sourceStackSlug: "catalog-stack",
                validationId: "20260629-010203Z-import002");

            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var result = await resolver.ResolvePrivateStagingSourceMaterialAsync(
                "bkp_imported_staging_fallback",
                CancellationToken.None);

            Assert.Equal("matrix.import-fallback.test", result.MatrixServerName);
            Assert.Equal("catalog-stack", result.SourceStackSlug);
            Assert.Equal("20260629-010203Z-import002", result.ValidationId);
        }
        finally
        {
            DeleteTestFiles(databasePath, payloadRoot);
        }
    }

    private static async Task<MemDbContext> CreateDatabaseAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        var db = new MemDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static async Task AddCatalogEntryAsync(
        MemDbContext db,
        string catalogEntryId,
        string originKind,
        string payloadRoot,
        string? sourceStackSlug,
        string? validationId)
    {
        db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
        {
            Id = Guid.NewGuid(),
            CatalogEntryId = catalogEntryId,
            OriginKind = originKind,
            DisplayName = "Private staging test backup",
            PayloadState = BackupCatalogPayloadStates.Available,
            PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
            PayloadDirectoryPath = payloadRoot,
            SourceStackSlug = sourceStackSlug,
            ValidationId = validationId,
            IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
            WarningCount = 0,
            CreatedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    private static async Task WriteRequiredPrivateStagingPayloadAsync(
        string payloadRoot,
        string homeserverYaml,
        string? signingKeyFileName = "signing.key")
    {
        Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix"));
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "database", "synapse.sql"), "-- test dump");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "matrix", "homeserver.yaml"), homeserverYaml);

        if (!string.IsNullOrWhiteSpace(signingKeyFileName))
        {
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "matrix", signingKeyFileName),
                "ed25519 x test");
        }
    }

    private static async Task WritePortableManifestAsync(
        string manifestPath,
        string stackSlug,
        string matrixServerName)
    {
        var manifest = new MemStackExportManifest(
            ManifestVersion: 1,
            ExportKind: "mem-stack-export",
            CreatedAtUtc: DateTime.UtcNow,
            CreatedBy: "test",
            MemVersion: "0.1.1-test",
            Stack: new MemStackExportStackManifest(
                StackId: null,
                Slug: stackSlug,
                DisplayName: stackSlug,
                MatrixServerName: matrixServerName,
                MatrixPublicUrl: null,
                ElementPublicUrl: null),
            Database: new MemStackExportDatabaseManifest(
                Engine: "postgres",
                DumpFile: "database/synapse.sql",
                DatabaseName: null,
                Username: null,
                Present: true),
            Matrix: new MemStackExportMatrixManifest(
                HomeserverConfig: "matrix/homeserver.yaml",
                SigningKey: "matrix/signing.key",
                MediaStore: "matrix/media_store",
                MediaBytes: 0,
                MediaFiles: 0,
                Present: true),
            Element: new MemStackExportElementManifest(
                Config: "element/config.json",
                Present: false),
            Routes: new MemStackExportRoutesManifest(
                MatrixHost: null,
                ElementHost: null,
                RequiresDns: false),
            Coturn: new MemStackExportCoturnManifest(
                Configured: false,
                PublicHost: null,
                Realm: null,
                TurnUris: []),
            RestorePolicy: new MemStackExportRestorePolicyManifest(
                CanRestoreToFreshMemServer: true,
                RequiresPostgres: true,
                RequiresDomainMapping: false,
                RequiresSigningKey: true,
                RequiresOldServerStoppedForSameServerName: true),
            Integrity: new MemStackExportIntegrityManifest(
                ChecksumsFile: "checksums.sha256"),
            IncludedFiles: [],
            Warnings: []);

        await using var stream = File.Create(manifestPath);
        await JsonSerializer.SerializeAsync(stream, manifest);
    }

    private static string CreateDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"mem-catalog-staging-{Guid.NewGuid():N}.db");

    private static string CreatePayloadRoot() =>
        Path.Combine(Path.GetTempPath(), $"mem-catalog-staging-root-{Guid.NewGuid():N}");

    private static void DeleteTestFiles(string databasePath, string payloadRoot)
    {
        if (Directory.Exists(payloadRoot))
        {
            Directory.Delete(payloadRoot, recursive: true);
        }

        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
