using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteCaptureJournalTests
{
    [Fact]
    public async Task Records_and_returns_a_completed_capture_idempotently()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-capture-journal-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var journal = new SqliteCaptureJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            var started = DateTimeOffset.Parse("2026-07-12T22:00:00Z");
            await journal.StartAsync(
                "20260712-220000Z-0123456789abcdef0123456789abcdef",
                started,
                new string('a', 64),
                CancellationToken.None);
            var report = new CaptureReport(
                "mem-migration-capture-receipt",
                2,
                "20260712-220000Z-0123456789abcdef0123456789abcdef",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "tester",
                "matrix.example.test",
                CaptureLifecycleStatus.Completed,
                started,
                started.AddMinutes(1),
                new string('a', 64),
                new string('a', 64),
                SourceChangedDuringCapture: false,
                RehearsalOnly: true,
                "/private/capture.zip",
                new string('b', 64),
                123,
                null,
                null,
                null,
                PlaintextArchiveRetained: true,
                new CapturePlanSummary(1, 100, 1000, 500, 20),
                [],
                ["verify"]);
            var json = JsonSerializer.Serialize(report, CaptureJson.Options);
            await journal.CompleteAsync(
                report,
                json,
                CancellationToken.None);

            var stored = await journal.GetAsync(
                report.CaptureId,
                CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal(CaptureLifecycleStatus.Completed, stored.Status);
            Assert.Equal(report.ArchiveSha256, stored.ArchiveSha256);
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
