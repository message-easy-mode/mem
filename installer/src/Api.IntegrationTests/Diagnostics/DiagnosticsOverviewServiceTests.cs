using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsOverviewServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Warning_technical_activity_without_an_incident_does_not_create_attention()
    {
        var events = new[]
        {
            Event("warning", incidentId: null),
            Event("information", incidentId: null)
        };

        var attentionEvents = events
            .Where(DiagnosticsOverviewService.IsAttentionEvent)
            .ToArray();
        var status = DiagnosticsOverviewService.DetermineStatus(
            attentionEvents,
            partial: false);

        Assert.Empty(attentionEvents);
        Assert.Equal("ready", status);
    }

    [Theory]
    [InlineData("warning", "warning")]
    [InlineData("error", "attention")]
    [InlineData("critical", "attention")]
    public void Incident_severity_drives_attention_status(
        string severity,
        string expected)
    {
        var status = DiagnosticsOverviewService.DetermineStatus(
            [Event(severity, "inc_status_test")],
            partial: false);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void Partial_projection_without_attention_is_degraded()
    {
        var status = DiagnosticsOverviewService.DetermineStatus(
            Array.Empty<MemDiagnosticEvent>(),
            partial: true);

        Assert.Equal("degraded", status);
    }

    private static MemDiagnosticEvent Event(string severity, string? incidentId) =>
        new(
            SchemaVersion: 1,
            EventId: $"evt_{Guid.NewGuid():N}",
            TimestampUtc: Now,
            Severity: severity,
            EventCode: "diagnostics.status_test",
            Source: "test",
            Feature: "diagnostics",
            Stage: null,
            Message: "Safe diagnostics status test.",
            IncidentId: incidentId,
            TraceId: null,
            SpanId: null,
            RequestId: null,
            CorrelationId: null,
            OperationId: null,
            Resource: null,
            Expected: null,
            Observed: null,
            Details: null,
            Exception: null,
            SuggestedAction: null,
            Retryable: false,
            RedactionsApplied: true,
            Truncated: false);
}
