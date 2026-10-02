using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

/// <summary>
/// Exercises the read-only Backup Catalog projections against the real SQLite
/// migration chain. The assertions intentionally prove that safe API read
/// models do not contain host payload directory paths or storage kinds.
/// </summary>
public sealed class BackupCatalogReadProjectionPersistenceTests
{
    [Fact]
    public async Task List_returns_entries_newest_first_with_safe_metadata()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();

                db.BackupCatalogEntries.AddRange(
                    CreateEntry(
                        catalogEntryId: "bkp_older",
                        capturedAtUtc: new DateTime(2026, 6, 25, 1, 0, 0, DateTimeKind.Utc),
                        createdAtUtc: new DateTime(2026, 6, 25, 1, 0, 0, DateTimeKind.Utc),
                        payloadDirectoryPath: "/host/private/backups/older"),
                    CreateEntry(
                        catalogEntryId: "bkp_newer",
                        capturedAtUtc: new DateTime(2026, 6, 26, 1, 0, 0, DateTimeKind.Utc),
                        createdAtUtc: new DateTime(2026, 6, 26, 1, 0, 0, DateTimeKind.Utc),
                        payloadDirectoryPath: "/host/private/backups/newer"));

                await db.SaveChangesAsync();

                var store = new BackupCatalogStore(db);

                var result = await store.ListAsync(CancellationToken.None);

                Assert.Equal(2, result.TotalCount);
                Assert.Equal(
                    new[] { "bkp_newer", "bkp_older" },
                    result.Entries.Select(entry => entry.CatalogEntryId));

                var newest = result.Entries[0];

                Assert.Equal("local-captured", newest.OriginKind);
                Assert.Equal("demo-stack", newest.SourceStackSlug);
                Assert.Equal("20260626-010000Z", newest.SourceBackupId);
                Assert.Equal("available", newest.PayloadState);
                Assert.Equal("valid", newest.IntegrityStatus);
                Assert.Equal(0, newest.WarningCount);

                Assert.DoesNotContain(
                    "PayloadDirectoryPath",
                    typeof(BackupCatalogListItem)
                        .GetProperties()
                        .Select(property => property.Name));

                Assert.DoesNotContain(
                    "PayloadStorageKind",
                    typeof(BackupCatalogListItem)
                        .GetProperties()
                        .Select(property => property.Name));
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task Find_returns_safe_detail_for_existing_catalog_entry()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();

                db.BackupCatalogEntries.Add(CreateEntry(
                    catalogEntryId: "bkp_detail",
                    capturedAtUtc: new DateTime(2026, 6, 27, 1, 0, 0, DateTimeKind.Utc),
                    createdAtUtc: new DateTime(2026, 6, 27, 1, 0, 0, DateTimeKind.Utc),
                    payloadDirectoryPath: "/host/private/backups/detail"));

                await db.SaveChangesAsync();

                var store = new BackupCatalogStore(db);

                var result = await store.FindByCatalogEntryIdAsync(
                    "bkp_detail",
                    CancellationToken.None);

                Assert.NotNull(result);
                Assert.Equal("bkp_detail", result.CatalogEntryId);
                Assert.Equal("Local backup demo-stack", result.DisplayName);
                Assert.Equal("demo-stack", result.SourceStackSlug);
                Assert.Equal("20260626-010000Z", result.SourceBackupId);
                Assert.Equal("Manifest and payload checks verified.", result.IntegritySummary);

                Assert.DoesNotContain(
                    "PayloadDirectoryPath",
                    typeof(BackupCatalogDetailResponse)
                        .GetProperties()
                        .Select(property => property.Name));

                Assert.DoesNotContain(
                    "PayloadStorageKind",
                    typeof(BackupCatalogDetailResponse)
                        .GetProperties()
                        .Select(property => property.Name));
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task Find_returns_null_for_unknown_catalog_entry()
    {
        var databasePath = CreateDatabasePath();

        try
        {
            var options = CreateOptions(databasePath);

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var store = new BackupCatalogStore(db);

            var result = await store.FindByCatalogEntryIdAsync(
                "bkp_missing",
                CancellationToken.None);

            Assert.Null(result);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    private static DbContextOptions<MemDbContext> CreateOptions(
        string databasePath) =>
        new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

    private static string CreateDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"mem-backup-catalog-read-{Guid.NewGuid():N}.db");

    private static BackupCatalogEntryEntity CreateEntry(
        string catalogEntryId,
        DateTime capturedAtUtc,
        DateTime createdAtUtc,
        string payloadDirectoryPath) =>
        new()
        {
            Id = Guid.NewGuid(),
            CatalogEntryId = catalogEntryId,
            OriginKind = BackupCatalogOriginKinds.LocalCaptured,
            DisplayName = "Local backup demo-stack",
            PayloadState = BackupCatalogPayloadStates.Available,
            PayloadStorageKind = BackupCatalogPayloadStorageKinds.LocalBackupDirectory,
            PayloadDirectoryPath = payloadDirectoryPath,
            SourceStackSlug = "demo-stack",
            SourceBackupId = "20260626-010000Z",
            ManifestVersion = 1,
            MemVersion = "0.1.1-dev",
            MatrixServerName = "matrix.demo.test",
            MatrixHost = "matrix.demo.test",
            ElementHost = "chat.demo.test",
            IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
            IntegritySummary = "Manifest and payload checks verified.",
            WarningCount = 0,
            PayloadBytes = 4096,
            CreatedAtUtc = createdAtUtc,
            CapturedAtUtc = capturedAtUtc
        };

    private static void DeleteSqliteArtifacts(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     databasePath + "-shm",
                     databasePath + "-wal"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}