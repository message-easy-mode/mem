using System.Security.Cryptography;
using System.Text.Json;
using Api.IntegrationTests.Runtime;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Persistence;

/// <summary>
/// Exercises the intended external-import boundary end to end: a portable ZIP
/// is validated, materialised into the canonical Backup Catalog, and does not
/// create a restore workspace until an operator explicitly starts recovery.
/// </summary>
public sealed class ValidatedImportCatalogIngestionPersistenceTests
{
    private const string StackLogoBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAZUlEQVR42u3QQREAAAQAMDGEFlITcjh7rMAiq+ezECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAu5boJcyWUwYGNkAAAAASUVORK5CYII=";

    [Fact]
    public async Task Valid_uploaded_export_is_materialised_into_catalog_without_creating_a_restore_attempt()
    {
        var dataRoot = CreateTemporaryDirectory("mem-import-catalog-ingestion");
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem-import-catalog-ingestion-{Guid.NewGuid():N}.db");
        var sourcePayloadRoot = Path.Combine(dataRoot, "source-payload");

        try
        {
            const string sourceCatalogEntryId = "bkp_source_local_001";
            const string matrixServerName = "matrix.import-source.test";
            await WritePayloadAsync(sourcePayloadRoot, matrixServerName);

            await using var db = await CreateDatabaseAsync(databasePath);
            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = sourceCatalogEntryId,
                OriginKind = BackupCatalogOriginKinds.LocalCaptured,
                DisplayName = "Local backup import-source",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = sourcePayloadRoot,
                SourceStackSlug = "import-source",
                SourceBackupId = "20260630-010203Z",
                MatrixServerName = matrixServerName,
                MatrixHost = matrixServerName,
                ElementHost = "chat.import-source.test",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Valid local source payload.",
                WarningCount = 0,
                CreatedAtUtc = DateTime.UtcNow,
                CapturedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();

            var catalogStore = new BackupCatalogStore(db);
            var portableExporter = new CatalogPortableExportService(
                configuration,
                new BackupCatalogPayloadResolver(catalogStore),
                TestRuntimeContext.Create(
                    Path.Combine(dataRoot, "runtime-context"),
                    version: "0.2.0-import-ingestion-test"));
            var portableExport = await portableExporter.ExportAsync(
                sourceCatalogEntryId,
                CancellationToken.None);

            var portableArchivePath = Path.Combine(
                dataRoot,
                "exports",
                "stacks",
                "import-source",
                portableExport.DownloadName);

            await using var sourceStream = File.OpenRead(portableArchivePath);
            var uploadedFile = new FormFile(
                sourceStream,
                0,
                sourceStream.Length,
                "file",
                portableExport.DownloadName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/zip"
            };

            var validationService = new ImportValidationService(configuration);
            var materialisationService = new ImportedZipBackupCatalogMaterialisationService(
                configuration,
                validationService,
                catalogStore);
            var ingestionService = new ValidatedImportCatalogIngestionService(
                validationService,
                materialisationService);

            var result = await ingestionService.ValidateAndIngestAsync(
                uploadedFile,
                CancellationToken.None);

            Assert.Equal("valid", result.Status);
            Assert.False(string.IsNullOrWhiteSpace(result.CatalogEntryId));
            Assert.Equal(BackupCatalogPayloadStates.Available, result.CatalogPayloadState);
            Assert.Equal("created", result.CatalogMaterialisationAction);
            Assert.Null(result.RestoreSessionId);
            Assert.False(result.RestoreAttemptCreated);
            Assert.False(result.RestoreAttemptResumed);

            var projectedCatalogStore = new BackupCatalogStore(
                db,
                validationService);

            var importedEntry = await projectedCatalogStore.FindByCatalogEntryIdAsync(
                result.CatalogEntryId!,
                CancellationToken.None);

            Assert.NotNull(importedEntry);
            Assert.Equal(BackupCatalogOriginKinds.ImportedZip, importedEntry.OriginKind);
            Assert.Equal(result.ValidationId, importedEntry.ValidationId);
            Assert.Equal(BackupCatalogPayloadStates.Available, importedEntry.PayloadState);
            Assert.Equal("import-source", importedEntry.SourceStackSlug);
            Assert.Equal(BackupCatalogIntegrityStatuses.Valid, importedEntry.IntegrityStatus);
            Assert.Equal(0, importedEntry.WarningCount);
            Assert.Equal(3, importedEntry.AdvisoryCount);

            var importedMaterial = await new BackupCatalogPayloadResolver(projectedCatalogStore)
                .ResolveStandardRecreateExecutionMaterialAsync(
                    result.CatalogEntryId!,
                    CancellationToken.None);
            Assert.NotNull(importedMaterial.StackLogo);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(StackLogoBase64))).ToLowerInvariant(),
                importedMaterial.StackLogo!.Sha256);
            Assert.Equal(64, importedMaterial.StackLogo.Width);
            Assert.Equal(64, importedMaterial.StackLogo.Height);
            Assert.True(File.Exists(importedMaterial.StackLogo.Path));
            Assert.EndsWith(
                Path.Combine("identity", "logo.png"),
                importedMaterial.StackLogo.Path,
                StringComparison.Ordinal);

            Assert.Empty(await db.RestoreAttempts.AsNoTracking().ToListAsync());
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    private static async Task WritePayloadAsync(string payloadRoot, string matrixServerName)
    {
        Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix", "media_store"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "element"));
        Directory.CreateDirectory(Path.Combine(payloadRoot, "identity"));

        var logoBytes = Convert.FromBase64String(StackLogoBase64);
        var logoSha256 = Convert.ToHexString(SHA256.HashData(logoBytes)).ToLowerInvariant();

        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "database", "synapse.sql"), "-- test dump");
        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "matrix", "homeserver.yaml"),
            $"server_name: {matrixServerName}\n");
        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "matrix", matrixServerName + ".signing.key"),
            "ed25519 a_test_signing_key");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "matrix", "media_store", "media.txt"), "media");
        await File.WriteAllTextAsync(Path.Combine(payloadRoot, "element", "config.json"), "{}");
        await File.WriteAllBytesAsync(Path.Combine(payloadRoot, "identity", "logo.png"), logoBytes);
        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "backup-manifest.json"),
            JsonSerializer.Serialize(new
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
            }));
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

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestFiles(string databasePath, string dataRoot)
    {
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
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
