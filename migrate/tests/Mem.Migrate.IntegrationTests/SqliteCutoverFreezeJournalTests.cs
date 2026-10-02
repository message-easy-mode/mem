using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteCutoverFreezeJournalTests
{
    [Fact]
    public async Task Stores_completed_freeze_and_preserves_plan_binding()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-mm06b-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = new SqliteCutoverFreezeJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            await journal.StartAsync(
                "mm06b-proof",
                DateTimeOffset.UtcNow,
                "mm06a-proof",
                new string('1', 64),
                CancellationToken.None);
            var checkpoint = new CutoverRollbackCheckpoint(
                "mm06a-proof",
                new string('1', 64),
                new string('2', 64),
                DateTimeOffset.UtcNow,
                [], [], [], [], "warning");
            var report = new CutoverFreezeReport(
                "mem-cutover-source-freeze-report",
                1,
                "Frozen",
                "mm06b-proof",
                "mm06a-proof",
                new string('1', 64),
                new string('2', 64),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                true,
                false,
                [],
                checkpoint,
                Path.Combine(root, "report.json"),
                Path.Combine(root, "report.md"),
                [],
                []);
            var json = System.Text.Json.JsonSerializer.Serialize(report);

            await journal.CompleteAsync(report, json, CancellationToken.None);
            var stored = await journal.GetAsync("mm06b-proof", CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal("Completed", stored!.Status);
            Assert.Equal("mm06a-proof", stored.PlanId);
            Assert.Equal(new string('1', 64), stored.PlanHash);
            Assert.Equal(new string('2', 64), stored.SourceFingerprint);
            Assert.NotNull(stored.ReportJson);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
