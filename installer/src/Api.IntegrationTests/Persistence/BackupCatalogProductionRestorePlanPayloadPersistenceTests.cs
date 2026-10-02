using System.Text.Json;
using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class BackupCatalogProductionRestorePlanPayloadPersistenceTests
{
    [Fact]
    public async Task Warning_catalog_payload_with_required_material_resolves_for_production_plan()
    {
        await using var fixture = await CatalogProductionPlanPayloadFixture.CreateAsync(
            includeSigningKey: true,
            warningCount: 4);

        var resolver = new BackupCatalogPayloadResolver(
            new BackupCatalogStore(fixture.Db));

        var payload = await resolver.ResolveProductionRestorePlanPayloadAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.Equal(fixture.CatalogEntryId, payload.CatalogEntryId);
        Assert.Equal(BackupCatalogIntegrityStatuses.Warning, payload.IntegrityStatus);
        Assert.Equal(4, payload.WarningCount);
        Assert.True(File.Exists(payload.ManifestPath));
        Assert.True(File.Exists(payload.DatabaseDumpPath));
        Assert.True(File.Exists(payload.HomeserverConfigPath));
        Assert.True(File.Exists(payload.SigningKeyPath));
        Assert.Equal("demo-stack", payload.Manifest.Stack.Slug);
        Assert.Equal("matrix.demo.test", payload.Manifest.Stack.MatrixServerName);
        Assert.True(payload.ElementConfigPresent);
    }

    [Fact]
    public async Task Missing_signing_key_returns_stable_production_plan_payload_error()
    {
        await using var fixture = await CatalogProductionPlanPayloadFixture.CreateAsync(
            includeSigningKey: false,
            warningCount: 0);

        var resolver = new BackupCatalogPayloadResolver(
            new BackupCatalogStore(fixture.Db));

        var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
            () => resolver.ResolveProductionRestorePlanPayloadAsync(
                fixture.CatalogEntryId,
                CancellationToken.None));

        Assert.Equal(
            "backup_catalog_production_restore_payload_incomplete",
            ex.ErrorCode);
        Assert.Contains("matrix/signing.key", ex.Message, StringComparison.Ordinal);
    }

    private sealed class CatalogProductionPlanPayloadFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly string _payloadRoot;

        private CatalogProductionPlanPayloadFixture(
            MemDbContext db,
            string databasePath,
            string payloadRoot,
            string catalogEntryId)
        {
            Db = db;
            _databasePath = databasePath;
            _payloadRoot = payloadRoot;
            CatalogEntryId = catalogEntryId;
        }

        public MemDbContext Db { get; }
        public string CatalogEntryId { get; }

        public static async Task<CatalogProductionPlanPayloadFixture> CreateAsync(
            bool includeSigningKey,
            int warningCount)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-production-plan-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-production-plan-payload-{Guid.NewGuid():N}");
            var catalogEntryId = $"bkp_production_plan_{Guid.NewGuid():N}";

            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix", "media_store"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "element"));

            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "database", "synapse.sql"),
                "-- test dump");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "matrix", "homeserver.yaml"),
                "server_name: matrix.demo.test");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "element", "config.json"),
                "{}");

            if (includeSigningKey)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(payloadRoot, "matrix", "signing.key"),
                    "ed25519 test");
            }

            var manifestJson = JsonSerializer.Serialize(new
            {
                manifestVersion = 2,
                exportKind = "mem-stack-export",
                createdAtUtc = DateTimeOffset.UtcNow,
                createdBy = "test",
                memVersion = "0.1.1-dev",
                stack = new
                {
                    stackId = (string?)null,
                    slug = "demo-stack",
                    displayName = "Demo stack",
                    matrixServerName = "matrix.demo.test",
                    matrixPublicUrl = "https://matrix.demo.test",
                    elementPublicUrl = "https://chat.demo.test"
                },
                database = new
                {
                    engine = "postgres",
                    dumpFile = "database/synapse.sql",
                    databaseName = "synapse",
                    username = "synapse",
                    present = true
                },
                matrix = new
                {
                    homeserverConfig = "matrix/homeserver.yaml",
                    signingKey = "matrix/signing.key",
                    mediaStore = "matrix/media_store",
                    mediaBytes = 0,
                    mediaFiles = 0,
                    present = true
                },
                element = new
                {
                    config = "element/config.json",
                    present = true
                },
                routes = new
                {
                    matrixHost = "matrix.demo.test",
                    elementHost = "chat.demo.test",
                    requiresDns = true
                },
                coturn = new
                {
                    configured = false,
                    publicHost = (string?)null,
                    realm = (string?)null,
                    turnUris = Array.Empty<string>(),
                    sharedSecretPresent = false,
                    userLifetime = (string?)null,
                    allowGuests = (bool?)null
                },
                restorePolicy = new
                {
                    canRestoreToFreshMemServer = true,
                    requiresPostgres = true,
                    requiresDomainMapping = true,
                    requiresSigningKey = true,
                    requiresOldServerStoppedForSameServerName = true
                },
                integrity = new
                {
                    checksumsFile = "checksums.sha256"
                },
                includedFiles = Array.Empty<string>(),
                warnings = new[] { "Validation warning retained for review." }
            });

            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "mem-export-manifest.json"),
                manifestJson);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = catalogEntryId,
                OriginKind = BackupCatalogOriginKinds.ImportedZip,
                DisplayName = "Imported production-plan test backup",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = payloadRoot,
                IntegrityStatus = warningCount > 0
                    ? BackupCatalogIntegrityStatuses.Warning
                    : BackupCatalogIntegrityStatuses.Valid,
                WarningCount = warningCount,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return new CatalogProductionPlanPayloadFixture(
                db,
                databasePath,
                payloadRoot,
                catalogEntryId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            if (Directory.Exists(_payloadRoot))
            {
                Directory.Delete(_payloadRoot, recursive: true);
            }

            foreach (var path in new[]
                     {
                         _databasePath,
                         _databasePath + "-shm",
                         _databasePath + "-wal"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
