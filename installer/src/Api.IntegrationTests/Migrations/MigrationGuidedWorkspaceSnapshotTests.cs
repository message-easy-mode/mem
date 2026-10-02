using System.Data.Common;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Modules.Operator.Migrations;
using Modules.Operator.Migrations.Workspace;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationGuidedWorkspaceSnapshotTests
{
    [Fact]
    public async Task Concurrent_write_cannot_split_session_detail_from_guided_progression()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mem-guided-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "authority.db"),
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
        var now = DateTime.UtcNow;
        var options = new DbContextOptionsBuilder<MemDbContext>().UseSqlite(connectionString).Options;
        var gate = new FirstReadGate();
        try
        {
            await using (var setup = new MemDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
                await setup.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
                setup.MigrationIntakes.Add(new MigrationIntakeEntity
                {
                    Id = Guid.NewGuid(), IntakeId = "mig_snapshot", DisplayName = "Snapshot test",
                    CreatedAtUtc = now, UpdatedAtUtc = now,
                    PackageRevisions = [new MigrationPackageRevisionEntity
                    {
                        Id = Guid.NewGuid(), PackageRevisionId = "mpr_snapshot", RevisionNumber = 1,
                        Purpose = "preview", Status = "awaiting-package", RetentionState = "active",
                        ActivePurposeKey = "mig_snapshot:preview", CreatedAtUtc = now,
                        ExpiresAtUtc = now.AddHours(2),
                    }],
                });
                await setup.SaveChangesAsync();
            }

            var readerOptions = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connectionString).AddInterceptors(gate).Options;
            await using var reader = new MemDbContext(readerOptions);
            var projection = new MigrationWorkspaceProjectionService(reader,
                new MigrationSessionProjectionService(reader, TimeProvider.System), TimeProvider.System);
            var reading = projection.GetAsync("mig_snapshot", CancellationToken.None);
            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                await using var writer = new MemDbContext(options);
                var revision = await writer.MigrationPackageRevisions.SingleAsync();
                revision.Status = "package-validated";
                revision.EncryptedPackageSha256 = new string('a', 64);
                revision.DecryptedArchiveSha256 = new string('b', 64);
                revision.ValidatedAtUtc = now.AddMinutes(1);
                revision.ArchiveStackCount = 1;
                await writer.SaveChangesAsync();
            }
            finally { gate.Continue.TrySetResult(); }

            var before = await reading;
            Assert.NotNull(before);
            Assert.Equal("awaiting-package", before!.Guided.Detail.Package?.Status);
            Assert.Equal(MigrationWorkspaceStageCodes.CreateAndUploadPackage, before.Guided.CurrentStageCode);
            Assert.Equal("upload-package", before.Guided.NextAction?.Code);
            Assert.Equal("empty-session", before.Guided.Cancellation!.ConfirmationKind);
            Assert.Null(reader.Database.CurrentTransaction);

            var after = await projection.GetAsync("mig_snapshot", CancellationToken.None);
            Assert.Equal("package-validated", after!.Guided.Detail.Package?.Status);
            Assert.Equal(MigrationWorkspaceStageCodes.ReviewOldServer, after.Guided.CurrentStageCode);
            Assert.Equal("start-conversion", after.Guided.NextAction?.Code);
            Assert.Equal("package", after.Guided.Cancellation!.ConfirmationKind);
            Assert.Equal(before.Guided.OperationRevisions["cancel-migration"], after.Guided.OperationRevisions["cancel-migration"]);
            Assert.NotEqual(before.Guided.OperationRevisions["upload-package"], after.Guided.OperationRevisions["upload-package"]);
            Assert.Equal(before.Guided.OperationRevisions["finish-migration"], after.Guided.OperationRevisions["finish-migration"]);
            Assert.NotEqual(before.Guided.Revision, after.Guided.Revision);
            Assert.Equal(1L, (await reader.MigrationIntakes.AsNoTracking().SingleAsync()).StateVersion);
        }
        finally
        {
            gate.Continue.TrySetResult();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FirstReadGate : DbCommandInterceptor
    {
        private int _seen;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _seen, 1) == 0)
            {
                Started.TrySetResult();
                await Continue.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
