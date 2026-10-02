using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Api.IntegrationTests.Runtime;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Persistence;

public sealed class CatalogPortableExportServicePersistenceTests
{
    private const string StackLogoBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAZUlEQVR42u3QQREAAAQAMDGEFlITcjh7rMAiq+ezECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAu5boJcyWUwYGNkAAAAASUVORK5CYII=";

    [Fact]
    public async Task Local_catalog_payload_generates_a_normalised_portable_zip_without_a_validation_id()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-export-data");
        var databasePath = CreateDatabasePath();
        var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

        try
        {
            const string catalogEntryId = "bkp_catalog_export_local";
            const string matrixServerName = "matrix.cool-stack.test";
            await WritePayloadAsync(
                payloadRoot,
                matrixServerName,
                signingKeyFileName: matrixServerName + ".signing.key",
                includePortableManifest: false,
                includeLogo: true);

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId,
                BackupCatalogOriginKinds.LocalCaptured,
                payloadRoot,
                sourceStackSlug: "cool-stack",
                validationId: null,
                elementHost: "chat.cool-stack.test");

            var result = await CreateService(dataRoot, db).ExportAsync(
                catalogEntryId,
                CancellationToken.None);

            Assert.Equal("created", result.Status);
            Assert.Equal(catalogEntryId, result.CatalogEntryId);
            Assert.Equal("local-captured", result.OriginKind);
            Assert.Equal("cool-stack", result.SourceStackSlug);
            Assert.StartsWith("catalog-", result.ExportId);
            Assert.Contains("artifacts/portable-exports", result.DownloadPath, StringComparison.Ordinal);
            Assert.True(result.SizeBytes > 0);
            Assert.Contains(result.Warnings, warning => warning.Contains("signing identity", StringComparison.OrdinalIgnoreCase));

            var exportPath = Path.Combine(
                dataRoot,
                "exports",
                "stacks",
                "cool-stack",
                result.DownloadName);

            Assert.True(File.Exists(exportPath));

            using var archive = ZipFile.OpenRead(exportPath);
            Assert.NotNull(archive.GetEntry("mem-stack-export/database/synapse.sql"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/matrix/homeserver.yaml"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/matrix/signing.key"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/matrix/media_store/media.txt"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/element/config.json"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/identity/logo.png"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/backup/backup-manifest.json"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/mem-export-manifest.json"));
            Assert.NotNull(archive.GetEntry("mem-stack-export/backup/checksums.sha256"));

            var manifest = await ReadManifestAsync(archive);
            Assert.Equal("0.2.0-catalog-export-test", manifest.MemVersion);
            Assert.Equal("cool-stack", manifest.Stack.Slug);
            Assert.Equal(matrixServerName, manifest.Stack.MatrixServerName);
            Assert.Equal("chat.cool-stack.test", manifest.Routes.ElementHost);
            Assert.True(manifest.Matrix.Present);
            Assert.NotNull(manifest.Logo);
            Assert.True(manifest.Logo!.Present);
            Assert.Equal("identity/logo.png", manifest.Logo.File);
            Assert.Equal(64, manifest.Logo.Width);
            Assert.Equal(64, manifest.Logo.Height);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(StackLogoBase64))).ToLowerInvariant(),
                manifest.Logo.Sha256);
            Assert.Contains("matrix/signing.key", manifest.IncludedFiles);
            Assert.Contains("identity/logo.png", manifest.IncludedFiles);

            var checksums = await ReadEntryAsync(
                archive,
                "mem-stack-export/backup/checksums.sha256");
            Assert.Contains("mem-stack-export/matrix/signing.key", checksums, StringComparison.Ordinal);
            Assert.Contains("mem-stack-export/identity/logo.png", checksums, StringComparison.Ordinal);
            Assert.Contains("mem-stack-export/mem-export-manifest.json", checksums, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public async Task Imported_catalog_payload_exports_from_materialised_payload_when_original_archive_is_not_needed()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-export-data");
        var databasePath = CreateDatabasePath();
        var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

        try
        {
            const string catalogEntryId = "bkp_catalog_export_imported";
            await WritePayloadAsync(
                payloadRoot,
                homeserverServerName: "matrix.fallback.test",
                signingKeyFileName: "signing.key",
                includePortableManifest: true);

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                catalogEntryId,
                BackupCatalogOriginKinds.ImportedZip,
                payloadRoot,
                sourceStackSlug: "catalog-fallback-stack",
                validationId: "validation-imported-1",
                elementHost: "chat.fallback.test");

            var result = await CreateService(dataRoot, db).ExportAsync(
                catalogEntryId,
                CancellationToken.None);

            var exportPath = Path.Combine(
                dataRoot,
                "exports",
                "stacks",
                "imported-stack",
                result.DownloadName);

            Assert.Equal("imported-zip", result.OriginKind);
            Assert.Equal("imported-stack", result.SourceStackSlug);
            Assert.True(File.Exists(exportPath));

            using var archive = ZipFile.OpenRead(exportPath);
            var manifest = await ReadManifestAsync(archive);

            Assert.Equal("0.2.0-catalog-export-test", manifest.MemVersion);
            Assert.Equal("imported-stack", manifest.Stack.Slug);
            Assert.Equal("matrix.imported.test", manifest.Stack.MatrixServerName);
            Assert.Equal("chat.imported.test", manifest.Routes.ElementHost);
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public async Task Missing_required_catalog_payload_material_returns_the_catalog_specific_error()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-export-data");
        var databasePath = CreateDatabasePath();
        var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

        try
        {
            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix"));
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, "database", "synapse.sql"), "-- test dump");
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, "matrix", "homeserver.yaml"), "server_name: matrix.missing-key.test");

            await using var db = await CreateDatabaseAsync(databasePath);
            await AddCatalogEntryAsync(
                db,
                "bkp_catalog_export_missing_key",
                BackupCatalogOriginKinds.LocalCaptured,
                payloadRoot,
                sourceStackSlug: "broken-stack",
                validationId: null,
                elementHost: null);

            var exception = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
                () => CreateService(dataRoot, db).ExportAsync(
                    "bkp_catalog_export_missing_key",
                    CancellationToken.None));

            Assert.Equal("backup_catalog_private_staging_payload_incomplete", exception.ErrorCode);
            Assert.Contains("matrix/signing.key", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    private static CatalogPortableExportService CreateService(
        string dataRoot,
        MemDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HostAgent:DataRoot"] = dataRoot
            })
            .Build();

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
        var runtimeContext = TestRuntimeContext.Create(
            Path.Combine(dataRoot, "runtime-context"),
            version: "0.2.0-catalog-export-test");

        return new CatalogPortableExportService(
            configuration,
            resolver,
            runtimeContext);
    }

    private static async Task WritePayloadAsync(
        string payloadRoot,
        string homeserverServerName,
        string signingKeyFileName,
        bool includePortableManifest,
        bool includeLogo = false)
    {
        Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix", "media_store"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "element"));

        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "database", "synapse.sql"), "-- test dump");
        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "matrix", "homeserver.yaml"),
            $"server_name: {homeserverServerName}\n");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "matrix", signingKeyFileName), "ed25519 x test");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "matrix", "media_store", "media.txt"), "media");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "element", "config.json"), "{}");

        object backupManifest = new { backupVersion = "1" };
        if (includeLogo)
        {
            var logoBytes = Convert.FromBase64String(StackLogoBase64);
            var logoSha256 = Convert.ToHexString(SHA256.HashData(logoBytes)).ToLowerInvariant();
            Directory.CreateDirectory(Path.Combine(payloadRoot, "identity"));
            await File.WriteAllBytesAsync(Path.Combine(payloadRoot, "identity", "logo.png"), logoBytes);
            backupManifest = new
            {
                backupVersion = "mem-stack-backup-v2",
                logo = new
                {
                    file = "identity/logo.png",
                    sha256 = logoSha256,
                    bytes = (long)logoBytes.Length,
                    width = 64,
                    height = 64,
                    present = true
                }
            };
        }

        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "backup-manifest.json"),
            JsonSerializer.Serialize(backupManifest));

        if (includePortableManifest)
        {
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "mem-export-manifest.json"),
                """
                {
                  "manifestVersion": 2,
                  "exportKind": "mem-stack-export",
                  "createdAtUtc": "2026-06-29T00:00:00Z",
                  "createdBy": "test",
                  "memVersion": "0.1.1-dev",
                  "stack": {
                    "stackId": null,
                    "slug": "imported-stack",
                    "displayName": "Imported stack",
                    "matrixServerName": "matrix.imported.test",
                    "matrixPublicUrl": "https://matrix.imported.test",
                    "elementPublicUrl": "https://chat.imported.test"
                  },
                  "database": { "engine": "postgres", "dumpFile": "database/synapse.sql", "databaseName": null, "username": null, "present": true },
                  "matrix": { "homeserverConfig": "matrix/homeserver.yaml", "signingKey": "matrix/signing.key", "mediaStore": "matrix/media_store", "mediaBytes": 0, "mediaFiles": 0, "present": true },
                  "element": { "config": "element/config.json", "present": true },
                  "routes": { "matrixHost": "matrix.imported.test", "elementHost": "chat.imported.test", "requiresDns": true },
                  "coturn": { "configured": false, "publicHost": null, "realm": null, "turnUris": [], "sharedSecretPresent": false, "userLifetime": null, "allowGuests": null },
                  "restorePolicy": { "canRestoreToFreshMemServer": true, "requiresPostgres": true, "requiresDomainMapping": true, "requiresSigningKey": true, "requiresOldServerStoppedForSameServerName": true },
                  "integrity": { "checksumsFile": "backup/checksums.sha256" },
                  "includedFiles": [],
                  "warnings": []
                }
                """);
        }
    }

    private static async Task AddCatalogEntryAsync(
        MemDbContext db,
        string catalogEntryId,
        string originKind,
        string payloadRoot,
        string sourceStackSlug,
        string? validationId,
        string? elementHost)
    {
        db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
        {
            Id = Guid.NewGuid(),
            CatalogEntryId = catalogEntryId,
            OriginKind = originKind,
            DisplayName = $"Catalog entry {catalogEntryId}",
            PayloadState = BackupCatalogPayloadStates.Available,
            PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
            PayloadDirectoryPath = payloadRoot,
            SourceStackSlug = sourceStackSlug,
            SourceBackupId = "source-backup-1",
            ValidationId = validationId,
            IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
            IntegritySummary = "Valid",
            WarningCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            ElementHost = elementHost
        });
        await db.SaveChangesAsync();
    }

    private static async Task<MemStackExportManifest> ReadManifestAsync(ZipArchive archive)
    {
        var json = await ReadEntryAsync(archive, "mem-stack-export/mem-export-manifest.json");
        return JsonSerializer.Deserialize<MemStackExportManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Portable manifest was missing.");
    }

    private static async Task<string> ReadEntryAsync(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidOperationException($"Archive entry '{entryName}' was not found.");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
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

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        $"mem-catalog-export-{Guid.NewGuid():N}.db");

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestFiles(string databasePath, string dataRoot)
    {
        if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, recursive: true);
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
