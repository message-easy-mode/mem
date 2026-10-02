using Mem.Migrate.Core.Target;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteTargetImportJournalTests
{
    [Fact]
    public async Task Records_profile_target_progress_and_completed_report()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var journal = new SqliteTargetImportJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            var started = DateTimeOffset.UtcNow;
            await journal.StartAsync(
                "mm05c-test",
                started,
                new string('a', 64),
                new string('b', 64),
                "target-server",
                "http://localhost:8080",
                CancellationToken.None);
            await journal.SaveProgressAsync(
                "mm05c-test",
                "mig_test",
                "val_test",
                "bkp_test",
                "artifact-test",
                CancellationToken.None);

            var report = new TargetImportReport(
                "mem-target-import-report",
                2,
                "mm05c-test",
                "Completed",
                started,
                started.AddMinutes(1),
                "target-server",
                "http://localhost:8080",
                "/manifest",
                new string('a', 64),
                "/zip",
                new string('b', 64),
                10,
                "artifact-test",
                "mig_test",
                "val_test",
                "bkp_test",
                "bound",
                new string('b', 64),
                new string('b', 64),
                10,
                10,
                [],
                []);
            const string json = "{\"attemptId\":\"mm05c-test\"}";
            await journal.CompleteAsync(
                report,
                json,
                CancellationToken.None);

            var stored = await journal.GetAsync(
                "mm05c-test",
                CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal("Completed", stored.Status);
            Assert.Equal("target-server", stored.TargetProfileName);
            Assert.Equal("http://localhost:8080", stored.TargetBaseUrl);
            Assert.Equal("mig_test", stored.IntakeId);
            Assert.Equal("val_test", stored.ValidationId);
            Assert.Equal("bkp_test", stored.CatalogEntryId);
            Assert.Equal(json, stored.ReportJson);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
