using Mem.Migrate.Core.Target;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteTargetPrivateStageJournalTests
{
    [Fact]
    public async Task Records_progress_and_returns_completed_report_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-mm05d-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = new SqliteTargetPrivateStageJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            var started = DateTimeOffset.UtcNow;
            await journal.StartAsync("stage-1", started, "import-1", "profile-1", "http://127.0.0.1:7105", "bkp_1", CancellationToken.None);
            await journal.SaveProgressAsync("stage-1", "restore-1", "staging-1", CancellationToken.None);

            var report = new TargetPrivateStageReport(
                "mem-target-private-stage-report", 1, "stage-1", "Completed", started, DateTimeOffset.UtcNow,
                "import-1", "profile-1", "http://127.0.0.1:7105", "bkp_1", "restore-1", "staging-1",
                true, true, true, true, true, true, true, Path.Combine(root, "evidence.json"), [], []);
            await journal.CompleteAsync(report, System.Text.Json.JsonSerializer.Serialize(report), CancellationToken.None);

            var stored = await journal.GetAsync("stage-1", CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal("Completed", stored!.Status);
            Assert.Equal("restore-1", stored.RestoreSessionId);
            Assert.Equal("staging-1", stored.StagingId);
            Assert.NotNull(stored.ReportJson);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
