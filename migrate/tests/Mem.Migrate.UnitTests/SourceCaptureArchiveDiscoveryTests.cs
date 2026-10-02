using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.UnitTests;

public sealed class SourceCaptureArchiveDiscoveryTests
{
    [Fact]
    public async Task Selects_the_only_retained_completed_live_plaintext_capture()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            var output = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(output);
            var journal = new SqliteCaptureJournal(workspace);
            await journal.InitializeAsync(CancellationToken.None);

            var capturePath = Path.Combine(output, "live.memmigration.zip");
            await AddCaptureAsync(
                journal,
                capturePath,
                "capture-live",
                rehearsalOnly: true,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero));

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var selected = await discovery.ResolveSingleEligibleArchiveAsync(
                CancellationToken.None);
            var eligible = Assert.Single(await discovery.ListEligibleArchivesAsync(
                CancellationToken.None));

            Assert.Equal(Path.GetFullPath(capturePath), selected);
            Assert.Equal(new string('f', 64), eligible.StableSourceIdentity);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Prefers_the_only_frozen_capture_when_live_captures_also_exist()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            var output = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(output);
            var journal = new SqliteCaptureJournal(workspace);
            await journal.InitializeAsync(CancellationToken.None);

            await AddCaptureAsync(
                journal,
                Path.Combine(output, "live.memmigration.zip"),
                "capture-live",
                rehearsalOnly: true,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 1, 0, 0, TimeSpan.Zero));
            var finalPath = Path.Combine(output, "final.memmigration.zip");
            await AddCaptureAsync(
                journal,
                finalPath,
                "capture-final",
                rehearsalOnly: false,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero));

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var selected = await discovery.ResolveSingleEligibleArchiveAsync(
                CancellationToken.None);

            Assert.Equal(Path.GetFullPath(finalPath), selected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Final_handoff_rejects_a_workspace_with_only_preview_captures()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            var output = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(output);
            var journal = new SqliteCaptureJournal(workspace);
            await journal.InitializeAsync(CancellationToken.None);

            await AddCaptureAsync(
                journal,
                Path.Combine(output, "preview.memmigration.zip"),
                "capture-preview",
                rehearsalOnly: true,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero));

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                discovery.ResolveSingleEligibleArchiveAsync(
                    CancellationToken.None,
                    requireFinalFrozen: true));

            Assert.Contains("No eligible final frozen", exception.Message, StringComparison.Ordinal);
            Assert.Contains("--final-frozen", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Requires_an_explicit_archive_when_multiple_final_captures_are_eligible()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            var output = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(output);
            var journal = new SqliteCaptureJournal(workspace);
            await journal.InitializeAsync(CancellationToken.None);

            await AddCaptureAsync(
                journal,
                Path.Combine(output, "final-one.memmigration.zip"),
                "capture-final-one",
                rehearsalOnly: false,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 1, 0, 0, TimeSpan.Zero));
            await AddCaptureAsync(
                journal,
                Path.Combine(output, "final-two.memmigration.zip"),
                "capture-final-two",
                rehearsalOnly: false,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero));

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => discovery.ResolveSingleEligibleArchiveAsync(
                    CancellationToken.None));

            Assert.Contains(
                "Multiple eligible plaintext source captures",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "capture-final-one",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "capture-final-two",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Contains(
                "--archive <path>",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fails_clearly_when_no_retained_plaintext_capture_exists()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            var output = Path.Combine(root, "artifacts");
            Directory.CreateDirectory(output);
            var journal = new SqliteCaptureJournal(workspace);
            await journal.InitializeAsync(CancellationToken.None);

            var missingArchivePath = Path.Combine(
                output,
                "missing.memmigration.zip");
            await AddCaptureAsync(
                journal,
                missingArchivePath,
                "capture-missing",
                rehearsalOnly: true,
                completedAtUtc: new DateTimeOffset(2026, 7, 16, 1, 0, 0, TimeSpan.Zero));
            File.Delete(missingArchivePath);

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => discovery.ResolveSingleEligibleArchiveAsync(
                    CancellationToken.None));

            Assert.Contains(
                "No eligible completed plaintext source capture",
                exception.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Current_source_workflow_journal_without_capture_table_is_empty()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            var workflowJournal = new SqliteSourceWorkflowJournal(workspace);
            await workflowJournal.InitializeAsync(CancellationToken.None);

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var captures = await discovery.ListEligibleArchivesAsync(
                CancellationToken.None);

            Assert.Empty(captures);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => discovery.ResolveSingleEligibleArchiveAsync(
                    CancellationToken.None));

            Assert.Contains(
                "No eligible completed plaintext source capture",
                exception.Message,
                StringComparison.Ordinal);
            Assert.Null(exception.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Reports_an_incompatible_historical_journal_clearly()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = Path.Combine(root, "work");
            Directory.CreateDirectory(workspace);
            var databasePath = Path.Combine(workspace, "mem-migrate.sqlite");
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE historical_runs (id TEXT PRIMARY KEY);";
                await command.ExecuteNonQueryAsync();
            }

            var discovery = new SourceCaptureArchiveDiscovery(workspace);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => discovery.ResolveSingleEligibleArchiveAsync(
                    CancellationToken.None));

            Assert.Contains(
                "could not be read as a current capture journal",
                exception.Message,
                StringComparison.Ordinal);
            Assert.IsType<Microsoft.Data.Sqlite.SqliteException>(
                exception.InnerException);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AddCaptureAsync(
        SqliteCaptureJournal journal,
        string archivePath,
        string captureId,
        bool rehearsalOnly,
        DateTimeOffset completedAtUtc)
    {
        await File.WriteAllTextAsync(archivePath, captureId);
        var startedAtUtc = completedAtUtc.AddMinutes(-5);
        var sourceFingerprint = new string('a', 64);
        await journal.StartAsync(
            captureId,
            startedAtUtc,
            sourceFingerprint,
            CancellationToken.None);

        var report = new CaptureReport(
            Schema: "mem-v010-capture-report",
            SchemaVersion: 2,
            CaptureId: captureId,
            SourceStackId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            SourceStackSlug: "tester",
            MatrixServerName: "matrix.example.test",
            Status: CaptureLifecycleStatus.Completed,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            StartSourceFingerprint: sourceFingerprint,
            CompletionSourceFingerprint: sourceFingerprint,
            SourceChangedDuringCapture: false,
            RehearsalOnly: rehearsalOnly,
            ArchivePath: archivePath,
            ArchiveSha256: new string('b', 64),
            ArchiveBytes: new FileInfo(archivePath).Length,
            EncryptedArchivePath: null,
            EncryptedArchiveSha256: null,
            EncryptedArchiveBytes: null,
            PlaintextArchiveRetained: true,
            Plan: new CapturePlanSummary(1, 1, 1, 1, 1),
            Warnings: [],
            NextSteps: [],
            StableSourceIdentity: new string('f', 64));

        await journal.CompleteAsync(
            report,
            JsonSerializer.Serialize(report, CaptureJson.Options),
            CancellationToken.None);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"mem-source-capture-discovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
