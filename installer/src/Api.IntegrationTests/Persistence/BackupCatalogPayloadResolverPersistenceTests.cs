using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class BackupCatalogPayloadResolverPersistenceTests
{
    [Fact]
    public async Task Available_catalog_entry_with_database_dump_resolves_payload()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem-catalog-payload-{Guid.NewGuid():N}.db");
        var payloadRoot = Path.Combine(Path.GetTempPath(), $"mem-catalog-payload-root-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            await File.WriteAllTextAsync(Path.Combine(payloadRoot, "database", "synapse.sql"), "-- test dump");

            var options = new DbContextOptionsBuilder<MemDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(), CatalogEntryId = "bkp_payload_001", OriginKind = BackupCatalogOriginKinds.ImportedZip,
                DisplayName = "Imported", PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory, PayloadDirectoryPath = payloadRoot,
                IntegrityStatus = BackupCatalogIntegrityStatuses.Warning, WarningCount = 2, CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var result = await resolver.ResolveDatabaseDumpAsync("bkp_payload_001", CancellationToken.None);
            Assert.Equal("bkp_payload_001", result.CatalogEntryId);
            Assert.True(File.Exists(result.DatabaseDumpPath));
        }
        finally
        {
            if (Directory.Exists(payloadRoot)) Directory.Delete(payloadRoot, true);
            foreach (var path in new[] { databasePath, databasePath+"-shm", databasePath+"-wal" }) if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Missing_catalog_entry_throws_stable_resolution_error()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"mem-catalog-payload-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>().UseSqlite($"Data Source={databasePath}").Options;
            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(db));
            var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(() => resolver.ResolveDatabaseDumpAsync("bkp_missing", CancellationToken.None));
            Assert.Equal("backup_catalog_entry_not_found", ex.ErrorCode);
        }
        finally { foreach (var path in new[] { databasePath, databasePath+"-shm", databasePath+"-wal" }) if (File.Exists(path)) File.Delete(path); }
    }
}
