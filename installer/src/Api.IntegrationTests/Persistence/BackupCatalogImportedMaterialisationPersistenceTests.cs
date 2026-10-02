using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class BackupCatalogImportedMaterialisationPersistenceTests
{
    [Fact]
    public async Task Imported_materialisation_preserves_catalog_identity_across_failure_and_retry()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-imported-catalog-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var store = new BackupCatalogStore(db);
            var registration = new BackupCatalogImportedMaterialisationRegistration(
                ValidationId: "20260628-010203Z-import001",
                DisplayName: "Imported backup demo-stack",
                SourceStackSlug: "demo-stack",
                ManifestVersion: 1,
                MemVersion: "0.1.1-dev",
                MatrixServerName: "matrix.demo.test",
                MatrixHost: "matrix.demo.test",
                ElementHost: "chat.demo.test",
                ImportedAtUtc: DateTime.UtcNow,
                IntegrityStatus: BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary: "Validated upload.",
                WarningCount: 0);

            var started = await store.StartImportedMaterialisationAsync(
                registration,
                "/test/catalog/imported/payload",
                CancellationToken.None);

            Assert.Equal("created", started.Action);
            Assert.Equal(BackupCatalogPayloadStates.Materialising, started.PayloadState);

            await store.FailImportedMaterialisationAsync(
                started.CatalogEntryId,
                "Archive extraction failed.",
                CancellationToken.None);

            var retried = await store.StartImportedMaterialisationAsync(
                registration,
                "/test/catalog/imported/payload",
                CancellationToken.None);

            Assert.Equal(started.CatalogEntryId, retried.CatalogEntryId);
            Assert.Equal("resumed", retried.Action);
            Assert.Equal(BackupCatalogPayloadStates.Materialising, retried.PayloadState);

            await store.CompleteImportedMaterialisationAsync(
                retried.CatalogEntryId,
                2048,
                CancellationToken.None);

            var detail = await store.FindByCatalogEntryIdAsync(
                retried.CatalogEntryId,
                CancellationToken.None);

            Assert.NotNull(detail);
            Assert.Equal(BackupCatalogPayloadStates.Available, detail.PayloadState);
            Assert.Equal(2048, detail.PayloadBytes);
            Assert.Equal("20260628-010203Z-import001", detail.ValidationId);
            Assert.NotNull(detail.MaterialisedAtUtc);
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
