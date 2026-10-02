using System.Text.Json;
using HostAgent.Platform;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Stacks.Turn;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Workspace;

public sealed class RestoreWorkspaceCatalogStandardRecreatePreflightPersistenceTests
{
    [Fact]
    public async Task Canonical_preflight_uses_local_catalog_homeserver_identity_without_validation_contract()
    {
        await using var fixture = await CatalogPreflightFixture.CreateAsync();

        var response = await fixture.CanonicalPreflight.AssessAsync(
            fixture.RestoreSessionId,
            new StandardRecreatePreflightRequest(
                TargetStackSlug: "catalog-restored",
                RequestedDomainId: null,
                MatrixHost: null,
                ElementHost: "chat-catalog-restored.test"),
            CancellationToken.None)
            ?? throw new InvalidOperationException("Canonical preflight did not return the catalog restore attempt.");

        Assert.DoesNotContain(
            "ValidationId",
            JsonSerializer.Serialize(response),
            StringComparison.Ordinal);
        Assert.Equal(fixture.RestoreSessionId, response.RestoreSessionId);
        Assert.Equal("matrix.catalog.test", response.Targets.MatrixHost);
        Assert.Equal("chat-catalog-restored.test", response.Targets.ElementHost);

        var identityCheck = response.Checks.Single(
            check => check.Code == "matrix-server-identity-preserved");
        Assert.Equal("passed", identityCheck.State);

        Assert.DoesNotContain(
            response.Checks,
            check => check.Code == "matrix-server-identity-available" &&
                     check.State == "blocked");

        Assert.NotNull(response.Turn);
        Assert.Equal(StandardRecreateTurnModes.RestoreDisconnected, response.Turn.Mode);
        Assert.Equal("not-connected", response.Turn.State);
        Assert.False(response.Turn.PlatformTurnRequired);
    }

    [Fact]
    public async Task Connected_mem_managed_backup_blocks_when_destination_platform_turn_is_not_ready()
    {
        await using var fixture = await CatalogPreflightFixture.CreateAsync(
            backupCoturn: ConnectedMemManagedBackupTurn());

        var response = await fixture.CanonicalPreflight.AssessAsync(
            fixture.RestoreSessionId,
            Request(),
            CancellationToken.None)
            ?? throw new InvalidOperationException("Canonical preflight did not return the catalog restore attempt.");

        Assert.False(response.CanCreate);
        Assert.Equal("blocked", response.Status);
        Assert.NotNull(response.Turn);
        Assert.Equal(StandardRecreateTurnModes.RebindPlatform, response.Turn!.Mode);
        Assert.True(response.Turn.PlatformTurnRequired);
        Assert.False(response.Turn.PlatformTurnReady);
        Assert.Null(response.Turn.PublicHost);
        Assert.Empty(response.Turn.TurnUris);

        var turnCheck = response.Checks.Single(check => check.Code == "backup-turn-restore-policy");
        Assert.Equal("blocked", turnCheck.State);
    }

    [Fact]
    public async Task Connected_mem_managed_backup_preflight_uses_current_destination_platform_turn()
    {
        var destination = DestinationPlatformTurn();
        await using var fixture = await CatalogPreflightFixture.CreateAsync(
            backupCoturn: ConnectedMemManagedBackupTurn(),
            platformTurn: destination);

        var response = await fixture.CanonicalPreflight.AssessAsync(
            fixture.RestoreSessionId,
            Request(),
            CancellationToken.None)
            ?? throw new InvalidOperationException("Canonical preflight did not return the catalog restore attempt.");

        Assert.True(response.CanCreate);
        Assert.Equal("ready", response.Status);
        Assert.NotNull(response.Turn);
        Assert.Equal(StandardRecreateTurnModes.RebindPlatform, response.Turn!.Mode);
        Assert.True(response.Turn.PlatformTurnRequired);
        Assert.True(response.Turn.PlatformTurnReady);
        Assert.Equal(destination.PublicHost, response.Turn.PublicHost);
        Assert.Equal(destination.TurnUris, response.Turn.TurnUris);
        Assert.DoesNotContain("turn.source-old.test", response.Turn.TurnUris);
    }

    [Fact]
    public async Task External_backup_remains_preserved_when_destination_platform_turn_is_unavailable()
    {
        await using var fixture = await CatalogPreflightFixture.CreateAsync(
            backupCoturn: ExternalBackupTurn());

        var response = await fixture.CanonicalPreflight.AssessAsync(
            fixture.RestoreSessionId,
            Request(),
            CancellationToken.None)
            ?? throw new InvalidOperationException("Canonical preflight did not return the catalog restore attempt.");

        Assert.True(response.CanCreate);
        Assert.NotNull(response.Turn);
        Assert.Equal(StandardRecreateTurnModes.PreserveBackup, response.Turn!.Mode);
        Assert.False(response.Turn.PlatformTurnRequired);
        Assert.Null(response.Turn.PlatformTurnReady);
        Assert.Equal("turn.external-source.test", response.Turn.PublicHost);
        Assert.Contains("turn:turn.external-source.test:3478?transport=udp", response.Turn.TurnUris);
    }

    [Fact]
    public async Task Canonical_preflight_blocks_with_matrix_identity_reason_when_local_catalog_payload_has_no_homeserver_identity()
    {
        await using var fixture = await CatalogPreflightFixture.CreateAsync(
            includeHomeserverIdentity: false);

        var response = await fixture.CanonicalPreflight.AssessAsync(
            fixture.RestoreSessionId,
            new StandardRecreatePreflightRequest(
                TargetStackSlug: "catalog-restored",
                RequestedDomainId: null,
                MatrixHost: null,
                ElementHost: "chat-catalog-restored.test"),
            CancellationToken.None)
            ?? throw new InvalidOperationException("Canonical preflight did not return the catalog restore attempt.");

        var identityCheck = response.Checks.Single(
            check => check.Code == "matrix-server-identity-available");

        Assert.Equal("blocked", identityCheck.State);
        Assert.DoesNotContain(
            "ValidationId",
            JsonSerializer.Serialize(response),
            StringComparison.Ordinal);
        Assert.False(response.CanCreate);
    }

    private static StandardRecreatePreflightRequest Request() =>
        new(
            TargetStackSlug: "catalog-restored",
            RequestedDomainId: null,
            MatrixHost: null,
            ElementHost: "chat-catalog-restored.test");

    private static LocalBackupCoturnManifest ConnectedMemManagedBackupTurn() =>
        new(
            Configured: true,
            PublicHost: "turn.source-old.test",
            Realm: "source-old.test",
            TurnUris:
            [
                "turn:turn.source-old.test:3478?transport=udp",
                "turn:turn.source-old.test:3478?transport=tcp"
            ],
            SharedSecretPresent: true,
            UserLifetime: "1h",
            AllowGuests: true,
            State: "connected",
            Management: RuntimeStackTurnManagementKinds.MemManaged,
            ConfigurationSource: "platform-coturn",
            ConfigurationSha256: "sha256:source");

    private static LocalBackupCoturnManifest ExternalBackupTurn() =>
        new(
            Configured: true,
            PublicHost: "turn.external-source.test",
            Realm: "external-source.test",
            TurnUris:
            [
                "turn:turn.external-source.test:3478?transport=udp",
                "turn:turn.external-source.test:3478?transport=tcp"
            ],
            SharedSecretPresent: true,
            UserLifetime: "2h",
            AllowGuests: false,
            State: "external",
            Management: RuntimeStackTurnManagementKinds.ExternalObserved,
            ConfigurationSource: "legacy-external",
            ConfigurationSha256: "sha256:external");

    private static CoturnSynapseConfig DestinationPlatformTurn() =>
        new(
            PublicHost: "turn.destination.test",
            Realm: "destination.test",
            TurnUris:
            [
                "turn:turn.destination.test:3478?transport=udp",
                "turn:turn.destination.test:3478?transport=tcp"
            ],
            SharedSecret: "destination-secret",
            UserLifetime: "1h",
            AllowGuests: true,
            RelayPortsPublished: true,
            ExpectedBaseDomain: "destination.test");

    private sealed class CatalogPreflightFixture : IAsyncDisposable
    {
        private CatalogPreflightFixture(
            string dataRoot,
            string databasePath,
            string payloadRoot,
            MemDbContext db,
            string restoreSessionId,
            StandardRecreatePreflightService canonicalPreflight)
        {
            _dataRoot = dataRoot;
            _databasePath = databasePath;
            _payloadRoot = payloadRoot;
            Db = db;
            RestoreSessionId = restoreSessionId;
            CanonicalPreflight = canonicalPreflight;
        }

        private readonly string _dataRoot;
        private readonly string _databasePath;
        private readonly string _payloadRoot;

        public MemDbContext Db { get; }
        public string RestoreSessionId { get; }
        public StandardRecreatePreflightService CanonicalPreflight { get; }

        public static async Task<CatalogPreflightFixture> CreateAsync(
            bool includeHomeserverIdentity = true,
            LocalBackupCoturnManifest? backupCoturn = null,
            CoturnSynapseConfig? platformTurn = null)
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-canonical-preflight-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-canonical-preflight-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

            var matrixDirectory = Path.Combine(payloadRoot, "matrix");
            Directory.CreateDirectory(matrixDirectory);

            if (includeHomeserverIdentity)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(matrixDirectory, "homeserver.yaml"),
                    "server_name: matrix.catalog.test");
            }

            if (backupCoturn is not null)
            {
                var localManifest = new LocalBackupManifest(
                    BackupVersion: "0.2.0",
                    BackupId: "20260629-catalog-source",
                    CreatedAtUtc: DateTime.UtcNow,
                    RuntimeStackId: Guid.NewGuid(),
                    StackSlug: "catalog-source",
                    Database: new LocalBackupDatabaseManifest(
                        "postgres",
                        "mem-postgres",
                        5432,
                        "matrix_catalog_source",
                        "mxu_catalog_source",
                        "matrix_postgres_password",
                        "database/synapse.sql"),
                    Matrix: new LocalBackupMatrixManifest(
                        "matrix",
                        "matrix/homeserver.yaml",
                        null,
                        null,
                        "matrix"),
                    Element: null,
                    Stats: new LocalBackupStats(
                        new LocalBackupFileStats(false, null, 0),
                        new LocalBackupFileStats(includeHomeserverIdentity, "matrix/homeserver.yaml", 1),
                        new LocalBackupFileStats(false, null, 0),
                        new LocalBackupDirectoryStats(false, null, 0, 0),
                        new LocalBackupFileStats(false, null, 0),
                        1,
                        1),
                    Warnings: [])
                {
                    Routes = new LocalBackupRouteManifest(
                        "matrix.catalog.test",
                        "https://matrix.catalog.test",
                        "chat.catalog.test",
                        "https://chat.catalog.test",
                        true),
                    Coturn = backupCoturn
                };

                await File.WriteAllTextAsync(
                    Path.Combine(payloadRoot, "backup-manifest.json"),
                    JsonSerializer.Serialize(localManifest, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            db.Domains.Add(new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = "destination.test",
                DisplayName = "Destination test platform",
                Purpose = "platform",
                IsMainPlatformDomain = true,
                DnsProvider = "test",
                DnsZone = "destination.test",
                Status = "Active",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                UpdatedAtUtc = DateTime.UtcNow
            });

            var catalogEntry = new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = "bkp_canonical_preflight",
                OriginKind = BackupCatalogOriginKinds.LocalCaptured,
                DisplayName = "Local catalog preflight backup",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.LocalBackupDirectory,
                PayloadDirectoryPath = payloadRoot,
                SourceStackSlug = "catalog-source",
                SourceBackupId = "20260629-catalog-source",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Payload is available.",
                WarningCount = 0,
                PayloadBytes = 123,
                ElementHost = "chat.catalog.test",
                CreatedAtUtc = DateTime.UtcNow,
                CapturedAtUtc = DateTime.UtcNow
            };

            db.BackupCatalogEntries.Add(catalogEntry);
            await db.SaveChangesAsync();

            var workspaceStore = new RestoreAttemptWorkspaceStore(configuration);
            var operationStore = new RuntimeOperationStore(db);
            var logs = new RestoreStructuredLogService(
                db,
                workspaceStore,
                NullLogger<RestoreStructuredLogService>.Instance);
            var coordinator = new RestoreAttemptCoordinator(
                db,
                workspaceStore,
                operationStore,
                logs,
                NullLogger<RestoreAttemptCoordinator>.Instance);
            var catalogStore = new BackupCatalogStore(db);
            var payloadResolver = new BackupCatalogPayloadResolver(catalogStore);
            var catalogSessions = new CatalogRestoreSessionService(
                catalogStore,
                coordinator);
            var manifestStore = new RuntimeStackManifestStore(configuration, db);
            var domainResolver = new PlatformDomainResolver(db);
            var catalogPreflight = new CatalogStandardRecreatePreflightService(
                payloadResolver,
                catalogSessions,
                coordinator,
                manifestStore,
                domainResolver,
                new FakePlatformTurnConfiguration(platformTurn));
            var canonicalPreflight = new StandardRecreatePreflightService(
                coordinator,
                catalogStore,
                catalogPreflight);

            var session = await catalogSessions.PrepareAsync(
                catalogEntry.CatalogEntryId,
                CancellationToken.None);

            return new CatalogPreflightFixture(
                dataRoot,
                databasePath,
                payloadRoot,
                db,
                session.RestoreSessionId,
                canonicalPreflight);
        }

        private sealed class FakePlatformTurnConfiguration
            : IRuntimeStackTurnPlatformConfigurationProvider
        {
            private readonly CoturnSynapseConfig? _configuration;

            public FakePlatformTurnConfiguration(CoturnSynapseConfig? configuration)
            {
                _configuration = configuration;
            }

            public Task<CoturnSynapseConfig?> GetSynapseConfigAsync(CancellationToken ct) =>
                Task.FromResult(_configuration);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
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
