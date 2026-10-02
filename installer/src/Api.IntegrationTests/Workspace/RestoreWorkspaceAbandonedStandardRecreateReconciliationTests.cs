using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.StandardRecreate.Reconciliation;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Backups.Workspace;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Workspace;

public sealed class RestoreWorkspaceAbandonedStandardRecreateReconciliationTests
{
    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_05_optional_private_test_failure_is_warning_not_terminal_restore_failure()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();

        var now = DateTime.UtcNow;
        var privateTestOperationId = Guid.NewGuid();

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            item => item.Id == fixture.RestoreAttemptId);
        attempt.Status = RestoreAttemptStatuses.NeedsAttention;
        attempt.CurrentStage = RestoreAttemptStages.NeedsAttention;
        attempt.RuntimeOperationId = privateTestOperationId;
        attempt.UpdatedAtUtc = now;
        attempt.LastEventAtUtc = now;
        attempt.LastErrorCode = "restore.private-test.cancelled";
        attempt.LastErrorSummary = "The optional private test did not complete.";

        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = privateTestOperationId,
            RestoreAttemptId = attempt.Id,
            Operation = "restore.private-test",
            Status = "failed",
            RequestedAtUtc = now.AddMinutes(-2),
            StartedAtUtc = now.AddMinutes(-2),
            CompletedAtUtc = now.AddMinutes(-1),
            CurrentStep = RestoreAttemptStages.PrivateTest,
            AttemptCount = 1,
            HostMutationLevel = "docker",
            LastError = attempt.LastErrorSummary
        });
        await fixture.Db.SaveChangesAsync();

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal("private-test-warning", workspace!.OverallStatus.Code);
        Assert.Equal("warning", workspace.OverallStatus.Severity);
        Assert.Equal(
            "choose-restored-server-details",
            workspace.OverallStatus.NextAction?.Code);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_05_running_standard_recreate_is_authoritative_over_historical_optional_private_test_failure()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();

        var now = DateTime.UtcNow;
        var privateTestOperationId = Guid.NewGuid();
        var recreateOperationId = Guid.NewGuid();

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            item => item.Id == fixture.RestoreAttemptId);
        attempt.Status = RestoreAttemptStatuses.Recreating;
        attempt.CurrentStage = RestoreAttemptStages.CreateRestoredChatServer;
        attempt.RuntimeOperationId = recreateOperationId;
        attempt.UpdatedAtUtc = now;
        attempt.LastEventAtUtc = now;
        attempt.LastErrorCode = null;
        attempt.LastErrorSummary = null;

        fixture.Db.RuntimeOperations.AddRange(
            new RuntimeOperationEntity
            {
                Id = privateTestOperationId,
                RestoreAttemptId = attempt.Id,
                Operation = "restore.private-test",
                Status = "failed",
                RequestedAtUtc = now.AddMinutes(-8),
                StartedAtUtc = now.AddMinutes(-8),
                CompletedAtUtc = now.AddMinutes(-7),
                CurrentStep = RestoreAttemptStages.PrivateTest,
                AttemptCount = 1,
                HostMutationLevel = "docker",
                LastError = "Optional private test did not complete."
            },
            new RuntimeOperationEntity
            {
                Id = recreateOperationId,
                RestoreAttemptId = attempt.Id,
                Operation = "restore.standard-recreate",
                Status = "running",
                RequestedAtUtc = now.AddMinutes(-2),
                StartedAtUtc = now.AddMinutes(-2),
                CurrentStep = "restore-database",
                AttemptCount = 1,
                LockedUntilUtc = now.AddMinutes(8),
                HostMutationLevel = "filesystem,postgres-write,docker,npm"
            });
        await fixture.Db.SaveChangesAsync();

        await fixture.Logs.RecordAsync(
            attempt.Id,
            recreateOperationId,
            stage: RestoreAttemptStages.CreateRestoredChatServer,
            severity: RestoreLogSeverities.Information,
            eventCode: "restore.standard-recreate.progress.restore-database",
            message: "Restoring the Synapse database.",
            details: new Dictionary<string, string?>
            {
                ["currentStep"] = "restore-database"
            },
            ct: CancellationToken.None,
            updateAttemptSummary: true);

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal("creating-restored-server", workspace!.OverallStatus.Code);
        Assert.Equal("information", workspace.OverallStatus.Severity);
        Assert.Contains(
            "previous optional private test",
            workspace.OverallStatus.Description,
            StringComparison.OrdinalIgnoreCase);

        var privateStage = Assert.Single(
            workspace.StandardStages,
            stage => stage.Code == "private-test");
        Assert.Equal(RestoreWorkspaceStageStates.Failed, privateStage.State);

        var recreateStage = Assert.Single(
            workspace.StandardStages,
            stage => stage.Code == "create-restored-chat-server");
        Assert.Equal(RestoreWorkspaceStageStates.Running, recreateStage.State);
        Assert.NotNull(recreateStage.OperationSummary);
        Assert.Equal("restore-database", recreateStage.OperationSummary!.CurrentStep);
        Assert.NotNull(recreateStage.OperationSummary.LastActivityAtUtc);
        Assert.Equal(1, recreateStage.OperationSummary.AttemptNumber);
    }

    [Fact]
    public async Task Startup_reconciliation_terminalizes_previous_process_standard_recreate_and_allows_reviewed_cancel()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();

        var operationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync(
            item => item.Id == fixture.RestoreAttemptId);
        attempt.Status = RestoreAttemptStatuses.Recreating;
        attempt.CurrentStage = RestoreAttemptStages.CreateRestoredChatServer;
        attempt.RuntimeOperationId = operationId;
        attempt.UpdatedAtUtc = now;
        attempt.LastEventAtUtc = now;

        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = operationId,
            RestoreAttemptId = attempt.Id,
            Operation = "restore.standard-recreate",
            Status = "running",
            RequestedAtUtc = now.AddMinutes(-2),
            StartedAtUtc = now.AddMinutes(-2),
            CurrentStep = RestoreAttemptStages.CreateRestoredChatServer,
            AttemptCount = 1,
            LockedUntilUtc = now.AddMinutes(8),
            HostMutationLevel = "filesystem,postgres-write,docker,npm",
            InputJson = """
                {
                  "restoreSessionId": "reconciliation-fixture",
                  "recreateId": "recreate-before-restart",
                  "targetStackSlug": "reconciled-target"
                }
                """
        });

        fixture.Db.RestoreTargetClaims.Add(new RestoreTargetClaimEntity
        {
            Id = Guid.NewGuid(),
            RestoreAttemptId = attempt.Id,
            ResourceType = RestoreTargetResourceTypes.StackSlug,
            ResourceValue = "reconciled-target",
            ActiveClaimKey = "stack-slug:reconciled-target",
            ClaimedAtUtc = now
        });

        await fixture.Db.SaveChangesAsync();

        var before = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.NotNull(before);
        Assert.False(before!.Cancellation!.CanCancel);

        var result = await fixture.Reconciliation
            .ReconcileAfterControlPlaneStartAsync(CancellationToken.None);

        Assert.Equal(1, result.ReconciledCount);
        Assert.Single(result.RestoreSessionIds);
        Assert.Equal(fixture.RestoreSessionId, result.RestoreSessionIds[0]);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync(
            item => item.Id == operationId);
        Assert.Equal("failed", operation.Status);
        Assert.Equal(
            RestoreAttemptStages.CreateRestoredChatServer,
            operation.CurrentStep);
        Assert.NotNull(operation.CompletedAtUtc);
        Assert.Null(operation.LockedUntilUtc);
        Assert.Contains(
            "Control Plane restarted",
            operation.LastError ?? string.Empty,
            StringComparison.Ordinal);

        var reconciledAttempt = await fixture.Db.RestoreAttempts.SingleAsync(
            item => item.Id == fixture.RestoreAttemptId);
        Assert.Equal(
            RestoreAttemptStatuses.NeedsAttention,
            reconciledAttempt.Status);
        Assert.Equal(
            RestoreAttemptStages.NeedsAttention,
            reconciledAttempt.CurrentStage);
        Assert.Equal(
            "restore.standard-recreate.abandoned-after-control-plane-restart",
            reconciledAttempt.LastErrorCode);
        Assert.NotNull(reconciledAttempt.ActiveSourceKey);

        var retainedClaim = await fixture.Db.RestoreTargetClaims.SingleAsync(
            claim => claim.RestoreAttemptId == fixture.RestoreAttemptId);
        Assert.NotNull(retainedClaim.ActiveClaimKey);
        Assert.Null(retainedClaim.ReleasedAtUtc);

        var after = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.NotNull(after);
        Assert.Equal("needs-attention", after!.OverallStatus.Code);
        Assert.True(after.Cancellation!.CanCancel);

        var logPage = await fixture.Logs.ListAsync(
            fixture.RestoreSessionId,
            new RestoreLogQuery(1, 50, null, null, null),
            CancellationToken.None);
        Assert.NotNull(logPage);
        Assert.Contains(
            logPage!.Events,
            item => item.EventCode ==
                "restore.standard-recreate.abandoned-after-control-plane-restart");

        Assert.Contains(
            fixture.Diagnostics.Requests,
            request =>
                request.EventCode == "runtime.operation.failed" &&
                request.CreateIncident &&
                request.OperationId == operationId &&
                request.Resource?.Kind == "restore" &&
                request.Resource?.Id == fixture.RestoreSessionId);

        var repeated = await fixture.Reconciliation
            .ReconcileAfterControlPlaneStartAsync(CancellationToken.None);
        Assert.Equal(0, repeated.ReconciledCount);

        var cancelled = await fixture.Coordinator.CancelAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.Equal(RestoreAttemptStatuses.Cancelled, cancelled.Status);

        var releasedClaim = await fixture.Db.RestoreTargetClaims.SingleAsync(
            claim => claim.RestoreAttemptId == fixture.RestoreAttemptId);
        Assert.Null(releasedClaim.ActiveClaimKey);
        Assert.NotNull(releasedClaim.ReleasedAtUtc);
        Assert.Equal("restore-cancelled", releasedClaim.ReleaseReason);
    }

    [Fact]
    public async Task Startup_reconciliation_does_not_terminalize_other_running_restore_operations()
    {
        await using var fixture = await ReconciliationFixture.CreateAsync();

        var operationId = Guid.NewGuid();
        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = operationId,
            RestoreAttemptId = fixture.RestoreAttemptId,
            Operation = "restore.private-test",
            Status = "running",
            RequestedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow,
            CurrentStep = RestoreAttemptStages.PrivateTest,
            AttemptCount = 1,
            LockedUntilUtc = DateTime.UtcNow.AddMinutes(10),
            HostMutationLevel = "docker"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Reconciliation
            .ReconcileAfterControlPlaneStartAsync(CancellationToken.None);

        Assert.Equal(0, result.ReconciledCount);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync(
            item => item.Id == operationId);
        Assert.Equal("running", operation.Status);
        Assert.Null(operation.CompletedAtUtc);
        Assert.Empty(fixture.Diagnostics.Requests);
    }

    private sealed class ReconciliationFixture : IAsyncDisposable
    {
        private ReconciliationFixture(
            string dataRoot,
            string databasePath,
            MemDbContext db,
            RestoreAttemptCoordinator coordinator,
            RestoreWorkspaceService workspace,
            RestoreStructuredLogService logs,
            RecordingDiagnosticEventWriter diagnostics,
            AbandonedStandardRecreateReconciliationService reconciliation,
            Guid restoreAttemptId,
            string restoreSessionId)
        {
            DataRoot = dataRoot;
            DatabasePath = databasePath;
            Db = db;
            Coordinator = coordinator;
            Workspace = workspace;
            Logs = logs;
            Diagnostics = diagnostics;
            Reconciliation = reconciliation;
            RestoreAttemptId = restoreAttemptId;
            RestoreSessionId = restoreSessionId;
        }

        public string DataRoot { get; }
        public string DatabasePath { get; }
        public MemDbContext Db { get; }
        public RestoreAttemptCoordinator Coordinator { get; }
        public RestoreWorkspaceService Workspace { get; }
        public RestoreStructuredLogService Logs { get; }
        public RecordingDiagnosticEventWriter Diagnostics { get; }
        public AbandonedStandardRecreateReconciliationService Reconciliation { get; }
        public Guid RestoreAttemptId { get; }
        public string RestoreSessionId { get; }

        public static async Task<ReconciliationFixture> CreateAsync()
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-workspace-reconcile-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-workspace-reconcile-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

            Directory.CreateDirectory(Path.Combine(payloadRoot, "database"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "matrix"));
            Directory.CreateDirectory(Path.Combine(payloadRoot, "element"));
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "database", "synapse.sql"),
                "-- reconciliation test dump");
            await File.WriteAllTextAsync(
                Path.Combine(payloadRoot, "matrix", "homeserver.yaml"),
                "server_name: matrix.reconciliation.test");
            await File.WriteAllTextAsync(
                Path.Combine(
                    payloadRoot,
                    "matrix",
                    "matrix.reconciliation.test.signing.key"),
                "ed25519 a_reconciliation test");
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
                CatalogEntryId = "bkp_workspace_reconcile",
                OriginKind = BackupCatalogOriginKinds.LocalCaptured,
                DisplayName = "Workspace reconciliation backup",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind =
                    BackupCatalogPayloadStorageKinds.LocalBackupDirectory,
                PayloadDirectoryPath = payloadRoot,
                SourceStackSlug = "reconciliation-source",
                SourceBackupId = "20260818-reconciliation",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Payload is available.",
                MatrixHost = "matrix.reconciliation.test",
                ElementHost = "chat.reconciliation.test",
                WarningCount = 0,
                PayloadBytes = 1,
                CreatedAtUtc = DateTime.UtcNow,
                CapturedAtUtc = DateTime.UtcNow
            };
            db.BackupCatalogEntries.Add(entry);
            await db.SaveChangesAsync();

            var workspaceStore = new RestoreAttemptWorkspaceStore(configuration);
            var diagnostics = new RecordingDiagnosticEventWriter();
            var operationStore = new RuntimeOperationStore(db, diagnostics);
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
            var catalogPayloadResolver =
                new BackupCatalogPayloadResolver(catalogStore);

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

            var reconciliation =
                new AbandonedStandardRecreateReconciliationService(
                    db,
                    coordinator,
                    NullLogger<AbandonedStandardRecreateReconciliationService>.Instance);

            return new ReconciliationFixture(
                dataRoot,
                databasePath,
                db,
                coordinator,
                workspace,
                logs,
                diagnostics,
                reconciliation,
                created.Attempt.Id,
                created.Attempt.RestoreSessionId);
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

    private sealed class RecordingDiagnosticEventWriter
        : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_{Requests.Count}",
                IncidentId: request.IncidentId,
                WarningCode: null));
        }
    }
}
