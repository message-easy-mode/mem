using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Diagnostics;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticEventStoreTests
{
    [Fact]
    public async Task Concurrent_writers_produce_valid_ndjson_and_active_file_is_readable()
    {
        using var fixture = new MemDiagnosticTestFixture();

        var writes = Enumerable.Range(0, 40)
            .Select(index => fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: index % 5 == 0
                    ? MemDiagnosticSeverities.Warning
                    : MemDiagnosticSeverities.Information,
                EventCode: $"diagnostics.test.event_{index:000}",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: $"Concurrent test event {index}",
                Details: new Dictionary<string, string?>
                {
                    ["index"] = index.ToString()
                })))
            .ToArray();

        var results = await Task.WhenAll(writes);
        Assert.All(results, result => Assert.True(result.Stored));

        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(PageSize: 100));
        Assert.Equal(40, page.Events.Count);
        Assert.Null(page.NextCursor);
        Assert.Empty(page.Warnings);

        var eventFiles = fixture.Store.EnumerateAllEventFiles();
        Assert.NotEmpty(eventFiles);
        foreach (var line in eventFiles.SelectMany(File.ReadAllLines))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        }

        var health = fixture.Health.GetHealth();
        Assert.Equal(40, health.StoredEventCount);
        Assert.Equal("ready", health.Status);
    }


    [Fact]
    public async Task Active_event_file_can_remain_open_for_read_while_an_event_is_appended()
    {
        using var fixture = new MemDiagnosticTestFixture();
        await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Information,
            EventCode: "diagnostics.test.first",
            Source: "host-agent",
            Feature: "diagnostics",
            Message: "First event"));
        var activePath = fixture.Store.CurrentActiveFilePath!;

        await using var openReader = new FileStream(
            activePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var result = await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Warning,
            EventCode: "diagnostics.test.second",
            Source: "host-agent",
            Feature: "diagnostics",
            Message: "Second event"));

        Assert.True(result.Stored);
        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery());
        Assert.Equal(2, page.Events.Count);
    }

    [Fact]
    public async Task Events_survive_a_new_reader_and_are_filtered_server_side()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-diagnostics-restart-{Guid.NewGuid():N}");
        try
        {
            var options = new MemDiagnosticsOptions { RootPath = root };
            using (var first = new MemDiagnosticTestFixture(options, rootPath: root))
            {
                await first.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Error,
                    EventCode: "migration.staging.failed",
                    Source: "host-agent",
                    Feature: "migration",
                    Message: "Private staging failed",
                    Stage: "private-staging",
                    Resource: new MemDiagnosticResource(
                        "migration",
                        "mig_123",
                        StackId: "stack-123")));
            }

            var secondOptions = new MemDiagnosticsOptions { RootPath = root };
            using var second = new MemDiagnosticTestFixture(secondOptions, rootPath: root);
            var page = await second.Reader.QueryAsync(new MemDiagnosticQuery(
                Severity: MemDiagnosticSeverities.Error,
                Feature: "migration",
                Stage: "private-staging",
                StackId: "stack-123",
                Search: "staging"));

            var @event = Assert.Single(page.Events);
            Assert.Equal("migration.staging.failed", @event.EventCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task File_rollover_and_event_size_limits_are_enforced()
    {
        var options = new MemDiagnosticsOptions
        {
            EventFileMaximumBytes = 64 * 1024,
            MaximumEventBytes = 4096,
            MaximumDetailCharacters = 1500
        };
        using var fixture = new MemDiagnosticTestFixture(options);
        var oversizedText = new string('x', 20000);

        for (var index = 0; index < 60; index++)
        {
            var result = await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Warning,
                EventCode: "diagnostics.test.large_event",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: oversizedText,
                Details: Enumerable.Range(0, 20).ToDictionary(
                    key => $"detail-{key}",
                    _ => (string?)oversizedText,
                    StringComparer.Ordinal)));
            Assert.True(result.Stored);
        }

        var files = fixture.Store.EnumerateAllEventFiles();
        Assert.True(files.Count > 1);
        Assert.All(files, file => Assert.True(new FileInfo(file).Length <= options.EventFileMaximumBytes));
        Assert.All(
            files.SelectMany(File.ReadAllLines).Where(line => !string.IsNullOrWhiteSpace(line)),
            line => Assert.True(Encoding.UTF8.GetByteCount(line) + 1 <= options.MaximumEventBytes));

        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(PageSize: 100));
        Assert.Equal(60, page.Events.Count);
        Assert.All(page.Events, @event => Assert.True(@event.Truncated));
    }

    [Fact]
    public async Task Malformed_and_incomplete_lines_are_ignored_without_losing_valid_events()
    {
        using var fixture = new MemDiagnosticTestFixture();
        await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Error,
            EventCode: "diagnostics.test.valid",
            Source: "host-agent",
            Feature: "diagnostics",
            Message: "Valid event"));
        Assert.NotNull(fixture.Store.CurrentActiveFilePath);
        var activePath = fixture.Store.CurrentActiveFilePath!;
        await File.AppendAllTextAsync(activePath, "{not-json}\n{\"schemaVersion\":1");

        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery());

        Assert.Single(page.Events);
        Assert.Contains(MemDiagnosticCodes.EventMalformed, page.Warnings);
        var health = fixture.Health.GetHealth();
        Assert.Equal(2, health.MalformedLineCount);
        Assert.Equal("degraded", health.Status);
    }

    [Fact]
    public async Task Cursor_round_trips_and_tampering_is_rejected()
    {
        using var fixture = new MemDiagnosticTestFixture();
        for (var index = 0; index < 3; index++)
        {
            fixture.TimeProvider.SetUtcNow(
                fixture.TimeProvider.GetUtcNow().AddSeconds(1));
            await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: $"diagnostics.test.page_{index}",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: $"Event {index}"));
        }

        var first = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(PageSize: 1));
        Assert.Single(first.Events);
        Assert.NotNull(first.NextCursor);

        var second = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(
            Cursor: first.NextCursor,
            PageSize: 1));
        Assert.Single(second.Events);
        Assert.NotEqual(first.Events[0].EventId, second.Events[0].EventId);

        var cursor = first.NextCursor!;
        var tamperIndex = cursor.Length / 2;
        var replacement = cursor[tamperIndex] == 'A' ? 'B' : 'A';
        var tampered = cursor[..tamperIndex] + replacement + cursor[(tamperIndex + 1)..];
        await Assert.ThrowsAsync<MemDiagnosticCursorException>(() =>
            fixture.Reader.QueryAsync(new MemDiagnosticQuery(Cursor: tampered)));
    }

    [Fact]
    public async Task Query_bounds_page_size_and_maximum_time_window()
    {
        var options = new MemDiagnosticsOptions
        {
            DefaultPageSize = 2,
            MaximumPageSize = 3,
            DefaultQueryWindowHours = 24,
            MaximumQueryWindowHours = 48
        };
        using var fixture = new MemDiagnosticTestFixture(options);
        await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Information,
            EventCode: "diagnostics.test.query_bounds",
            Source: "host-agent",
            Feature: "diagnostics",
            Message: "Bounded query"));

        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(
            FromUtc: fixture.TimeProvider.GetUtcNow().AddDays(-30),
            PageSize: 999));

        Assert.Equal(3, page.PageSize);
        Assert.True(page.WindowClamped);
        Assert.Equal(
            TimeSpan.FromHours(48),
            page.UntilUtc - page.FromUtc);
    }

    [Fact]
    public async Task Disabled_and_unwritable_stores_do_not_escape_failures()
    {
        var disabledOptions = new MemDiagnosticsOptions { Enabled = false };
        using (var disabled = new MemDiagnosticTestFixture(disabledOptions))
        {
            var result = await disabled.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Error,
                EventCode: "diagnostics.test.disabled",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: "Disabled"));
            Assert.False(result.Stored);
            Assert.Equal(MemDiagnosticCodes.StoreDisabled, result.WarningCode);
            Assert.Empty(disabled.Store.EnumerateAllEventFiles());

            var page = await disabled.Reader.QueryAsync(new MemDiagnosticQuery());
            Assert.Contains(MemDiagnosticCodes.StoreDisabled, page.Warnings);
        }

        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-diagnostics-blocked-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        var blockingFile = Path.Combine(temporaryRoot, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "block");
        try
        {
            var options = new MemDiagnosticsOptions
            {
                RootPath = Path.Combine(blockingFile, "diagnostics")
            };
            using var blocked = new MemDiagnosticTestFixture(
                options,
                rootPath: options.RootPath);
            var result = await blocked.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Error,
                EventCode: "diagnostics.test.write_failure",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: "Write failure"));

            Assert.False(result.Stored);
            Assert.Equal(MemDiagnosticCodes.StoreWriteFailed, result.WarningCode);
            Assert.Equal(1, blocked.Health.GetHealth().DroppedEventCount);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }
    [Fact]
    public async Task Cursor_paginates_same_timestamp_events_without_duplicates_or_skips()
    {
        using var fixture = new MemDiagnosticTestFixture();
        for (var index = 0; index < 5; index++)
        {
            await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: $"diagnostics.test.same_time_{index}",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: $"Event {index}"));
        }

        var observed = new List<string>();
        string? cursor = null;
        do
        {
            var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(
                Cursor: cursor,
                PageSize: 2));
            observed.AddRange(page.Events.Select(@event => @event.EventId));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(5, observed.Count);
        Assert.Equal(5, observed.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Incident_only_query_filters_unrelated_events_before_the_page_boundary()
    {
        using var fixture = new MemDiagnosticTestFixture();
        const string incidentId = "inc_diagnostics_incident_volume";

        await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Error,
            EventCode: "diagnostics.test.incident_volume",
            Source: "host-agent",
            Feature: "diagnostics",
            Message: "Older incident evidence",
            IncidentId: incidentId));

        for (var index = 0; index < 235; index++)
        {
            fixture.TimeProvider.SetUtcNow(
                fixture.TimeProvider.GetUtcNow().AddSeconds(1));
            await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: $"diagnostics.test.technical_{index:000}",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: $"Newer technical event {index}"));
        }

        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(
            PageSize: 50,
            RequireIncidentId: true));

        var incident = Assert.Single(page.Events);
        Assert.Equal(incidentId, incident.IncidentId);
        Assert.Null(page.NextCursor);
        Assert.Empty(page.Warnings);
    }

    [Fact]
    public async Task Reader_and_writer_propagate_requested_cancellation()
    {
        using var fixture = new MemDiagnosticTestFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: "diagnostics.test.cancelled_write",
                Source: "host-agent",
                Feature: "diagnostics",
                Message: "Cancelled"), cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Reader.QueryAsync(
                new MemDiagnosticQuery(),
                cancellation.Token));
    }

}
