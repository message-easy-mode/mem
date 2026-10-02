using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteCutoverPlanJournalTests
{
    [Fact]
    public async Task Stores_completed_plan_and_preserves_input_binding()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-mm06a-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var journal = new SqliteCutoverPlanJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            await journal.StartAsync(
                "mm06a-proof",
                DateTimeOffset.UtcNow,
                new string('1', 64),
                new string('2', 64),
                "mm05d-proof",
                CancellationToken.None);
            var plan = new CutoverPlanDocument(
                "mem-cutover-plan",
                1,
                "mm06a-proof",
                "test",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(30),
                "assessment-1",
                new string('2', 64),
                AssessmentClassification.ConfirmedSupportedV010,
                false,
                false,
                [],
                [],
                [],
                [],
                new CutoverTargetEvidence(
                    "mm05d-proof",
                    "mm05c-proof",
                    "target",
                    "http://127.0.0.1:7105",
                    "bkp_1",
                    "restore-1",
                    "staging-1",
                    DateTimeOffset.UtcNow,
                    true,
                    true,
                    true,
                    true,
                    true),
                [],
                [],
                [],
                [],
                []);
            var report = new CutoverPreparationReport(
                "mem-cutover-preparation-report",
                1,
                "Prepared",
                CutoverPlanHash.Compute(plan),
                plan,
                Path.Combine(root, "plan.json"),
                Path.Combine(root, "plan.md"));
            var json = System.Text.Json.JsonSerializer.Serialize(report);

            await journal.CompleteAsync(report, json, CancellationToken.None);
            var stored = await journal.GetAsync(
                "mm06a-proof",
                CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal("Completed", stored!.Status);
            Assert.Equal(new string('1', 64), stored.InputBindingSha256);
            Assert.Equal(new string('2', 64), stored.SourceFingerprint);
            Assert.Equal(report.PlanHash, stored.PlanHash);
            Assert.NotNull(stored.ReportJson);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
