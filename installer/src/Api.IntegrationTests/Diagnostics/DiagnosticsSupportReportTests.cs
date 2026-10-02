using System.Security.Claims;
using System.Text.Json;
using Api.IntegrationTests.Runtime;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSupportReportTests
{
    private const string PrivateMarker = "support-report-private-marker";

    [Fact]
    public async Task Support_report_contains_only_correlated_safe_evidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(dbOptions);
        await db.Database.EnsureCreatedAsync();

        var operationId = Guid.NewGuid();
        db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = operationId,
            Operation = "migration-private-staging",
            Status = "failed",
            RequestedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = DateTime.UtcNow,
            InputJson = $"{{\"password\":\"{PrivateMarker}\"}}",
            ResultJson = $"{{\"secret\":\"{PrivateMarker}\"}}",
            EvidenceJson = $"{{\"path\":\"/srv/{PrivateMarker}\"}}",
            LastError = PrivateMarker,
            CurrentStep = PrivateMarker
        });
        await db.SaveChangesAsync();

        var incidentId = $"inc_{Guid.NewGuid():N}";
        var events = new[]
        {
            Event(incidentId, operationId, "evt_11111111111111111111111111111111", "api.docker.operation_failed"),
            Event($"inc_{Guid.NewGuid():N}", null, "evt_22222222222222222222222222222222", "unrelated.failure")
        };
        var reader = new FilteringReader(events);
        var lifecycleStore = new DiagnosticsIncidentLifecycleService(
            db,
            new MemOperatorAuditService(db, TimeProvider.System),
            TimeProvider.System);
        await lifecycleStore.ResolveAsync(
            new DiagnosticsIncidentEventWatermark(
                incidentId,
                events[0].TimestampUtc,
                events[0].EventId),
            DiagnosticsIncidentResolutionCodes.Fixed,
            Guid.NewGuid(),
            null,
            CancellationToken.None);
        var apiOptions = new DiagnosticsApiOptions();
        var capabilityService = new DiagnosticsCapabilityService();
        var queryParser = new DiagnosticsQueryParser(apiOptions, TimeProvider.System);
        var collector = new DiagnosticsEventCollector(reader, apiOptions, TimeProvider.System);
        var lifecycleReader = new DiagnosticsIncidentLifecycleReader(
            new DiagnosticsIncidentLifecycleProjector(db, TimeProvider.System),
            NullLogger<DiagnosticsIncidentLifecycleReader>.Instance);
        var incidentService = new DiagnosticsIncidentService(
            reader,
            collector,
            capabilityService,
            new DiagnosticsWorkspaceLinkBuilder(),
            queryParser,
            apiOptions,
            db,
            NullLogger<DiagnosticsIncidentService>.Instance,
            lifecycleReader);
        var loggingHealth = new DiagnosticsLoggingHealthService(
            new ReadyLocalHealth(),
            new ReadyStoreHealth(),
            new SeqDiagnosticsOptions(),
            new FixedSeqHealthReader(),
            capabilityService,
            TimeProvider.System);
        var audit = new CapturingAudit();
        var runtimeRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-support-report-runtime-{Guid.NewGuid():N}");
        var runtimeContext = TestRuntimeContext.Create(
            runtimeRoot,
            version: "0.2.0-test");
        var service = new DiagnosticsSupportReportService(
            incidentService,
            loggingHealth,
            new AvailableDockerEvidenceReader(),
            apiOptions,
            new DiagnosticsSupportReportSizeLimiter(apiOptions),
            runtimeContext,
            audit,
            NullLogger<DiagnosticsSupportReportService>.Instance,
            TimeProvider.System,
            exposureInspector: new FixedExposureInspector(
                new MemControlPlaneExposureProjection(
                    MemControlPlaneExposureStates.Private,
                    MemControlPlaneAccessModes.SshTunnel,
                    "127.0.0.1",
                    8443,
                    BindingCount: 1,
                    WarningCode: null)));
        var principal = Principal(MemOperatorRoles.PlatformOwner);

        var report = await service.GenerateAsync(
            new DiagnosticsSupportReportRequest(
                incidentId,
                IncludeDockerEvidence: true),
            principal,
            Guid.NewGuid(),
            "corr-test",
            CancellationToken.None);
        var json = JsonSerializer.Serialize(report);

        Assert.Equal("0.2.0-test", report.MemVersion);
        Assert.Equal(runtimeContext.RuntimeMode, report.RuntimeContext.RuntimeMode);
        Assert.Equal(
            runtimeContext.ControlPlaneInstanceId,
            report.RuntimeContext.ControlPlaneInstanceId);
        Assert.True(report.RuntimeContext.ControlPlaneExposure.IsPrivate);
        Assert.Equal("ssh-tunnel", report.RuntimeContext.ControlPlaneExposure.AccessMode);
        Assert.Equal("127.0.0.1", report.RuntimeContext.ControlPlaneExposure.HostAddress);
        Assert.Equal(8443, report.RuntimeContext.ControlPlaneExposure.HostPort);
        Assert.DoesNotContain(runtimeContext.StateRootPath, json, StringComparison.Ordinal);
        Assert.Equal(incidentId, report.Incident.IncidentId);
        Assert.NotNull(report.Incident.Lifecycle);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Resolved,
            report.Incident.Lifecycle!.State);
        Assert.Equal(
            DiagnosticsIncidentResolutionCodes.Fixed,
            report.Incident.Lifecycle.ResolutionCode);
        Assert.Single(report.Events);
        Assert.Single(report.Operations);
        Assert.Equal(operationId, report.Operations[0].OperationId);
        Assert.NotNull(report.DockerEvidence);
        Assert.True(report.DockerEvidence!.Available);
        Assert.Contains(
            "sanitized container output",
            report.DockerEvidence.LogTail!.Content,
            StringComparison.Ordinal);
        Assert.Contains(
            MemDiagnosticCodes.DockerEvidenceSensitiveOperationalMetadata,
            report.Warnings);
        Assert.DoesNotContain(PrivateMarker, json, StringComparison.Ordinal);
        Assert.DoesNotContain("inputJson", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("resultJson", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evidenceJson", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspacePath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/data/private.clef", json, StringComparison.Ordinal);
        Assert.True(report.Redaction.RedactionsApplied);
        Assert.Equal("diagnostics.support-report.generated", audit.Last?.EventType);

        Directory.Delete(runtimeRoot, recursive: true);
    }

    private static MemDiagnosticEvent Event(
        string incidentId,
        Guid? operationId,
        string eventId,
        string eventCode) =>
        new(
            1,
            eventId,
            DateTimeOffset.UtcNow,
            MemDiagnosticSeverities.Error,
            eventCode,
            "control-plane-api",
            "migrations",
            "request",
            "The private staging container failed safely.",
            incidentId,
            "trace-1",
            "span-1",
            "request-1",
            "correlation-1",
            operationId,
            new MemDiagnosticResource(
                "migration",
                "migration-1",
                "Migration 1",
                null,
                null,
                "synapse",
                $"/srv/{PrivateMarker}"),
            null,
            null,
            null,
            null,
            "Review the incident.",
            false,
            true,
            false);

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

    private sealed class FilteringReader(IReadOnlyList<MemDiagnosticEvent> events)
        : IMemDiagnosticEventReader
    {
        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            var filtered = events
                .Where(@event => query.IncidentId is null || string.Equals(
                    @event.IncidentId,
                    query.IncidentId,
                    StringComparison.Ordinal))
                .Where(@event => query.EventId is null || string.Equals(
                    @event.EventId,
                    query.EventId,
                    StringComparison.Ordinal))
                .Take(query.PageSize ?? 100)
                .ToArray();
            return Task.FromResult(new MemDiagnosticEventPage(
                query.FromUtc ?? DateTimeOffset.UtcNow.AddHours(-1),
                query.UntilUtc ?? DateTimeOffset.UtcNow,
                query.PageSize ?? 100,
                false,
                filtered,
                null,
                Array.Empty<string>()));
        }
    }

    private sealed class AvailableDockerEvidenceReader : IMemDockerEvidenceReader
    {
        public Task<MemDockerEvidenceReadResult> ReadForIncidentAsync(
            string incidentId,
            IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
            CancellationToken cancellationToken = default)
        {
            var resource = incidentEvents
                .First(@event => string.Equals(
                    @event.IncidentId,
                    incidentId,
                    StringComparison.Ordinal))
                .Resource!;
            return Task.FromResult(new MemDockerEvidenceReadResult(
                true,
                new MemDockerEvidence(
                    resource with { WorkspacePath = null },
                    DateTimeOffset.UtcNow,
                    new MemDockerEvidenceContainer(
                        "Migration private Synapse staging runtime",
                        "exited",
                        1,
                        "unhealthy",
                        DateTimeOffset.UtcNow.AddMinutes(-1),
                        DateTimeOffset.UtcNow,
                        0,
                        "sha256:image"),
                    new MemDockerEvidenceLogTail(
                        100,
                        1,
                        30000,
                        "sanitized container output",
                        false,
                        true),
                    [MemDiagnosticCodes.DockerEvidenceSensitiveOperationalMetadata]),
                null,
                [MemDiagnosticCodes.DockerEvidenceSensitiveOperationalMetadata]));
        }
    }

    private sealed class CapturingAudit : IMemOperatorAuditService
    {
        public MemOperatorAuditEventWrite? Last { get; private set; }

        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default)
        {
            Last = auditEvent;
            return Task.CompletedTask;
        }
    }

    private sealed class ReadyLocalHealth : IMemLocalLogHealthReader
    {
        public MemLocalLogHealth GetHealth() => new(
            true, "ready", true, true, "/data/private.clef",
            DateTimeOffset.UtcNow, 1, 100, 0, null);
    }

    private sealed class FixedSeqHealthReader : ISeqHealthReader
    {
        public SeqHealthSnapshot GetHealth() => new(
            Status: "optional-disabled",
            SinkConfigured: false,
            Reachable: false,
            LastCheckedAtUtc: null,
            LastSuccessAtUtc: null,
            WarningCode: null);
    }

    private sealed class ReadyStoreHealth : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() => new(
            true, "ready", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            1, 0, 0, null, null, DateTimeOffset.UtcNow, 0, 0, null);
    }
    private sealed class FixedExposureInspector(
        MemControlPlaneExposureProjection projection) : IControlPlaneExposureInspector
    {
        public Task<MemControlPlaneExposureProjection> InspectAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(projection);
    }

}
