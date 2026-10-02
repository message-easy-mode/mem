using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticEventLookupTests
{
    [Fact]
    public async Task Reader_filters_by_event_id_and_resource_kind()
    {
        using var fixture = new MemDiagnosticTestFixture();

        var first = await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Error,
            EventCode: "migration.failed",
            Source: "host-agent",
            Feature: "migrations",
            Message: "Migration failed.",
            CreateIncident: true,
            Resource: new MemDiagnosticResource(
                "migration",
                "migration-1")));

        await fixture.Writer.WriteAsync(new MemDiagnosticWriteRequest(
            Severity: MemDiagnosticSeverities.Warning,
            EventCode: "restore.warning",
            Source: "host-agent",
            Feature: "restore",
            Message: "Restore warning.",
            Resource: new MemDiagnosticResource(
                "restore",
                "restore-1")));

        var byEvent = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(
            EventId: first.EventId,
            PageSize: 10));
        var byResource = await fixture.Reader.QueryAsync(new MemDiagnosticQuery(
            ResourceKind: "restore",
            PageSize: 10));

        Assert.Single(byEvent.Events);
        Assert.Equal(first.EventId, byEvent.Events[0].EventId);
        Assert.Single(byResource.Events);
        Assert.Equal("restore", byResource.Events[0].Resource?.Kind);
    }
}
