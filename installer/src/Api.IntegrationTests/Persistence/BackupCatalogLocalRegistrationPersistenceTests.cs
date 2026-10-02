using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

/// <summary>
/// Exercises local-backup catalog registration against a real SQLite database
/// and the application's EF migration chain.
/// </summary>
public sealed class BackupCatalogLocalRegistrationPersistenceTests
{
    [Fact]
    public async Task Repeating_same_local_backup_registration_keeps_one_catalog_entry()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-backup-catalog-local-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();

                var store = new BackupCatalogStore(db);

                var created = await store.EnsureLocalCapturedAsync(
                    CreateRegistration(
                        payloadDirectoryPath: "/test/backups/stacks/demo-stack/20260628-010203Z",
                        payloadBytes: 1024,
                        manifestVersion: 2,
                        memVersion: "0.2.0-provenance-test",
                        matrixServerName: "demo.test"),
                    CancellationToken.None);

                var updated = await store.EnsureLocalCapturedAsync(
                    CreateRegistration(
                        payloadDirectoryPath: "/test/backups/stacks/demo-stack/20260628-010203Z",
                        payloadBytes: 2048,
                        manifestVersion: null,
                        memVersion: null,
                        matrixServerName: null),
                    CancellationToken.None);

                Assert.Equal("created", created.Action);
                Assert.Equal("updated", updated.Action);
                Assert.Equal(created.CatalogEntryId, updated.CatalogEntryId);
                Assert.Equal(1, await db.BackupCatalogEntries.CountAsync());

                var persisted = await db.BackupCatalogEntries.SingleAsync();
                Assert.Equal(2048, persisted.PayloadBytes);
                Assert.Equal("local-captured", persisted.OriginKind);
                Assert.Equal("available", persisted.PayloadState);
                Assert.Equal(2, persisted.ManifestVersion);
                Assert.Equal("0.2.0-provenance-test", persisted.MemVersion);
                Assert.Equal("demo.test", persisted.MatrixServerName);

                var detail = await store.FindByCatalogEntryIdAsync(
                    persisted.CatalogEntryId,
                    CancellationToken.None);

                Assert.NotNull(detail);
                Assert.Equal(2, detail.ManifestVersion);
                Assert.Equal("0.2.0-provenance-test", detail.MemVersion);
                Assert.Equal("demo.test", detail.MatrixServerName);
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    private static BackupCatalogLocalRegistration CreateRegistration(
        string payloadDirectoryPath,
        long payloadBytes,
        int? manifestVersion,
        string? memVersion,
        string? matrixServerName) => new(
        StackSlug: "demo-stack",
        BackupId: "20260628-010203Z",
        PayloadDirectoryPath: payloadDirectoryPath,
        CapturedAtUtc: DateTime.UtcNow,
        PayloadBytes: payloadBytes,
        WarningCount: 0,
        IntegrityStatus: BackupCatalogIntegrityStatuses.Valid,
        IntegritySummary: "Required backup material is present.",
        MatrixHost: "matrix.demo.test",
        ElementHost: "chat.demo.test")
    {
        ManifestVersion = manifestVersion,
        MemVersion = memVersion,
        MatrixServerName = matrixServerName
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
