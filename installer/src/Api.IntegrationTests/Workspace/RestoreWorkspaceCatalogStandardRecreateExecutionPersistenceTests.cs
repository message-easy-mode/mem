using System.Security.Cryptography;
using System.Text.Json;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Workspace;

public sealed class RestoreWorkspaceCatalogStandardRecreateExecutionPersistenceTests
{
    private const string StackLogoBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAZUlEQVR42u3QQREAAAQAMDGEFlITcjh7rMAiq+ezECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAu5boJcyWUwYGNkAAAAASUVORK5CYII=";

    [Fact]
    public async Task Catalog_execution_material_uses_local_payload_paths_and_has_no_validation_contract()
    {
        await using var fixture = await Fixture.CreateAsync();

        var material = await fixture.PayloadResolver
            .ResolveStandardRecreateExecutionMaterialAsync(
                fixture.CatalogEntryId,
                CancellationToken.None);

        Assert.Equal(fixture.CatalogEntryId, material.CatalogEntryId);
        Assert.Equal(fixture.CatalogEntryDatabaseId, material.CatalogEntryDatabaseId);
        Assert.DoesNotContain(
            "ValidationId",
            JsonSerializer.Serialize(material),
            StringComparison.Ordinal);
        Assert.Equal("catalog-source", material.SourceStackSlug);
        Assert.Equal("matrix.catalog.test", material.MatrixServerName);
        Assert.Equal("chat.catalog.test", material.SourceElementHost);
        Assert.True(File.Exists(material.DatabaseDumpPath));
        Assert.True(File.Exists(material.HomeserverPath));
        Assert.True(File.Exists(material.SigningKeyPath));
        Assert.NotNull(material.StackLogo);
        Assert.Equal(fixture.LogoSha256, material.StackLogo!.Sha256);
        Assert.Equal(64, material.StackLogo.Width);
        Assert.Equal(64, material.StackLogo.Height);
        Assert.True(File.Exists(material.StackLogo.Path));
        Assert.EndsWith(
            Path.Combine("identity", "logo.png"),
            material.StackLogo.Path,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("matrix", "matrix.catalog.test.signing.key"),
            material.SigningKeyPath,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canonical_workspace_dispatches_catalog_execution_by_catalog_identity_not_validation_id()
    {
        await using var fixture = await Fixture.CreateAsync();

        var executor = new RecordingExecutor();
        var dispatcher = new RestoreWorkspaceStandardRecreateService(
            fixture.Coordinator,
            fixture.CatalogStore,
            executor);

        var result = await dispatcher.ExecuteAsync(
            fixture.RestoreSessionId,
            CreateRequest(),
            CancellationToken.None)
            ?? throw new InvalidOperationException(
                "Canonical execution dispatcher did not find the restore attempt.");

        Assert.Equal("backup-catalog", result.SourceKind);
        Assert.Equal(fixture.CatalogEntryId, result.CatalogEntryId);
        Assert.DoesNotContain(
            "ValidationId",
            JsonSerializer.Serialize(result),
            StringComparison.Ordinal);

        Assert.Equal(1, executor.CatalogCalls);
        Assert.Equal(fixture.CatalogEntryId, executor.CatalogEntryId);
        Assert.Equal(fixture.RestoreSessionId, executor.RestoreSessionId);
    }


    [Fact]
    public async Task Catalog_execution_reserves_targets_and_starts_one_source_aware_operation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var reservation = await fixture.Coordinator
            .ReserveTargetsAndStartStandardRecreateForCatalogAsync(
                fixture.RestoreSessionId,
                fixture.CatalogEntryDatabaseId,
                targetStackSlug: "catalog-restored",
                matrixHost: "matrix.catalog.test",
                elementHost: "chat-catalog-restored.test",
                recreateId: "catalog-recreate-001",
                requestedBy: "Nigel",
                note: "catalog test",
                ct: CancellationToken.None);

        Assert.Equal(fixture.RestoreSessionId, reservation.Attempt.RestoreSessionId);
        Assert.Equal("backup-catalog", reservation.Attempt.SourceKind);
        Assert.Equal(fixture.CatalogEntryDatabaseId, reservation.Attempt.BackupCatalogEntryId);

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            x => x.Id == reservation.Attempt.Id);
        Assert.Equal(RestoreAttemptStatuses.Recreating, attempt.Status);
        Assert.Equal(
            RestoreAttemptStages.CreateRestoredChatServer,
            attempt.CurrentStage);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync(
            x => x.Id == reservation.RuntimeOperationId);
        Assert.Equal("restore.standard-recreate", operation.Operation);
        Assert.Contains("\"sourceKind\": \"backup-catalog\"", operation.InputJson);
        Assert.Contains(
            $"\"backupCatalogEntryId\": \"{fixture.CatalogEntryDatabaseId}\"",
            operation.InputJson);
        Assert.DoesNotContain("\"validationId\":", operation.InputJson);

        var activeClaims = await fixture.Db.RestoreTargetClaims
            .Where(x => x.RestoreAttemptId == reservation.Attempt.Id &&
                        x.ActiveClaimKey != null)
            .ToListAsync();

        Assert.Equal(3, activeClaims.Count);
        Assert.Contains(activeClaims, x =>
            x.ResourceType == RestoreTargetResourceTypes.StackSlug &&
            x.ResourceValue == "catalog-restored");
        Assert.Contains(activeClaims, x =>
            x.ResourceType == RestoreTargetResourceTypes.MatrixHost &&
            x.ResourceValue == "matrix.catalog.test");
        Assert.Contains(activeClaims, x =>
            x.ResourceType == RestoreTargetResourceTypes.ElementHost &&
            x.ResourceValue == "chat-catalog-restored.test");
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_05_standard_recreate_progress_updates_operation_step_and_restore_activity()
    {
        await using var fixture = await Fixture.CreateAsync();

        var reservation = await fixture.Coordinator
            .ReserveTargetsAndStartStandardRecreateForCatalogAsync(
                fixture.RestoreSessionId,
                fixture.CatalogEntryDatabaseId,
                targetStackSlug: "catalog-restored-progress",
                matrixHost: "matrix.catalog.test",
                elementHost: "chat-catalog-restored-progress.test",
                recreateId: "catalog-recreate-progress",
                requestedBy: "Nigel",
                note: null,
                ct: CancellationToken.None);

        await fixture.Coordinator.RecordStandardRecreateProgressAsync(
            reservation.Attempt.Id,
            reservation.RuntimeOperationId,
            "restore-database",
            "Restoring the Synapse database.",
            CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync(
            x => x.Id == reservation.RuntimeOperationId);
        Assert.Equal("restore-database", operation.CurrentStep);

        var logPage = await fixture.Logs.ListAsync(
            fixture.RestoreSessionId,
            new RestoreLogQuery(1, 50, null, null, null),
            CancellationToken.None);

        Assert.NotNull(logPage);
        var progress = Assert.Single(
            logPage!.Events,
            item => item.EventCode ==
                "restore.standard-recreate.progress.restore-database");
        Assert.Equal(reservation.RuntimeOperationId, progress.OperationId);
        Assert.Equal("Restoring the Synapse database.", progress.Message);
    }

    [Fact]
    public async Task Catalog_execution_completion_persists_source_aware_operation_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();

        var reservation = await fixture.Coordinator
            .ReserveTargetsAndStartStandardRecreateForCatalogAsync(
                fixture.RestoreSessionId,
                fixture.CatalogEntryDatabaseId,
                targetStackSlug: "catalog-restored-evidence",
                matrixHost: "matrix.catalog.test",
                elementHost: "chat-catalog-restored-evidence.test",
                recreateId: "catalog-recreate-evidence",
                requestedBy: "Nigel",
                note: null,
                ct: CancellationToken.None);

        var runtimeStackId = Guid.NewGuid();
        fixture.Db.RuntimeStacks.Add(new RuntimeStackEntity
        {
            Id = runtimeStackId,
            Slug = "catalog-restored-evidence-runtime",
            Status = "public_routes_verified",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MatrixInstanceId = Guid.NewGuid()
        });
        await fixture.Db.SaveChangesAsync();

        var result = CreateResult(
            catalogEntryId: fixture.CatalogEntryId,
            recreateId: "catalog-recreate-evidence",
            runtimeStackId: runtimeStackId,
            targetStackSlug: "catalog-restored-evidence",
            elementHost: "chat-catalog-restored-evidence.test");

        await fixture.Coordinator.CompleteStandardRecreateAsync(
            reservation.Attempt.Id,
            reservation.RuntimeOperationId,
            runtimeStackId,
            publicReadinessPassed: true,
            result: result,
            ct: CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync(
            x => x.Id == reservation.RuntimeOperationId);

        using var evidence = JsonDocument.Parse(operation.EvidenceJson!);
        var root = evidence.RootElement;
        Assert.Equal("backup-catalog", root.GetProperty("sourceKind").GetString());
        Assert.Equal(fixture.CatalogEntryId, root.GetProperty("catalogEntryId").GetString());
        Assert.False(root.TryGetProperty("validationId", out _));
        Assert.Equal("catalog-restored-evidence", root.GetProperty("targetStackSlug").GetString());
        Assert.Equal("matrix.catalog.test", root.GetProperty("matrixHost").GetString());
        Assert.Equal("chat-catalog-restored-evidence.test", root.GetProperty("elementHost").GetString());
        Assert.True(root.GetProperty("databaseImportSucceeded").GetBoolean());
        Assert.True(root.GetProperty("publicReadinessPassed").GetBoolean());
        Assert.True(root.GetProperty("targetClaimsReleased").GetBoolean());
    }

    [Fact]
    public async Task RestoreWorkspaceUserInventory_evidence_persists_safe_summary_and_releases_claims()
    {
        await using var fixture = await Fixture.CreateAsync();

        var reservation = await fixture.Coordinator
            .ReserveTargetsAndStartStandardRecreateForCatalogAsync(
                fixture.RestoreSessionId,
                fixture.CatalogEntryDatabaseId,
                targetStackSlug: "catalog-restored-users",
                matrixHost: "matrix.catalog.test",
                elementHost: "chat-catalog-restored-users.test",
                recreateId: "catalog-recreate-users",
                requestedBy: "Nigel",
                note: null,
                ct: CancellationToken.None);

        var runtimeStackId = Guid.NewGuid();
        fixture.Db.RuntimeStacks.Add(new RuntimeStackEntity
        {
            Id = runtimeStackId,
            Slug = "catalog-restored-users-runtime",
            Status = "public_routes_verified",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MatrixInstanceId = Guid.NewGuid()
        });
        await fixture.Db.SaveChangesAsync();

        var synchronizedAtUtc = new DateTimeOffset(2026, 7, 10, 2, 3, 4, TimeSpan.Zero);
        var result = CreateResult(
            catalogEntryId: fixture.CatalogEntryId,
            recreateId: "catalog-recreate-users",
            runtimeStackId: runtimeStackId,
            targetStackSlug: "catalog-restored-users",
            elementHost: "chat-catalog-restored-users.test",
            userInventory: new StandardRecreateUserInventorySummary(
                Status: "synchronized",
                Source: "synapse-postgres",
                UserCount: 2,
                ActiveAdminCount: 1,
                SynchronizedAtUtc: synchronizedAtUtc,
                ErrorCode: null));

        await fixture.Coordinator.CompleteStandardRecreateAsync(
            reservation.Attempt.Id,
            reservation.RuntimeOperationId,
            runtimeStackId,
            publicReadinessPassed: true,
            result: result,
            ct: CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync(
            x => x.Id == reservation.RuntimeOperationId);

        Assert.DoesNotContain("username", operation.EvidenceJson!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matrixUserId", operation.EvidenceJson!, StringComparison.OrdinalIgnoreCase);

        using var evidence = JsonDocument.Parse(operation.EvidenceJson!);
        var root = evidence.RootElement;
        Assert.Equal("synchronized", root.GetProperty("matrixUserInventoryStatus").GetString());
        Assert.Equal("synapse-postgres", root.GetProperty("matrixUserInventorySource").GetString());
        Assert.Equal(2, root.GetProperty("matrixUserInventoryUserCount").GetInt32());
        Assert.Equal(1, root.GetProperty("matrixUserInventoryActiveAdminCount").GetInt32());
        Assert.Equal(
            synchronizedAtUtc,
            root.GetProperty("matrixUserInventorySynchronizedAtUtc").GetDateTimeOffset());
        Assert.False(root.TryGetProperty("matrixUserInventoryErrorCode", out _));
        Assert.True(root.GetProperty("targetClaimsReleased").GetBoolean());

        var activeClaims = await fixture.Db.RestoreTargetClaims
            .CountAsync(x => x.RestoreAttemptId == reservation.Attempt.Id && x.ActiveClaimKey != null);
        Assert.Equal(0, activeClaims);
    }

    [Fact]
    public async Task Catalog_execution_rejects_a_different_catalog_entry_for_the_same_workspace()
    {
        await using var fixture = await Fixture.CreateAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Coordinator
                .ReserveTargetsAndStartStandardRecreateForCatalogAsync(
                    fixture.RestoreSessionId,
                    Guid.NewGuid(),
                    targetStackSlug: "catalog-restored",
                    matrixHost: "matrix.catalog.test",
                    elementHost: "chat-catalog-restored.test",
                    recreateId: "catalog-recreate-mismatch",
                    requestedBy: "Nigel",
                    note: null,
                    ct: CancellationToken.None));

        Assert.Contains(
            "not linked to the requested Backup Catalog entry",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static StandardRecreateRequest CreateRequest() =>
        new(
            TargetStackSlug: "catalog-restored",
            RequestedDomainId: null,
            MatrixHost: "matrix.catalog.test",
            ElementHost: "chat-catalog-restored.test",
            MatrixImage: null,
            ElementImage: null,
            Operator: "Nigel",
            Note: "catalog execution test",
            ExecuteProductionRecreate: true,
            AcknowledgeCreatesRealStack: true,
            AcknowledgeMutatesProductionPostgres: true,
            AcknowledgeMutatesNpmRoutes: true,
            AcknowledgeNoAutomaticRollback: true);

    private static StandardRecreateResult CreateResult(
        string catalogEntryId,
        string recreateId = "test-recreate",
        Guid? runtimeStackId = null,
        string targetStackSlug = "catalog-restored",
        string elementHost = "chat-catalog-restored.test",
        StandardRecreateUserInventorySummary? userInventory = null) =>
        new(
            Source: "control-plane",
            Status: "production_recreate_verified",
            RecreateId: recreateId,
            RuntimeStackId: runtimeStackId ?? Guid.NewGuid(),
            MatrixInstanceId: Guid.NewGuid(),
            ElementInstanceId: Guid.NewGuid(),
            TargetStackSlug: targetStackSlug,
            MatrixHost: "matrix.catalog.test",
            ElementHost: elementHost,
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            Operator: null,
            Note: null,
            Database: new StandardRecreateDatabaseSummary(
                Provisioned: true,
                Host: "mem-postgres",
                Port: 5432,
                DatabaseName: "synapse_catalog_restored",
                DatabaseUsername: "mem_restore",
                ImportSucceeded: true,
                PublicTableCount: 1,
                SynapseKnownTableCount: 1,
                UsersCount: 0,
                EventsCount: 0,
                RoomsCount: 0,
                StateEventsCount: 0),
            Runtime: new StandardRecreateRuntimeSummary(
                MatrixContainerName: "matrix",
                MatrixContainerId: null,
                MatrixStarted: true,
                MatrixHealthPassed: true,
                MatrixHealthResponse: "OK",
                ElementContainerName: "element",
                ElementContainerId: null,
                ElementStarted: true,
                ElementHealthPassed: true,
                ElementHealthResponse: "OK",
                RuntimeNetworkName: "mem-gateway",
                MatrixDataPath: "/runtime/matrix",
                ElementDataPath: "/runtime/element",
                HomeserverPath: "/runtime/matrix/homeserver.yaml",
                ElementConfigPath: "/runtime/element/config.json",
                ManifestSaved: true,
                DatabaseOwnershipSaved: true,
                StackRegistered: true),
            Routes: new StandardRecreateRouteSummary(
                MatrixPublicHost: "matrix.catalog.test",
                MatrixForwardHost: "matrix",
                MatrixForwardPort: 8008,
                MatrixRouteId: "matrix-route",
                MatrixRouteReady: true,
                ElementPublicHost: elementHost,
                ElementForwardHost: "element",
                ElementForwardPort: 80,
                ElementRouteId: "element-route",
                ElementRouteReady: true,
                PublicReadinessPassed: true),
            Mutations: new StandardRecreateMutationSummary(
                RuntimeStackCreated: true,
                ProductionPostgresMutated: true,
                ProductionContainersTouched: true,
                NpmRoutesChanged: true,
                DnsChanged: false,
                CertificatesChanged: false,
                OldStacksDeleted: false,
                Notes: []),
            Checks: [],
            Warnings: [],
            Errors: [],
            Detail: "test",
            CatalogEntryId: catalogEntryId)
        {
            UserInventory = userInventory
        };

    private sealed class RecordingExecutor : IStandardRecreateExecutor
    {
        public int CatalogCalls { get; private set; }
        public string? CatalogEntryId { get; private set; }
        public string? RestoreSessionId { get; private set; }

        public Task<StandardRecreateResult> ExecuteCatalogAsync(
            string catalogEntryId,
            string restoreSessionId,
            StandardRecreateRequest request,
            CancellationToken ct)
        {
            CatalogCalls++;
            CatalogEntryId = catalogEntryId;
            RestoreSessionId = restoreSessionId;

            return Task.FromResult(CreateResult(
                catalogEntryId: catalogEntryId));
        }

    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            string dataRoot,
            string databasePath,
            MemDbContext db,
            BackupCatalogStore catalogStore,
            BackupCatalogPayloadResolver payloadResolver,
            RestoreAttemptCoordinator coordinator,
            RestoreStructuredLogService logs,
            string catalogEntryId,
            Guid catalogEntryDatabaseId,
            string restoreSessionId,
            string logoSha256)
        {
            _dataRoot = dataRoot;
            _databasePath = databasePath;
            Db = db;
            CatalogStore = catalogStore;
            PayloadResolver = payloadResolver;
            Coordinator = coordinator;
            Logs = logs;
            CatalogEntryId = catalogEntryId;
            CatalogEntryDatabaseId = catalogEntryDatabaseId;
            RestoreSessionId = restoreSessionId;
            LogoSha256 = logoSha256;
        }

        private readonly string _dataRoot;
        private readonly string _databasePath;

        public MemDbContext Db { get; }
        public BackupCatalogStore CatalogStore { get; }
        public BackupCatalogPayloadResolver PayloadResolver { get; }
        public RestoreAttemptCoordinator Coordinator { get; }
        public RestoreStructuredLogService Logs { get; }
        public string CatalogEntryId { get; }
        public Guid CatalogEntryDatabaseId { get; }
        public string RestoreSessionId { get; }
        public string LogoSha256 { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-standard-recreate-execution-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-catalog-standard-recreate-execution-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(dataRoot, "catalog-payload");
            var matrixDirectory = Path.Combine(payloadRoot, "matrix");

            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            Directory.CreateDirectory(matrixDirectory);
            Directory.CreateDirectory(Path.Combine(payloadRoot, "element"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "identity"));

            var logoBytes = Convert.FromBase64String(StackLogoBase64);
            var logoSha256 = Convert.ToHexString(SHA256.HashData(logoBytes)).ToLowerInvariant();
            await File.WriteAllBytesAsync(
                Path.Combine(payloadRoot, "identity", "logo.png"),
                logoBytes);
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "backup-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    backupVersion = "mem-stack-backup-v2",
                    logo = new
                    {
                        file = "identity/logo.png",
                        sha256 = logoSha256,
                        bytes = (long)logoBytes.Length,
                        width = 64,
                        height = 64,
                        present = true
                    }
                }));

            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "database", "synapse.sql"),
                "-- source-aware catalog execution test");
            await File.WriteAllTextAsync(
                Path.Combine(matrixDirectory, "homeserver.yaml"),
                "server_name: matrix.catalog.test");
            await File.WriteAllTextAsync(
                Path.Combine(
                    matrixDirectory,
                    "matrix.catalog.test.signing.key"),
                "ed25519 a_test 0123456789");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "element", "config.json"),
                "{\"default_server_config\":{\"m.homeserver\":{\"base_url\":\"https://matrix.catalog.test\"}}}");

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

            var entry = new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = "bkp_catalog_execution",
                OriginKind = BackupCatalogOriginKinds.LocalCaptured,
                DisplayName = "Catalog execution fixture",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.LocalBackupDirectory,
                PayloadDirectoryPath = payloadRoot,
                SourceStackSlug = "catalog-source",
                SourceBackupId = "catalog-source-backup",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Payload is available.",
                WarningCount = 0,
                PayloadBytes = 456,
                ElementHost = "chat.catalog.test",
                CreatedAtUtc = DateTime.UtcNow,
                CapturedAtUtc = DateTime.UtcNow
            };

            db.BackupCatalogEntries.Add(entry);
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
            var sessions = new CatalogRestoreSessionService(
                catalogStore,
                coordinator);

            var session = await sessions.PrepareAsync(
                entry.CatalogEntryId,
                CancellationToken.None);

            return new Fixture(
                dataRoot,
                databasePath,
                db,
                catalogStore,
                payloadResolver,
                coordinator,
                logs,
                entry.CatalogEntryId,
                entry.Id,
                session.RestoreSessionId,
                logoSha256);
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
