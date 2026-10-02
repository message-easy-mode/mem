using HostAgent.Runtime.Diagnostics;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticEventFactoryTests
{
    [Fact]
    public void Factory_creates_versioned_correlated_incident_and_redacts_exact_secrets()
    {
        using var fixture = new MemDiagnosticTestFixture();
        const string secret = "exact-runtime-secret";
        var operationId = Guid.NewGuid();

        var @event = fixture.Factory.Create(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Error,
            EventCode: "migration.staging.failed",
            Source: "host-agent",
            Feature: "migration",
            Message: $"Container returned {secret}",
            Stage: "private-staging",
            CreateIncident: true,
            OperationId: operationId,
            Resource: new MemDiagnosticResource(
                "migration",
                "mig_123",
                StackId: "stack-123",
                Service: "synapse"),
            Details: new Dictionary<string, string?>
            {
                ["safe"] = $"value {secret}",
                ["password"] = "another-secret"
            },
            Context: new MemDiagnosticContext(
                TraceId: "trace-123",
                SpanId: "span-123",
                RequestId: "request-123",
                CorrelationId: "correlation-123"),
            ExactSecrets: [secret]));

        Assert.Equal(1, @event.SchemaVersion);
        Assert.StartsWith("evt_", @event.EventId, StringComparison.Ordinal);
        Assert.StartsWith("inc_", @event.IncidentId!, StringComparison.Ordinal);
        Assert.Equal(operationId, @event.OperationId);
        Assert.Equal("trace-123", @event.TraceId);
        Assert.Equal("correlation-123", @event.CorrelationId);
        Assert.DoesNotContain(secret, @event.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, @event.Details!["safe"], StringComparison.Ordinal);
        Assert.Equal("[redacted]", @event.Details["password"]);
        Assert.True(@event.RedactionsApplied);
    }

    [Fact]
    public async Task Invalid_write_request_degrades_to_a_minimal_safe_event_without_throwing()
    {
        using var fixture = new MemDiagnosticTestFixture();

        var result = await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: "not-a-severity",
            EventCode: "diagnostics.test.invalid_projection",
            Source: "host-agent",
            Feature: "diagnostics",
            Message: "Invalid projection"));

        Assert.True(result.Stored);
        var page = await fixture.Reader.QueryAsync(new MemDiagnosticQuery());
        var @event = Assert.Single(page.Events);
        Assert.Equal(MemDiagnosticSeverities.Error, @event.Severity);
        Assert.Equal("diagnostics", @event.Feature);
        Assert.True(@event.Truncated);
    }
}
