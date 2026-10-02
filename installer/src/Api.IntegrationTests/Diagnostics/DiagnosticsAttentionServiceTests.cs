using Microsoft.Extensions.Logging.Abstractions;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsAttentionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Projects_only_attention_incidents_with_a_bounded_server_authored_link_set()
    {
        var events = new List<MemDiagnosticEvent>
        {
            Event("information", "inc_seq_optional", "seq.stopped_intentionally", Now),
            Event("warning", "inc_warning_1", "runtime.warning", Now.AddMinutes(-1)),
            Event("error", "inc_error_1", "migration.failed", Now.AddMinutes(-2)),
            Event("warning", "inc_error_1", "migration.retry", Now.AddMinutes(-1)),
            Event("critical", "inc_critical_1", "runtime.critical", Now.AddMinutes(-3)),
            Event("warning", "inc_warning_2", "restore.warning", Now.AddMinutes(-4)),
            Event("error", "inc_error_2", "federation.failed", Now.AddMinutes(-5)),
            Event("warning", "inc_warning_3", "seq.delivery_unreachable", Now.AddMinutes(-6)),
        };
        var service = CreateService(new StaticReader(events));

        var result = await service.GetAsync(
            DiagnosticsAttentionService.MaximumItems,
            CancellationToken.None);

        Assert.Equal("attention", result.State);
        Assert.Equal(6, result.Total);
        Assert.Equal("critical", result.HighestSeverity);
        Assert.Equal(5, result.Items.Count);
        Assert.Equal("inc_critical_1", result.Items[0].IncidentId);
        Assert.Equal(
            "/diagnostics/logs?incident=inc_critical_1",
            result.Items[0].Href);
        Assert.DoesNotContain(result.Items, item =>
            string.Equals(item.Severity, "information", StringComparison.OrdinalIgnoreCase));
        Assert.Single(result.Items.Where(item => item.IncidentId == "inc_error_1"));
        Assert.False(result.Partial);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task Warning_only_attention_uses_the_warning_state()
    {
        var service = CreateService(new StaticReader(
        [
            Event("warning", "inc_warning_only", "runtime.warning", Now)
        ]));

        var result = await service.GetAsync(5, CancellationToken.None);

        Assert.Equal("warning", result.State);
        Assert.Equal("warning", result.HighestSeverity);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Empty_attention_is_ready_only_when_the_projection_is_complete()
    {
        var service = CreateService(new StaticReader(
        [
            Event("information", null, "control_plane.started", Now)
        ]));

        var result = await service.GetAsync(5, CancellationToken.None);

        Assert.Equal("ready", result.State);
        Assert.Equal(0, result.Total);
        Assert.Null(result.HighestSeverity);
        Assert.Empty(result.Items);
        Assert.False(result.Partial);
    }

    [Fact]
    public async Task Reader_failure_returns_unavailable_instead_of_all_clear()
    {
        var service = CreateService(new ThrowingReader());

        var result = await service.GetAsync(5, CancellationToken.None);

        Assert.Equal("unavailable", result.State);
        Assert.True(result.Partial);
        Assert.Equal(0, result.Total);
        Assert.Contains("diagnostics.attention_unavailable", result.Warnings);
    }

    private static DiagnosticsAttentionService CreateService(
        IMemDiagnosticEventReader reader)
    {
        var timeProvider = new FakeTimeProvider(Now);
        var collector = new DiagnosticsEventCollector(
            reader,
            new DiagnosticsApiOptions(),
            timeProvider);
        return new DiagnosticsAttentionService(
            collector,
            timeProvider,
            NullLogger<DiagnosticsAttentionService>.Instance);
    }

    private static MemDiagnosticEvent Event(
        string severity,
        string? incidentId,
        string eventCode,
        DateTimeOffset timestamp) =>
        new(
            SchemaVersion: 1,
            EventId: $"evt_{Guid.NewGuid():N}",
            TimestampUtc: timestamp,
            Severity: severity,
            EventCode: eventCode,
            Source: "test",
            Feature: eventCode.Split('.')[0],
            Stage: "test-stage",
            Message: $"Safe summary for {eventCode}.",
            IncidentId: incidentId,
            TraceId: null,
            SpanId: null,
            RequestId: null,
            CorrelationId: null,
            OperationId: null,
            Resource: new MemDiagnosticResource(
                Kind: "runtime",
                Id: "private-resource-id",
                DisplayName: "Safe resource",
                WorkspacePath: "https://untrusted.example.test/private"),
            Expected: null,
            Observed: null,
            Details: null,
            Exception: null,
            SuggestedAction: null,
            Retryable: false,
            RedactionsApplied: true,
            Truncated: false);

    private sealed class StaticReader(IReadOnlyList<MemDiagnosticEvent> events)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MemDiagnosticEventPage(
                query.FromUtc ?? Now.AddHours(-24),
                query.UntilUtc ?? Now,
                query.PageSize ?? 200,
                WindowClamped: false,
                Events: events,
                NextCursor: null,
                Warnings: Array.Empty<string>()));
    }

    private sealed class ThrowingReader : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default) =>
            throw new IOException("diagnostic store unavailable");
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
