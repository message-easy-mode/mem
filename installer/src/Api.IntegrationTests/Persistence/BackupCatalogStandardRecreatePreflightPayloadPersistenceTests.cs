using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using System.Text.Json;
using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class BackupCatalogStandardRecreatePreflightPayloadPersistenceTests
{
    [Fact]
    public async Task Warning_catalog_entry_with_export_manifest_resolves_for_catalog_preflight()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Warning,
            warningCount: 3,
            includeManifest: true);

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));
        var payload = await resolver.ResolveStandardRecreatePreflightPayloadAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.Equal(fixture.CatalogEntryId, payload.CatalogEntryId);
        Assert.Equal(BackupCatalogIntegrityStatuses.Warning, payload.IntegrityStatus);
        Assert.Equal(3, payload.WarningCount);
        Assert.True(File.Exists(payload.ManifestPath));
        Assert.Equal("demo-stack", payload.Manifest.Stack.Slug);
        Assert.Equal("matrix.demo.test", payload.Manifest.Stack.MatrixServerName);
        Assert.Equal("matrix.demo.test", payload.Manifest.Routes.MatrixHost);
        Assert.True(payload.Manifest.RestorePolicy.RequiresOldServerStoppedForSameServerName);
    }

    [Fact]
    public async Task Missing_manifest_returns_stable_preflight_error()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: false);

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));

        var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
            () => resolver.ResolveStandardRecreatePreflightPayloadAsync(
                fixture.CatalogEntryId,
                CancellationToken.None));

        Assert.Equal("backup_catalog_standard_recreate_manifest_not_found", ex.ErrorCode);
    }

    [Fact]
    public async Task Removed_catalog_payload_cannot_begin_preflight_resolution()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Removed,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: true);

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));

        var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
            () => resolver.ResolveStandardRecreatePreflightPayloadAsync(
                fixture.CatalogEntryId,
                CancellationToken.None));

        Assert.Equal("backup_catalog_payload_not_available", ex.ErrorCode);
    }

    [Fact]
    public async Task Invalid_catalog_integrity_cannot_begin_preflight_resolution()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Invalid,
            warningCount: 0,
            includeManifest: true);

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));

        var ex = await Assert.ThrowsAsync<BackupCatalogPayloadResolutionException>(
            () => resolver.ResolveStandardRecreatePreflightPayloadAsync(
                fixture.CatalogEntryId,
                CancellationToken.None));

        Assert.Equal("backup_catalog_payload_invalid", ex.ErrorCode);
    }

    [Fact]
    public async Task Local_catalog_without_portable_manifest_resolves_standard_recreate_identity_from_homeserver()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: false,
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            homeserverServerName: "matrix.local-capture.test");

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));
        var source = await resolver.ResolveStandardRecreateSourceAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.Equal(fixture.CatalogEntryId, source.CatalogEntryId);
        Assert.Equal(BackupCatalogOriginKinds.LocalCaptured, source.OriginKind);
        Assert.Equal("matrix.local-capture.test", source.MatrixServerName);
        Assert.Equal("chat.demo.test", source.SourceElementHost);
        Assert.True(source.RequiresOldServerStoppedForSameServerName);
    }


    [Fact]
    public async Task Local_catalog_projects_disconnected_turn_intent_from_native_backup_manifest()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: false,
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            homeserverServerName: "matrix.local-turn.test");

        var localManifest = new LocalBackupManifest(
            BackupVersion: "mem-stack-backup-v2",
            BackupId: "20260726-local-turn",
            CreatedAtUtc: DateTime.UtcNow,
            RuntimeStackId: Guid.NewGuid(),
            StackSlug: "local-turn",
            Database: new LocalBackupDatabaseManifest(
                "postgres",
                "mem-postgres",
                5432,
                "matrix_local_turn",
                "mxu_local_turn",
                "matrix_postgres_password",
                "database/synapse.sql"),
            Matrix: new LocalBackupMatrixManifest(
                "matrix",
                "matrix/homeserver.yaml",
                "matrix/signing.key",
                "matrix/media_store",
                "matrix"),
            Element: null,
            Stats: new LocalBackupStats(
                new LocalBackupFileStats(true, "database/synapse.sql", 1),
                new LocalBackupFileStats(true, "matrix/homeserver.yaml", 1),
                new LocalBackupFileStats(true, "matrix/signing.key", 1),
                new LocalBackupDirectoryStats(false, null, 0, 0),
                new LocalBackupFileStats(false, null, 0),
                3,
                3),
            Warnings: [])
        {
            Routes = new LocalBackupRouteManifest(
                "matrix.local-turn.test",
                "https://matrix.local-turn.test",
                "chat.local-turn.test",
                "https://chat.local-turn.test",
                true),
            Coturn = new LocalBackupCoturnManifest(
                Configured: false,
                PublicHost: null,
                Realm: null,
                TurnUris: [],
                SharedSecretPresent: false,
                UserLifetime: null,
                AllowGuests: null,
                State: "not-connected",
                Management: "none",
                ConfigurationSource: "backup-disconnected",
                ConfigurationSha256: "sha256:test")
        };

        await File.WriteAllTextAsync(
            Path.Combine(fixture.PayloadRoot, "backup-manifest.json"),
            JsonSerializer.Serialize(localManifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));
        var source = await resolver.ResolveStandardRecreateSourceAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.False(source.DeclaresCoturnConfigured);
        Assert.NotNull(source.BackupCoturn);
        Assert.Equal("not-connected", source.BackupCoturn.State);
        Assert.Equal("none", source.BackupCoturn.Management);
        Assert.Empty(source.BackupCoturn.TurnUris);
    }

    [Fact]
    public async Task Imported_catalog_prefers_portable_manifest_identity_over_homeserver_fallback()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: true,
            originKind: BackupCatalogOriginKinds.ImportedZip,
            homeserverServerName: "matrix.homeserver-fallback.test");

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));
        var source = await resolver.ResolveStandardRecreateSourceAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.Equal("matrix.demo.test", source.MatrixServerName);
        Assert.Equal("chat.demo.test", source.SourceElementHost);
        Assert.True(source.RequiresOldServerStoppedForSameServerName);
    }

    [Fact]
    public async Task Imported_catalog_without_portable_manifest_falls_back_to_homeserver_identity()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: false,
            originKind: BackupCatalogOriginKinds.ImportedZip,
            homeserverServerName: "matrix.imported-fallback.test");

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));
        var source = await resolver.ResolveStandardRecreateSourceAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.Equal("matrix.imported-fallback.test", source.MatrixServerName);
    }

    [Fact]
    public async Task Local_catalog_uses_homeserver_identity_even_when_a_portable_manifest_is_present()
    {
        await using var fixture = await CatalogPayloadFixture.CreateAsync(
            payloadState: BackupCatalogPayloadStates.Available,
            integrityStatus: BackupCatalogIntegrityStatuses.Valid,
            warningCount: 0,
            includeManifest: true,
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            homeserverServerName: "matrix.local-authoritative.test");

        var resolver = new BackupCatalogPayloadResolver(new BackupCatalogStore(fixture.Db));
        var source = await resolver.ResolveStandardRecreateSourceAsync(
            fixture.CatalogEntryId,
            CancellationToken.None);

        Assert.Equal("matrix.local-authoritative.test", source.MatrixServerName);
    }

    private sealed class CatalogPayloadFixture : IAsyncDisposable
    {
        private CatalogPayloadFixture(
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

        private readonly string _databasePath;
        private readonly string _payloadRoot;

        public MemDbContext Db { get; }
        public string CatalogEntryId { get; }
        public string PayloadRoot => _payloadRoot;

        public static async Task<CatalogPayloadFixture> CreateAsync(
            string payloadState,
            string integrityStatus,
            int warningCount,
            bool includeManifest,
            string originKind = BackupCatalogOriginKinds.ImportedZip,
            string? homeserverServerName = null)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-standard-preflight-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-standard-preflight-payload-{Guid.NewGuid():N}");
            var catalogEntryId = $"bkp_preflight_{Guid.NewGuid():N}";

            Directory.CreateDirectory(payloadRoot);

            if (!string.IsNullOrWhiteSpace(homeserverServerName))
            {
                var matrixDirectory = Path.Combine(payloadRoot, "matrix");
                Directory.CreateDirectory(matrixDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(matrixDirectory, "homeserver.yaml"),
                    $"server_name: {homeserverServerName}");
            }

            if (includeManifest)
            {
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
                    warnings = Array.Empty<string>()
                });

                await File.WriteAllTextAsync(
                    Path.Combine(payloadRoot, "mem-export-manifest.json"),
                    manifestJson);
            }

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = catalogEntryId,
                OriginKind = originKind,
                DisplayName = "Catalog test backup",
                MatrixHost = "matrix.demo.test",
                ElementHost = "chat.demo.test",
                PayloadState = payloadState,
                PayloadStorageKind = originKind == BackupCatalogOriginKinds.LocalCaptured
                    ? BackupCatalogPayloadStorageKinds.LocalBackupDirectory
                    : BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = payloadRoot,
                IntegrityStatus = integrityStatus,
                WarningCount = warningCount,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            return new CatalogPayloadFixture(db, databasePath, payloadRoot, catalogEntryId);
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
