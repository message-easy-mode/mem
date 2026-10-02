using System.Text.Json;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Operations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Workspace;

/// <summary>
/// Regression coverage for the catalog-bound restore invariant. The test uses
/// the real SQLite migration and coordinator so validation receipts cannot
/// quietly become restore sources again.
/// </summary>
public sealed class CatalogBoundRestoreAttemptPersistenceTests
{
    [Fact]
    public async Task Local_and_imported_catalog_sources_each_create_or_resume_one_active_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();

        var localSource = await fixture.CreateAndRegisterSourceAsync(
            catalogEntryId: "bkp_local_restore_001",
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            displayName: "Local capture / 20260630-001",
            stackSlug: "local-stack",
            backupId: "20260630-001");
        var importedSource = await fixture.CreateAndRegisterSourceAsync(
            catalogEntryId: "bkp_import_restore_001",
            originKind: BackupCatalogOriginKinds.ImportedZip,
            displayName: "Imported portable ZIP / 20260630-002",
            stackSlug: "imported-stack",
            backupId: "20260630-002");

        var localCreated = await fixture.Coordinator.GetOrCreateBackupCatalogAsync(localSource, CancellationToken.None);
        var localResumed = await fixture.Coordinator.GetOrCreateBackupCatalogAsync(localSource, CancellationToken.None);
        var importedCreated = await fixture.Coordinator.GetOrCreateBackupCatalogAsync(importedSource, CancellationToken.None);
        var importedResumed = await fixture.Coordinator.GetOrCreateBackupCatalogAsync(importedSource, CancellationToken.None);

        Assert.True(localCreated.Created);
        Assert.True(localResumed.Resumed);
        Assert.Equal(localCreated.Attempt.RestoreSessionId, localResumed.Attempt.RestoreSessionId);
        Assert.True(importedCreated.Created);
        Assert.True(importedResumed.Resumed);
        Assert.Equal(importedCreated.Attempt.RestoreSessionId, importedResumed.Attempt.RestoreSessionId);
        Assert.Equal(2, await fixture.Db.RestoreAttempts.CountAsync());
        Assert.All(await fixture.Db.RestoreAttempts.ToListAsync(), attempt =>
        {
            Assert.Equal("backup-catalog", attempt.SourceKind);
            Assert.NotNull(attempt.BackupCatalogEntryId);
            Assert.False(string.IsNullOrWhiteSpace(attempt.SourceCatalogEntryIdSnapshot));
        });
    }



    [Fact]
    public async Task Cancel_releases_catalog_source_for_a_new_attempt_and_snapshot_has_no_validation_id()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.CreateAndRegisterSourceAsync(
            catalogEntryId: "bkp_cancel_restore_001",
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            displayName: "Disposable catalog capture",
            stackSlug: "cancel-stack",
            backupId: "backup-cancel-001");

        var first = await fixture.Coordinator.GetOrCreateBackupCatalogAsync(source, CancellationToken.None);
        var cancelled = await fixture.Coordinator.CancelAsync(first.Attempt.RestoreSessionId, CancellationToken.None);
        var second = await fixture.Coordinator.GetOrCreateBackupCatalogAsync(source, CancellationToken.None);

        Assert.Equal(RestoreAttemptStatuses.Cancelled, cancelled.Status);
        Assert.True(second.Created);
        Assert.NotEqual(first.Attempt.RestoreSessionId, second.Attempt.RestoreSessionId);
        Assert.Equal("bkp_cancel_restore_001", second.Attempt.CatalogEntryId);
        Assert.Equal("Disposable catalog capture", second.Attempt.SourceDisplayName);
        Assert.Equal(BackupCatalogOriginKinds.LocalCaptured, second.Attempt.SourceOriginKind);

        var projection = JsonSerializer.Serialize(second.Attempt);
        Assert.False(projection.Contains("validationId", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string dataRoot, string databasePath, MemDbContext db, RestoreAttemptCoordinator coordinator)
        {
            _dataRoot = dataRoot;
            _databasePath = databasePath;
            Db = db;
            Coordinator = coordinator;
        }

        private readonly string _dataRoot;
        private readonly string _databasePath;

        public MemDbContext Db { get; }
        public RestoreAttemptCoordinator Coordinator { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), $"mem-catalog-bound-attempts-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(Path.GetTempPath(), $"mem-catalog-bound-attempts-{Guid.NewGuid():N}.db");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();

            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options);
            await db.Database.MigrateAsync();

            var workspaceStore = new RestoreAttemptWorkspaceStore(configuration);
            var coordinator = new RestoreAttemptCoordinator(
                db,
                workspaceStore,
                new RuntimeOperationStore(db),
                new RestoreStructuredLogService(
                    db,
                    workspaceStore,
                    NullLogger<RestoreStructuredLogService>.Instance),
                NullLogger<RestoreAttemptCoordinator>.Instance);

            return new Fixture(dataRoot, databasePath, db, coordinator);
        }

        public async Task<BackupCatalogRestoreSource> CreateAndRegisterSourceAsync(
            string catalogEntryId,
            string originKind,
            string displayName,
            string stackSlug,
            string backupId)
        {
            var entryId = Guid.NewGuid();
            var payloadDirectory = Path.Combine(_dataRoot, "catalog", catalogEntryId);
            Directory.CreateDirectory(payloadDirectory);

            Db.BackupCatalogEntries.Add(new Infrastructure.Data.Entities.BackupCatalogEntryEntity
            {
                Id = entryId,
                CatalogEntryId = catalogEntryId,
                OriginKind = originKind,
                DisplayName = displayName,
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = payloadDirectory,
                SourceStackSlug = stackSlug,
                SourceBackupId = backupId,
                ValidationId = originKind == BackupCatalogOriginKinds.ImportedZip ? "ingestion-only-validation" : null,
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Test payload is available.",
                WarningCount = 0,
                PayloadBytes = 1,
                CreatedAtUtc = DateTime.UtcNow,
                CapturedAtUtc = DateTime.UtcNow
            });
            await Db.SaveChangesAsync();

            return new BackupCatalogRestoreSource(
                EntryId: entryId,
                CatalogEntryId: catalogEntryId,
                OriginKind: originKind,
                DisplayName: displayName,
                PayloadState: BackupCatalogPayloadStates.Available,
                PayloadDirectoryPath: payloadDirectory,
                SourceStackSlug: stackSlug,
                SourceBackupId: backupId,
                ValidationId: originKind == BackupCatalogOriginKinds.ImportedZip ? "ingestion-only-validation" : null,
                IntegrityStatus: BackupCatalogIntegrityStatuses.Valid,
                WarningCount: 0,
                ElementHost: null,
                CapturedAtUtc: DateTime.UtcNow,
                PayloadBytes: 1);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(_dataRoot)) Directory.Delete(_dataRoot, recursive: true);
            foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
