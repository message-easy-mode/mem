using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

/// <summary>
/// Migration-backed persistence checks for the catalog foundation.
/// These tests deliberately use a fresh SQLite file so they exercise the real
/// EF migration chain rather than EF's in-memory provider.
/// </summary>
public sealed class BackupCatalogFoundationPersistenceTests
{
    [Fact]
    public async Task Fresh_database_persists_catalog_entry_and_restore_link()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-backup-catalog-{Guid.NewGuid():N}.db");

        try
        {
            var options = CreateOptions(databasePath);
            var catalogId = Guid.NewGuid();
            var restoreAttemptId = Guid.NewGuid();

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();

                db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
                {
                    Id = catalogId,
                    CatalogEntryId = "bkp_test_local_001",
                    OriginKind = "local-captured",
                    DisplayName = "demo-stack / 20260628-010203Z",
                    PayloadState = "available",
                    PayloadStorageKind = "local-backup-directory",
                    PayloadDirectoryPath = "/test/backups/stacks/demo-stack/20260628-010203Z",
                    SourceStackSlug = "demo-stack",
                    SourceBackupId = "20260628-010203Z",
                    IntegrityStatus = "valid",
                    IntegritySummary = "Manifest and payload checks verified.",
                    WarningCount = 0,
                    PayloadBytes = 1024,
                    CreatedAtUtc = DateTime.UtcNow,
                    CapturedAtUtc = DateTime.UtcNow
                });

                db.RestoreAttempts.Add(new RestoreAttemptEntity
                {
                    Id = restoreAttemptId,
                    RestoreSessionId = "20260628-010203Z-test000",
                    SourceKind = "backup-catalog",
                    SourceKey = "backup-catalog:bkp_test_local_001",
                    ActiveSourceKey = "backup-catalog:bkp_test_local_001",
                    SourceCatalogEntryIdSnapshot = "bkp_test_local_001",
                    SourceDisplayNameSnapshot = "demo-stack / 20260628-010203Z",
                    SourceOriginKindSnapshot = "local-captured",
                    SourceStackSlugSnapshot = "demo-stack",
                    SourceBackupIdSnapshot = "20260628-010203Z",
                    BackupCatalogEntryId = catalogId,
                    Status = "ready",
                    CurrentStage = "source",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    LastEventAtUtc = DateTime.UtcNow,
                    WarningCount = 0,
                    ErrorCount = 0,
                    SessionDirectoryPath = "/test/restores/20260628-010203Z-test000",
                    LogDirectoryPath = "/test/restores/20260628-010203Z-test000/logs"
                });

                await db.SaveChangesAsync();
            }

            await using (var db = new MemDbContext(options))
            {
                var catalog = await db.BackupCatalogEntries
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == catalogId);

                var attempt = await db.RestoreAttempts
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == restoreAttemptId);

                Assert.Equal("bkp_test_local_001", catalog.CatalogEntryId);
                Assert.Equal("local-captured", catalog.OriginKind);
                Assert.Equal(catalogId, attempt.BackupCatalogEntryId);
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task Catalog_entry_id_is_unique()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-backup-catalog-{Guid.NewGuid():N}.db");

        try
        {
            var options = CreateOptions(databasePath);

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            db.BackupCatalogEntries.Add(CreateAvailableEntry("bkp_test_duplicate"));
            await db.SaveChangesAsync();

            db.BackupCatalogEntries.Add(CreateAvailableEntry("bkp_test_duplicate"));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    private static DbContextOptions<MemDbContext> CreateOptions(string databasePath) =>
        new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

    private static BackupCatalogEntryEntity CreateAvailableEntry(string catalogEntryId) => new()
    {
        Id = Guid.NewGuid(),
        CatalogEntryId = catalogEntryId,
        OriginKind = "imported-zip",
        DisplayName = "imported-export.zip",
        PayloadState = "available",
        PayloadStorageKind = "catalog-managed-directory",
        PayloadDirectoryPath = "/test/backups/catalog/test/payload",
        IntegrityStatus = "valid",
        WarningCount = 0,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static void DeleteSqliteArtifacts(string databasePath)
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
