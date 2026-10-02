using System.Security.Claims;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsIncidentIntegrationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resolve_projects_through_detail_list_and_attention_without_erasing_events()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incidentId = "inc_01b_resolve";
        fixture.Reader.Add(Event(
            incidentId,
            "evt_01b_resolve_0001",
            Now.AddMinutes(-1),
            MemDiagnosticSeverities.Error));

        var result = await fixture.Actions.ResolveAsync(
            incidentId,
            new DiagnosticsIncidentResolveRequest(
                DiagnosticsIncidentResolutionCodes.Fixed),
            fixture.Principal,
            fixture.OperatorId,
            "corr_01b_resolve",
            CancellationToken.None);

        Assert.NotNull(result.Incident.Lifecycle);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Resolved,
            result.Incident.Lifecycle!.State);
        Assert.Equal(
            DiagnosticsIncidentResolutionCodes.Fixed,
            result.Incident.Lifecycle.ResolutionCode);
        Assert.Single(fixture.Reader.Events);

        var attention = await fixture.Attention.GetAsync(5, CancellationToken.None);
        Assert.Equal("ready", attention.State);
        Assert.Equal(0, attention.Total);

        var overview = await fixture.Overview.GetAsync(
            fixture.Principal,
            CancellationToken.None);
        Assert.Equal("ready", overview.Status);
        Assert.Equal(0, overview.Counts.IncidentCount);
        Assert.Equal(1, overview.Counts.Error);
        Assert.Null(overview.ActiveContext);

        var resolvedPage = await fixture.Incidents.QueryAsync(
            new MemDiagnosticQuery(PageSize: 100),
            DiagnosticsIncidentLifecycleStates.Resolved,
            fixture.Principal,
            CancellationToken.None);
        Assert.Single(resolvedPage.Incidents);
        Assert.Equal(incidentId, resolvedPage.Incidents[0].IncidentId);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Resolved,
            resolvedPage.Incidents[0].Lifecycle?.State);

        var openPage = await fixture.Incidents.QueryAsync(
            new MemDiagnosticQuery(PageSize: 100),
            DiagnosticsIncidentLifecycleStates.Open,
            fixture.Principal,
            CancellationToken.None);
        Assert.Empty(openPage.Incidents);

        var audit = await fixture.Db.MemOperatorAuditEvents
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("diagnostics.incident.resolved", audit.EventType);
        Assert.Equal("corr_01b_resolve", audit.CorrelationId);
    }

    [Fact]
    public async Task New_event_after_resolution_reopens_attention_immediately()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incidentId = "inc_01b_recurrence";
        fixture.Reader.Add(Event(
            incidentId,
            "evt_01b_recurrence_0001",
            Now.AddMinutes(-2),
            MemDiagnosticSeverities.Error));

        await fixture.Actions.ResolveAsync(
            incidentId,
            new DiagnosticsIncidentResolveRequest(
                DiagnosticsIncidentResolutionCodes.SelfRecovered),
            fixture.Principal,
            fixture.OperatorId,
            null,
            CancellationToken.None);

        fixture.Reader.Add(Event(
            incidentId,
            "evt_01b_recurrence_0002",
            Now.AddMinutes(-1),
            MemDiagnosticSeverities.Error));

        var detail = await fixture.Incidents.GetAsync(
            incidentId,
            fixture.Principal,
            CancellationToken.None);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Open,
            detail.Incident.Lifecycle?.State);
        Assert.True(detail.Incident.Lifecycle!.Reopened);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Resolved,
            detail.Incident.Lifecycle?.StoredDisposition);

        var attention = await fixture.Attention.GetAsync(5, CancellationToken.None);
        Assert.Equal("attention", attention.State);
        Assert.Equal(1, attention.Total);
        Assert.Equal(incidentId, attention.Items.Single().IncidentId);

        var overview = await fixture.Overview.GetAsync(
            fixture.Principal,
            CancellationToken.None);
        Assert.Equal("attention", overview.Status);
        Assert.Equal(1, overview.Counts.IncidentCount);
        Assert.Equal(incidentId, overview.ActiveContext?.IncidentId);
    }

    [Fact]
    public async Task Acknowledge_snooze_and_manual_reopen_return_fresh_effective_projection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incidentId = "inc_01b_actions";
        fixture.Reader.Add(Event(
            incidentId,
            "evt_01b_actions_0001",
            Now.AddMinutes(-1),
            MemDiagnosticSeverities.Warning));

        var acknowledged = await fixture.Actions.AcknowledgeAsync(
            incidentId,
            fixture.Principal,
            fixture.OperatorId,
            null,
            CancellationToken.None);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Acknowledged,
            acknowledged.Incident.Lifecycle?.State);
        Assert.Equal(0, (await fixture.Attention.GetAsync(5, CancellationToken.None)).Total);

        var snoozedUntil = Now.AddHours(4);
        var snoozed = await fixture.Actions.SnoozeAsync(
            incidentId,
            new DiagnosticsIncidentSnoozeRequest(snoozedUntil),
            fixture.Principal,
            fixture.OperatorId,
            null,
            CancellationToken.None);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Snoozed,
            snoozed.Incident.Lifecycle?.State);
        Assert.Equal(snoozedUntil, snoozed.Incident.Lifecycle?.SnoozedUntilUtc);

        var reopened = await fixture.Actions.ReopenAsync(
            incidentId,
            fixture.Principal,
            fixture.OperatorId,
            null,
            CancellationToken.None);
        Assert.Equal(
            DiagnosticsIncidentLifecycleStates.Open,
            reopened.Incident.Lifecycle?.State);
        Assert.False(reopened.Incident.Lifecycle!.Reopened);
        Assert.Equal(1, (await fixture.Attention.GetAsync(5, CancellationToken.None)).Total);

        var auditTypes = await fixture.Db.MemOperatorAuditEvents
            .AsNoTracking()
            .Select(x => x.EventType)
            .ToArrayAsync();
        Assert.Contains("diagnostics.incident.acknowledged", auditTypes);
        Assert.Contains("diagnostics.incident.snoozed", auditTypes);
        Assert.Contains("diagnostics.incident.reopened", auditTypes);
    }

    [Fact]
    public async Task Invalid_action_inputs_are_returned_as_bounded_problem_contracts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var incidentId = "inc_01b_invalid_actions";
        fixture.Reader.Add(Event(
            incidentId,
            "evt_01b_invalid_actions_0001",
            Now.AddMinutes(-1),
            MemDiagnosticSeverities.Warning));

        var resolution = await Assert.ThrowsAsync<Shared.Exceptions.MemProblemException>(() =>
            fixture.Actions.ResolveAsync(
                incidentId,
                new DiagnosticsIncidentResolveRequest("operator typed free text"),
                fixture.Principal,
                fixture.OperatorId,
                null,
                CancellationToken.None));
        Assert.Equal(400, resolution.StatusCode);
        Assert.Equal("diagnostics_incident_action_invalid", resolution.Code);

        var snooze = await Assert.ThrowsAsync<Shared.Exceptions.MemProblemException>(() =>
            fixture.Actions.SnoozeAsync(
                incidentId,
                new DiagnosticsIncidentSnoozeRequest(Now.AddDays(31)),
                fixture.Principal,
                fixture.OperatorId,
                null,
                CancellationToken.None));
        Assert.Equal(400, snooze.StatusCode);
        Assert.Equal("diagnostics_incident_action_invalid", snooze.Code);

        Assert.Empty(await fixture.Db.DiagnosticsIncidentDispositions.ToArrayAsync());
    }

    [Fact]
    public async Task Incident_list_is_not_displaced_by_newer_non_incident_technical_events()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string incidentId = "inc_01c_volume_regression";
        fixture.Reader.Add(Event(
            incidentId,
            "evt_01c_volume_incident",
            Now.AddMinutes(-10),
            MemDiagnosticSeverities.Error));

        for (var index = 0; index < 235; index++)
        {
            fixture.Reader.Add(Event(
                incidentId: null,
                eventId: $"evt_01c_technical_{index:0000}",
                timestampUtc: Now.AddMinutes(-5).AddMilliseconds(index),
                severity: MemDiagnosticSeverities.Information));
        }

        var page = await fixture.Incidents.QueryAsync(
            new MemDiagnosticQuery(PageSize: 50),
            DiagnosticsIncidentLifecycleStates.Open,
            fixture.Principal,
            CancellationToken.None);

        var incident = Assert.Single(page.Incidents);
        Assert.Equal(incidentId, incident.IncidentId);
        Assert.True(fixture.Reader.LastQuery?.RequireIncidentId == true);
        Assert.False(page.Partial);
    }

    [Fact]
    public async Task Incident_cursor_pagination_does_not_claim_evidence_is_partial()
    {
        await using var fixture = await Fixture.CreateAsync();
        const string incidentId = "inc_01c_cursor_regression";

        for (var index = 0; index < 60; index++)
        {
            fixture.Reader.Add(Event(
                incidentId,
                $"evt_01c_cursor_{index:0000}",
                Now.AddMinutes(-5).AddMilliseconds(index),
                MemDiagnosticSeverities.Warning));
        }

        var page = await fixture.Incidents.QueryAsync(
            new MemDiagnosticQuery(PageSize: 50),
            DiagnosticsIncidentLifecycleStates.Open,
            fixture.Principal,
            CancellationToken.None);

        Assert.Single(page.Incidents);
        Assert.NotNull(page.NextCursor);
        Assert.False(page.Partial);
        Assert.Empty(page.Warnings);
    }

    [Fact]
    public void Lifecycle_query_filter_rejects_unknown_state()
    {
        var parser = new DiagnosticsQueryParser(
            new DiagnosticsApiOptions(),
            new MutableTimeProvider(Now));
        var query = new Microsoft.AspNetCore.Http.QueryCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["lifecycle"] = "deleted"
            });

        var exception = Assert.Throws<Shared.Exceptions.MemProblemException>(() =>
            parser.ParseLifecycleFilter(query));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("diagnostics_incident_lifecycle_invalid", exception.Code);
    }

    private static MemDiagnosticEvent Event(
        string? incidentId,
        string eventId,
        DateTimeOffset timestampUtc,
        string severity) =>
        new(
            SchemaVersion: 1,
            EventId: eventId,
            TimestampUtc: timestampUtc,
            Severity: severity,
            EventCode: "runtime.test_failure",
            Source: "test",
            Feature: "runtime",
            Stage: "test-stage",
            Message: "Safe diagnostic incident summary.",
            IncidentId: incidentId,
            TraceId: null,
            SpanId: null,
            RequestId: null,
            CorrelationId: null,
            OperationId: null,
            Resource: new MemDiagnosticResource(
                Kind: "runtime-operation",
                Id: "safe-runtime-resource",
                DisplayName: "Test runtime operation"),
            Expected: null,
            Observed: null,
            Details: null,
            Exception: null,
            SuggestedAction: null,
            Retryable: false,
            RedactionsApplied: true,
            Truncated: false);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private bool _disposed;

        private Fixture(
            string databasePath,
            MemDbContext db,
            MutableDiagnosticReader reader,
            Guid operatorId,
            ClaimsPrincipal principal,
            DiagnosticsIncidentService incidents,
            DiagnosticsIncidentActionService actions,
            DiagnosticsAttentionService attention,
            DiagnosticsOverviewService overview)
        {
            _databasePath = databasePath;
            Db = db;
            Reader = reader;
            OperatorId = operatorId;
            Principal = principal;
            Incidents = incidents;
            Actions = actions;
            Attention = attention;
            Overview = overview;
        }

        public MemDbContext Db { get; }
        public MutableDiagnosticReader Reader { get; }
        public Guid OperatorId { get; }
        public ClaimsPrincipal Principal { get; }
        public DiagnosticsIncidentService Incidents { get; }
        public DiagnosticsIncidentActionService Actions { get; }
        public DiagnosticsAttentionService Attention { get; }
        public DiagnosticsOverviewService Overview { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-diagnostics-incident-01b-{Guid.NewGuid():N}.db");
            var dbOptions = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(dbOptions);
            await db.Database.EnsureCreatedAsync();

            var time = new MutableTimeProvider(Now);
            var reader = new MutableDiagnosticReader(time);
            var apiOptions = new DiagnosticsApiOptions();
            var collector = new DiagnosticsEventCollector(reader, apiOptions, time);
            var projector = new DiagnosticsIncidentLifecycleProjector(db, time);
            var lifecycleReader = new DiagnosticsIncidentLifecycleReader(
                projector,
                NullLogger<DiagnosticsIncidentLifecycleReader>.Instance);
            var audit = new MemOperatorAuditService(db, time);
            var lifecycle = new DiagnosticsIncidentLifecycleService(db, audit, time);
            var incidents = new DiagnosticsIncidentService(
                reader,
                collector,
                new DiagnosticsCapabilityService(),
                new DiagnosticsWorkspaceLinkBuilder(),
                new DiagnosticsQueryParser(apiOptions, time),
                apiOptions,
                db,
                NullLogger<DiagnosticsIncidentService>.Instance,
                lifecycleReader);
            var actions = new DiagnosticsIncidentActionService(incidents, lifecycle);
            var attention = new DiagnosticsAttentionService(
                collector,
                time,
                NullLogger<DiagnosticsAttentionService>.Instance,
                lifecycleReader);
            var capabilityService = new DiagnosticsCapabilityService();
            var activeContext = new DiagnosticsActiveContextService(
                db,
                new DiagnosticsWorkspaceLinkBuilder(),
                time,
                NullLogger<DiagnosticsActiveContextService>.Instance,
                lifecycleReader);
            var loggingHealth = new DiagnosticsLoggingHealthService(
                new ReadyLocalHealth(),
                new ReadyStoreHealth(),
                new SeqDiagnosticsOptions(),
                new ReadySeqHealth(),
                capabilityService,
                time);
            var overview = new DiagnosticsOverviewService(
                collector,
                loggingHealth,
                capabilityService,
                activeContext,
                time,
                lifecycleReader);
            var operatorId = Guid.NewGuid();
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, operatorId.ToString("D")),
                new Claim(ClaimTypes.Role, MemOperatorRoles.Operator)
            ], "test"));

            return new Fixture(
                databasePath,
                db,
                reader,
                operatorId,
                principal,
                incidents,
                actions,
                attention,
                overview);
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                await Db.DisposeAsync();
                _disposed = true;
            }

            foreach (var path in new[]
                     {
                         _databasePath,
                         _databasePath + "-shm",
                         _databasePath + "-wal"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class MutableDiagnosticReader(MutableTimeProvider time)
        : IMemDiagnosticEventReader
    {
        private readonly List<MemDiagnosticEvent> _events = [];

        public IReadOnlyList<MemDiagnosticEvent> Events => _events;

        public MemDiagnosticQuery? LastQuery { get; private set; }

        public void Add(MemDiagnosticEvent @event) => _events.Add(@event);

        public Task<MemDiagnosticEventPage> QueryAsync(
            MemDiagnosticQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var from = query.FromUtc ?? time.GetUtcNow().AddDays(-14);
            var until = query.UntilUtc ?? time.GetUtcNow();
            var pageSize = query.PageSize ?? 200;
            var matched = _events
                .Where(@event => @event.TimestampUtc >= from && @event.TimestampUtc <= until)
                .Where(@event => !query.RequireIncidentId || !string.IsNullOrWhiteSpace(@event.IncidentId))
                .Where(@event => query.IncidentId is null || string.Equals(
                    @event.IncidentId,
                    query.IncidentId,
                    StringComparison.Ordinal))
                .Where(@event => query.EventId is null || string.Equals(
                    @event.EventId,
                    query.EventId,
                    StringComparison.Ordinal))
                .OrderByDescending(@event => @event.TimestampUtc)
                .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
                .ToArray();
            var filtered = matched.Take(pageSize).ToArray();

            return Task.FromResult(new MemDiagnosticEventPage(
                from,
                until,
                pageSize,
                WindowClamped: false,
                Events: filtered,
                NextCursor: matched.Length > pageSize ? "cursor_more_incident_events" : null,
                Warnings: Array.Empty<string>()));
        }
    }

    private sealed class ReadyLocalHealth : IMemLocalLogHealthReader
    {
        public MemLocalLogHealth GetHealth() => new(
            Enabled: true,
            Status: "ready",
            PersistentRecorderConfigured: true,
            PersistentRecorderActive: true,
            PersistentFilePath: null,
            LastFileWriteAtUtc: Now,
            RetainedFileCount: 1,
            RetainedBytes: 100,
            SerilogSelfLogMessageCount: 0,
            WarningCode: null);
    }

    private sealed class ReadyStoreHealth : IMemDiagnosticHealthReader
    {
        public MemDiagnosticStoreHealth GetHealth() => new(
            Enabled: true,
            Status: "ready",
            LastWriteAtUtc: Now,
            LastReadAtUtc: Now,
            StoredEventCount: 1,
            DroppedEventCount: 0,
            MalformedLineCount: 0,
            LastWriteErrorCode: null,
            LastReadWarningCode: null,
            LastRetentionRunAtUtc: Now,
            LastRetentionDeletedFileCount: 0,
            LastRetentionDeletedBytes: 0,
            LastRetentionErrorCode: null);
    }

    private sealed class ReadySeqHealth : ISeqHealthReader
    {
        public SeqHealthSnapshot GetHealth() => new(
            Status: "optional-disabled",
            SinkConfigured: false,
            Reachable: false,
            LastCheckedAtUtc: null,
            LastSuccessAtUtc: null,
            WarningCode: null);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
