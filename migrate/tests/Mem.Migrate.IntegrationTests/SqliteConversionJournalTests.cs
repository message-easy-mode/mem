using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteConversionJournalTests
{
    [Fact]
    public async Task Records_and_returns_a_completed_conversion_idempotently()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-migrate-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new SqliteConversionJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            var stackId = Guid.NewGuid();
            var started = DateTimeOffset.UtcNow;
            await journal.StartAsync("mm04-test", started, new string('a', 64), stackId, CancellationToken.None);

            var report = new ConversionReport(
                "mem-synapse-conversion-report", 1, "mm04-test",
                ConversionLifecycleStatus.Completed, started, started.AddMinutes(1),
                "migration", stackId, "matrix.example.test", "/archive.zip",
                new string('a', 64), "synapse@sha256:test", "postgres@sha256:test",
                "/dump", new string('b', 64), 123, "/evidence", false,
                [], [], []);
            const string json = "{\"conversionId\":\"mm04-test\"}";
            await journal.CompleteAsync(report, json, CancellationToken.None);

            var stored = await journal.GetAsync("mm04-test", CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal(ConversionLifecycleStatus.Completed, stored.Status);
            Assert.Equal(stackId, stored.SourceStackId);
            Assert.Equal(json, stored.ReportJson);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
