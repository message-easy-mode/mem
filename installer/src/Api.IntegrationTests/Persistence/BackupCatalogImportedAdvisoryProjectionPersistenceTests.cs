using System.Text.Json;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Persistence;

/// <summary>
/// Proves that successful portable ZIP validation guidance is exposed as
/// non-blocking catalog advisories rather than as a payload-integrity warning.
/// This protects the operator contract for existing imported catalog rows,
/// including rows created before advisory classification existed.
/// </summary>
public sealed class BackupCatalogImportedAdvisoryProjectionPersistenceTests
{
    [Fact]
    public async Task Legacy_imported_entry_projects_valid_integrity_and_deduplicated_advisories_from_its_validation_receipt()
    {
        var dataRoot = CreateTemporaryDirectory("mem-imported-advisories");
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-imported-advisories-{Guid.NewGuid():N}.db");
        const string validationId = "20260630-070000Z-advisory01";

        try
        {
            await WriteValidationReceiptAsync(dataRoot, validationId);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();

            await using var db = await CreateDatabaseAsync(databasePath);
            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = "bkp_imported_advisory_001",
                OriginKind = BackupCatalogOriginKinds.ImportedZip,
                DisplayName = "Imported backup super-stack-restored",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = Path.Combine(dataRoot, "backups", "catalog", "imported", validationId, "payload"),
                SourceStackSlug = "super-stack-restored",
                ValidationId = validationId,
                MatrixServerName = "matrix-super-stack.deltabox.dev",
                MatrixHost = "matrix-super-stack.deltabox.dev",
                ElementHost = "chat-super-stack.deltabox.dev",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Warning,
                IntegritySummary = "Validated uploaded ZIP is ready for managed catalog materialisation with warnings.",
                WarningCount = 6,
                PayloadBytes = 200_900,
                CreatedAtUtc = DateTime.UtcNow,
                ImportedAtUtc = DateTime.UtcNow,
                MaterialisedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var store = new BackupCatalogStore(
                db,
                new ImportValidationService(configuration));

            var list = await store.ListAsync(CancellationToken.None);
            var listEntry = Assert.Single(list.Entries);

            Assert.Equal(BackupCatalogIntegrityStatuses.Valid, listEntry.IntegrityStatus);
            Assert.Equal(0, listEntry.WarningCount);
            Assert.Equal(4, listEntry.AdvisoryCount);

            var detail = await store.FindByCatalogEntryIdAsync(
                "bkp_imported_advisory_001",
                CancellationToken.None);

            Assert.NotNull(detail);
            Assert.Equal(BackupCatalogIntegrityStatuses.Valid, detail.IntegrityStatus);
            Assert.Equal(0, detail.WarningCount);
            Assert.Equal(4, detail.AdvisoryCount);
            Assert.Contains("Structural, manifest, and checksum validation passed", detail.IntegritySummary);
            Assert.Contains(detail.Advisories!, advisory => advisory.Category == "security");
            Assert.Contains(detail.Advisories!, advisory => advisory.Category == "operational-safety");
            Assert.Contains(detail.Advisories!, advisory => advisory.Category == "configuration");
            Assert.Contains(detail.Advisories!, advisory => advisory.Category == "provenance");

            var source = await store.FindRestoreSourceAsync(
                "bkp_imported_advisory_001",
                CancellationToken.None);

            Assert.NotNull(source);
            Assert.Equal(BackupCatalogIntegrityStatuses.Valid, source.IntegrityStatus);
            Assert.Equal(0, source.WarningCount);
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public void Validated_import_warnings_are_classified_as_deduplicated_operator_advisories()
    {
        var validation = new ImportValidationResponse(
            Source: "control-plane",
            Status: "valid",
            ValidationId: "20260630-070000Z-advisory02",
            UploadedFileName: "super-stack.zip",
            StoredZipPath: "/internal/not-exposed/source.zip",
            ZipBytes: 1,
            ZipEntryCount: 12,
            TotalUncompressedBytes: 200_900,
            ManifestPresent: true,
            ChecksumsPresent: true,
            Manifest: null,
            Integrity: new ImportValidationIntegritySummary(11, 11, 0, 0, 11),
            Checks: [],
            Warnings:
            [
                "This export references Matrix signing identity material. Store and handle it securely.",
                "Restore policy says the old public homeserver must be stopped before restoring the same Matrix server name.",
                "Manifest warning: This export contains Matrix signing identity material. Store it securely.",
                "Manifest warning: Do not run two public homeservers with the same Matrix server name at the same time.",
                "Manifest warning: This ZIP was regenerated from the managed Backup Catalog payload. It does not require the original uploaded ZIP to remain retained.",
                "The backup contains TURN settings in homeserver.yaml but not complete TURN metadata. Production recreate can still preserve the homeserver TURN block."
            ],
            Errors: [],
            Detail: "MEM stack export ZIP validation passed.");

        var presentation = ImportedZipCatalogIntegrityClassifier.Classify(validation);

        Assert.NotNull(presentation);
        Assert.Equal(BackupCatalogIntegrityStatuses.Valid, presentation.IntegrityStatus);
        Assert.Equal(0, presentation.IntegrityWarningCount);
        Assert.Equal(4, presentation.Advisories.Count);
    }

    private static async Task WriteValidationReceiptAsync(
        string dataRoot,
        string validationId)
    {
        var importDirectory = Path.Combine(dataRoot, "imports", "uploads", validationId);
        Directory.CreateDirectory(importDirectory);

        var validation = new ImportValidationResponse(
            Source: "control-plane",
            Status: "valid",
            ValidationId: validationId,
            UploadedFileName: "super-stack.zip",
            StoredZipPath: "/internal/not-exposed/source.zip",
            ZipBytes: 29_500,
            ZipEntryCount: 12,
            TotalUncompressedBytes: 200_900,
            ManifestPresent: true,
            ChecksumsPresent: true,
            Manifest: null,
            Integrity: new ImportValidationIntegritySummary(11, 11, 0, 0, 11),
            Checks: [],
            Warnings:
            [
                "This export references Matrix signing identity material. Store and handle it securely.",
                "Restore policy says the old public homeserver must be stopped before restoring the same Matrix server name.",
                "Manifest warning: This export contains Matrix signing identity material. Store it securely.",
                "Manifest warning: Do not run two public homeservers with the same Matrix server name at the same time.",
                "Manifest warning: This ZIP was regenerated from the managed Backup Catalog payload. It does not require the original uploaded ZIP to remain retained.",
                "The backup contains TURN settings in homeserver.yaml but not complete TURN metadata. Production recreate can still preserve the homeserver TURN block."
            ],
            Errors: [],
            Detail: "MEM stack export ZIP validation passed.");

        var path = Path.Combine(importDirectory, "validation-result.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(validation, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
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
