using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class BackupCatalogRestoreSourcePersistenceTests
{
    [Fact]
    public async Task Available_catalog_entry_resolves_to_internal_restore_source()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-catalog-restore-source-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = "bkp_test_catalog_restore_001",
                OriginKind = BackupCatalogOriginKinds.ImportedZip,
                DisplayName = "Imported backup demo-stack",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = "/test/backups/catalog/bkp_test_catalog_restore_001",
                SourceStackSlug = "demo-stack",
                ValidationId = "20260628-010203Z-import001",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Warning,
                WarningCount = 2,
                CreatedAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            var store = new BackupCatalogStore(db);
            var source = await store.FindRestoreSourceAsync(
                "bkp_test_catalog_restore_001",
                CancellationToken.None);

            Assert.NotNull(source);
            Assert.Equal("bkp_test_catalog_restore_001", source.CatalogEntryId);
            Assert.Equal(BackupCatalogPayloadStates.Available, source.PayloadState);
            Assert.Equal("demo-stack", source.SourceStackSlug);
            Assert.Equal("20260628-010203Z-import001", source.ValidationId);
            Assert.Equal(2, source.WarningCount);
        }
        finally
        {
            foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public async Task Missing_catalog_entry_does_not_resolve_to_restore_source()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-catalog-restore-source-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var store = new BackupCatalogStore(db);

            var source = await store.FindRestoreSourceAsync(
                "bkp_missing",
                CancellationToken.None);

            Assert.Null(source);
        }
        finally
        {
            foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
