using Api.IntegrationTests.Runtime;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Backups.Workspace;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Workspace;

/// <summary>
/// Regression coverage for cancelling a canonical Restore Workspace. The
/// operator goal is to close an unfinished source-aware attempt, release its
/// temporary ownership, retain audit history, and then permit permanent source
/// deletion. No restored runtime stack is deleted by this lifecycle action.
/// </summary>
public sealed class RestoreWorkspaceCancellationPersistenceTests
{
    [Fact]
    public async Task Cancelling_active_catalog_workspace_releases_claims_preserves_audit_and_unblocks_catalog_delete()
    {
        await using var fixture = await CancellationFixture.CreateAsync();

        var beforeCancellation = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(beforeCancellation);
        Assert.NotNull(beforeCancellation!.Cancellation);
        Assert.True(beforeCancellation.Cancellation!.CanCancel);

        fixture.Db.RestoreTargetClaims.Add(new RestoreTargetClaimEntity
        {
            Id = Guid.NewGuid(),
            RestoreAttemptId = fixture.RestoreAttemptId,
            ResourceType = RestoreTargetResourceTypes.StackSlug,
            ResourceValue = "cancelled-target",
            ActiveClaimKey = "stack-slug:cancelled-target",
            ClaimedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var cancelled = await fixture.Coordinator.CancelAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.Equal(RestoreAttemptStatuses.Cancelled, cancelled.Status);
        Assert.Equal(RestoreAttemptStages.Cancelled, cancelled.CurrentStage);
        Assert.NotNull(cancelled.TerminalAtUtc);

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            entry => entry.Id == fixture.RestoreAttemptId);
        Assert.Equal(RestoreAttemptStatuses.Cancelled, attempt.Status);
        Assert.Null(attempt.ActiveSourceKey);
        Assert.NotNull(attempt.TerminalAtUtc);

        var releasedClaim = await fixture.Db.RestoreTargetClaims.SingleAsync(
            claim => claim.RestoreAttemptId == fixture.RestoreAttemptId);
        Assert.Null(releasedClaim.ActiveClaimKey);
        Assert.NotNull(releasedClaim.ReleasedAtUtc);
        Assert.Equal("restore-cancelled", releasedClaim.ReleaseReason);

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal(4, workspace!.SchemaVersion);
        Assert.Equal("cancelled", workspace.OverallStatus.Code);
        Assert.NotNull(workspace.Cancellation);
        Assert.False(workspace.Cancellation!.CanCancel);
        Assert.Equal(
            "This restore has already been cancelled.",
            workspace.Cancellation.ReasonUnavailable);
        Assert.All(
            workspace.StandardStages,
            stage => Assert.Equal(RestoreWorkspaceStageStates.Cancelled, stage.State));

        var logPage = await fixture.Logs.ListAsync(
            fixture.RestoreSessionId,
            new RestoreLogQuery(1, 50, null, null, null),
            CancellationToken.None);
        Assert.NotNull(logPage);
        Assert.Contains(
            logPage!.Events,
            item => item.EventCode == "restore.cancelled");

        var deletion = await fixture.Lifecycle.DeleteAsync(
            fixture.CatalogEntryId,
            "Nigel",
            CancellationToken.None);

        Assert.NotNull(deletion);
        Assert.Equal(1, deletion!.DetachedRestoreAttempts);
        Assert.False(await fixture.Db.BackupCatalogEntries.AnyAsync());

        var retainedAttempt = await fixture.Db.RestoreAttempts.SingleAsync(
            entry => entry.Id == fixture.RestoreAttemptId);
        Assert.Equal(RestoreAttemptStatuses.Cancelled, retainedAttempt.Status);
        Assert.Null(retainedAttempt.ActiveSourceKey);
        Assert.Null(retainedAttempt.BackupCatalogEntryId);
    }

    [Fact]
    public async Task Completed_restore_cannot_be_cancelled()
    {
        await using var fixture = await CancellationFixture.CreateAsync();

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            entry => entry.Id == fixture.RestoreAttemptId);
        attempt.Status = RestoreAttemptStatuses.Completed;
        attempt.CurrentStage = RestoreAttemptStages.PublicVerification;
        attempt.ActiveSourceKey = null;
        attempt.TerminalAtUtc = DateTime.UtcNow;
        attempt.UpdatedAtUtc = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.NotNull(workspace);
        Assert.NotNull(workspace!.Cancellation);
        Assert.False(workspace.Cancellation!.CanCancel);
        Assert.Contains(
            "completed",
            workspace.Cancellation.ReasonUnavailable ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Coordinator.CancelAsync(
                fixture.RestoreSessionId,
                CancellationToken.None));

        Assert.Contains(
            "terminal",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Running_restore_operation_blocks_cancellation_and_keeps_source_active()
    {
        await using var fixture = await CancellationFixture.CreateAsync();

        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = Guid.NewGuid(),
            RestoreAttemptId = fixture.RestoreAttemptId,
            Operation = "restore.private-test",
            Status = "running",
            RequestedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
            CurrentStep = RestoreAttemptStages.PrivateTest,
            AttemptCount = 1,
            HostMutationLevel = "docker"
        });
        await fixture.Db.SaveChangesAsync();

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.NotNull(workspace);
        Assert.NotNull(workspace!.Cancellation);
        Assert.False(workspace.Cancellation!.CanCancel);
        Assert.Contains(
            "queued or running",
            workspace.Cancellation.ReasonUnavailable ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Coordinator.CancelAsync(
                fixture.RestoreSessionId,
                CancellationToken.None));

        Assert.Contains(
            "queued or running operation",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            entry => entry.Id == fixture.RestoreAttemptId);
        Assert.Equal(RestoreAttemptStatuses.Ready, attempt.Status);
        Assert.NotNull(attempt.ActiveSourceKey);
    }

    private sealed class CancellationFixture : IAsyncDisposable
    {
        private CancellationFixture(
            string dataRoot,
            string databasePath,
            MemDbContext db,
            RestoreAttemptCoordinator coordinator,
            RestoreWorkspaceService workspace,
            RestoreStructuredLogService logs,
            BackupCatalogLifecycleService lifecycle,
            Guid restoreAttemptId,
            string restoreSessionId,
            string catalogEntryId)
        {
            DataRoot = dataRoot;
            DatabasePath = databasePath;
            Db = db;
            Coordinator = coordinator;
            Workspace = workspace;
            Logs = logs;
            Lifecycle = lifecycle;
            RestoreAttemptId = restoreAttemptId;
            RestoreSessionId = restoreSessionId;
            CatalogEntryId = catalogEntryId;
        }

        public string DataRoot { get; }
        public string DatabasePath { get; }
        public MemDbContext Db { get; }
        public RestoreAttemptCoordinator Coordinator { get; }
        public RestoreWorkspaceService Workspace { get; }
        public RestoreStructuredLogService Logs { get; }
        public BackupCatalogLifecycleService Lifecycle { get; }
        public Guid RestoreAttemptId { get; }
        public string RestoreSessionId { get; }
        public string CatalogEntryId { get; }

        public static async Task<CancellationFixture> CreateAsync()
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-workspace-cancel-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-workspace-cancel-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "element"));
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "database", "synapse.sql"),
                "-- cancellation test dump");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "matrix", "homeserver.yaml"),
                "server_name: matrix.cancel.test");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "matrix", "matrix.cancel.test.signing.key"),
                "ed25519 a_cancel test");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "element", "config.json"),
                "{}");

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
                CatalogEntryId = "bkp_workspace_cancel",
                OriginKind = BackupCatalogOriginKinds.LocalCaptured,
                DisplayName = "Workspace cancellation backup",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.LocalBackupDirectory,
                PayloadDirectoryPath = payloadRoot,
                SourceStackSlug = "cancel-stack",
                SourceBackupId = "20260630-cancel-test",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Payload is available.",
                MatrixHost = "matrix.cancel.test",
                ElementHost = "chat.cancel.test",
                WarningCount = 0,
                PayloadBytes = 1,
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
            var catalogPayloadResolver = new BackupCatalogPayloadResolver(catalogStore);

            var created = await coordinator.GetOrCreateBackupCatalogAsync(
                new BackupCatalogRestoreSource(
                    entry.Id,
                    entry.CatalogEntryId,
                    entry.OriginKind,
                    entry.DisplayName,
                    entry.PayloadState,
                    entry.PayloadDirectoryPath,
                    entry.SourceStackSlug,
                    entry.SourceBackupId,
                    entry.ValidationId,
                    entry.IntegrityStatus,
                    entry.WarningCount,
                    entry.ElementHost,
                    entry.CapturedAtUtc,
                    entry.PayloadBytes),
                CancellationToken.None);

            var workspace = new RestoreWorkspaceService(
                db,
                logs,
                workspaceStore,
                catalogStore,
                catalogPayloadResolver,
                new PrivateStagingHistoryService(configuration));

            var lifecycle = new BackupCatalogLifecycleService(
                db,
                new ImportValidationService(configuration),
                new CatalogPortableExportService(
                    configuration,
                    catalogPayloadResolver,
                    TestRuntimeContext.Create(
                        Path.Combine(dataRoot, "runtime-context"),
                        version: "0.2.0-cancellation-test")),
                configuration,
                new BackupCatalogDeleteOperationLifetime(
                    new FakeHostApplicationLifetime(),
                    NullLogger<BackupCatalogDeleteOperationLifetime>.Instance));

            return new CancellationFixture(
                dataRoot,
                databasePath,
                db,
                coordinator,
                workspace,
                logs,
                lifecycle,
                created.Attempt.Id,
                created.Attempt.RestoreSessionId,
                entry.CatalogEntryId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }

            foreach (var path in new[]
            {
                DatabasePath,
                DatabasePath + "-shm",
                DatabasePath + "-wal"
            })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }
}
