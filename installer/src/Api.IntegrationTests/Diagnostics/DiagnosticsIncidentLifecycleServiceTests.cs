using Infrastructure.Data.Entities.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Services.Identity;
using Modules.Operator.Diagnostics.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsIncidentLifecycleServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task No_disposition_projects_as_open()
    {
        await using var fixture = await Fixture.CreateAsync();

        var projection = await fixture.Projector.ProjectAsync(
            Watermark("inc_open", Now, "evt_0001"),
            CancellationToken.None);

        Assert.Equal(DiagnosticsIncidentLifecycleStates.Open, projection.State);
        Assert.False(projection.Reopened);
        Assert.Null(projection.StoredDisposition);
    }

    [Fact]
    public async Task Acknowledge_persists_watermark_revision_and_structured_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = Guid.NewGuid();
        var watermark = Watermark("inc_ack", Now, "evt_0001");

        await fixture.Lifecycle.AcknowledgeAsync(
            watermark,
            actor,
            "corr_ack_01",
            CancellationToken.None);

        var entity = await fixture.Db.DiagnosticsIncidentDispositions
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Acknowledged, entity.Disposition);
        Assert.Equal(watermark.ObservedThroughAtUtc, entity.ObservedThroughAtUtc);
        Assert.Equal(watermark.ObservedThroughEventId, entity.ObservedThroughEventId);
        Assert.Equal(actor, entity.UpdatedByOperatorId);
        Assert.Equal(1, entity.Revision);

        var projection = await fixture.Projector.ProjectAsync(
            watermark,
            CancellationToken.None);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Acknowledged, projection.State);
        Assert.False(projection.Reopened);

        var audit = await fixture.Db.MemOperatorAuditEvents
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("diagnostics.incident.acknowledged", audit.EventType);
        Assert.Equal("succeeded", audit.Outcome);
        Assert.Equal(actor, audit.ActorOperatorId);
        Assert.Equal("corr_ack_01", audit.CorrelationId);
        Assert.Equal("inc_ack", audit.ReasonCode);
    }

    [Fact]
    public async Task Reapplying_a_disposition_advances_the_optimistic_concurrency_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = Guid.NewGuid();
        var watermark = Watermark("inc_revision", Now, "evt_0001");

        await fixture.Lifecycle.AcknowledgeAsync(
            watermark,
            actor,
            null,
            CancellationToken.None);
        var acknowledgedRevision = await fixture.Db.DiagnosticsIncidentDispositions
            .AsNoTracking()
            .Select(x => x.Revision)
            .SingleAsync();

        await fixture.Lifecycle.ResolveAsync(
            watermark,
            DiagnosticsIncidentResolutionCodes.Fixed,
            actor,
            null,
            CancellationToken.None);
        var resolvedRevision = await fixture.Db.DiagnosticsIncidentDispositions
            .AsNoTracking()
            .Select(x => x.Revision)
            .SingleAsync();

        Assert.Equal(1, acknowledgedRevision);
        Assert.Equal(2, resolvedRevision);

        var revisionProperty = fixture.Db.Model
            .FindEntityType(typeof(DiagnosticsIncidentDispositionEntity))!
            .FindProperty(nameof(DiagnosticsIncidentDispositionEntity.Revision))!;
        Assert.True(revisionProperty.IsConcurrencyToken);
    }

    [Fact]
    public async Task Snooze_is_effective_until_expiry_then_returns_to_open_without_recurrence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = Guid.NewGuid();
        var watermark = Watermark("inc_snooze", Now, "evt_0001");

        await fixture.Lifecycle.SnoozeAsync(
            watermark,
            Now.AddHours(1),
            actor,
            null,
            CancellationToken.None);

        var snoozed = await fixture.Projector.ProjectAsync(
            watermark,
            CancellationToken.None);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Snoozed, snoozed.State);
        Assert.False(snoozed.Reopened);

        fixture.Time.SetUtcNow(Now.AddHours(2));
        var expired = await fixture.Projector.ProjectAsync(
            watermark,
            CancellationToken.None);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Open, expired.State);
        Assert.False(expired.Reopened);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Snoozed, expired.StoredDisposition);
    }

    [Theory]
    [InlineData(DiagnosticsIncidentLifecycleStates.Acknowledged)]
    [InlineData(DiagnosticsIncidentLifecycleStates.Snoozed)]
    [InlineData(DiagnosticsIncidentLifecycleStates.Resolved)]
    public async Task New_event_reopens_every_non_open_disposition(string disposition)
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.DiagnosticsIncidentDispositions.Add(
            new DiagnosticsIncidentDispositionEntity
            {
                IncidentId = "inc_recurrence_all",
                Disposition = disposition,
                UpdatedAtUtc = Now,
                UpdatedByOperatorId = Guid.NewGuid(),
                ObservedThroughAtUtc = Now,
                ObservedThroughEventId = "evt_0001",
                SnoozedUntilUtc = disposition == DiagnosticsIncidentLifecycleStates.Snoozed
                    ? Now.AddHours(4)
                    : null,
                ResolutionCode = disposition == DiagnosticsIncidentLifecycleStates.Resolved
                    ? DiagnosticsIncidentResolutionCodes.Fixed
                    : null,
                Revision = 1
            });
        await fixture.Db.SaveChangesAsync();

        var projection = await fixture.Projector.ProjectAsync(
            Watermark("inc_recurrence_all", Now.AddSeconds(1), "evt_0002"),
            CancellationToken.None);

        Assert.Equal(DiagnosticsIncidentLifecycleStates.Open, projection.State);
        Assert.True(projection.Reopened);
        Assert.Equal(disposition, projection.StoredDisposition);
    }

    [Fact]
    public async Task New_event_beyond_resolution_watermark_reopens_effective_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = Guid.NewGuid();

        await fixture.Lifecycle.ResolveAsync(
            Watermark("inc_recur", Now, "evt_0001"),
            DiagnosticsIncidentResolutionCodes.Fixed,
            actor,
            null,
            CancellationToken.None);

        var projection = await fixture.Projector.ProjectAsync(
            Watermark("inc_recur", Now.AddSeconds(1), "evt_0002"),
            CancellationToken.None);

        Assert.Equal(DiagnosticsIncidentLifecycleStates.Open, projection.State);
        Assert.True(projection.Reopened);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Resolved, projection.StoredDisposition);
        Assert.Equal(DiagnosticsIncidentResolutionCodes.Fixed, projection.ResolutionCode);
    }

    [Fact]
    public async Task System_self_recovery_resolves_without_inventing_an_operator_or_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var watermark = Watermark("inc_system_recovered", Now, "evt_recovered_0001");

        await fixture.Lifecycle.ResolveSelfRecoveredAsync(
            watermark.IncidentId,
            watermark.ObservedThroughAtUtc,
            watermark.ObservedThroughEventId,
            CancellationToken.None);

        var entity = await fixture.Db.DiagnosticsIncidentDispositions
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Resolved, entity.Disposition);
        Assert.Equal(Guid.Empty, entity.UpdatedByOperatorId);
        Assert.Equal(DiagnosticsIncidentResolutionCodes.SelfRecovered, entity.ResolutionCode);
        Assert.Empty(await fixture.Db.MemOperatorAuditEvents.ToArrayAsync());

        var projection = await fixture.Projector.ProjectAsync(
            watermark,
            CancellationToken.None);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Resolved, projection.State);
        Assert.False(projection.Reopened);
        Assert.Null(projection.UpdatedByOperatorId);
        Assert.Equal(DiagnosticsIncidentResolutionCodes.SelfRecovered, projection.ResolutionCode);
    }

    [Fact]
    public async Task New_event_reopens_a_system_self_recovered_incident_without_exposing_system_actor()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Lifecycle.ResolveSelfRecoveredAsync(
            "inc_system_recur",
            Now,
            "evt_recovered_0001",
            CancellationToken.None);

        var projection = await fixture.Projector.ProjectAsync(
            Watermark("inc_system_recur", Now.AddSeconds(1), "evt_failed_0002"),
            CancellationToken.None);

        Assert.Equal(DiagnosticsIncidentLifecycleStates.Open, projection.State);
        Assert.True(projection.Reopened);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Resolved, projection.StoredDisposition);
        Assert.Null(projection.UpdatedByOperatorId);
        Assert.Equal(DiagnosticsIncidentResolutionCodes.SelfRecovered, projection.ResolutionCode);
    }

    [Fact]
    public async Task Resolve_requires_a_supported_structured_reason_code()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Lifecycle.ResolveAsync(
                Watermark("inc_reason", Now, "evt_0001"),
                "operator typed free text",
                Guid.NewGuid(),
                null,
                CancellationToken.None));

        Assert.Empty(await fixture.Db.DiagnosticsIncidentDispositions.ToArrayAsync());
        Assert.Empty(await fixture.Db.MemOperatorAuditEvents.ToArrayAsync());
    }

    [Fact]
    public async Task Reopen_removes_disposition_and_retains_an_audit_record()
    {
        await using var fixture = await Fixture.CreateAsync();
        var actor = Guid.NewGuid();
        var watermark = Watermark("inc_manual_reopen", Now, "evt_0001");

        await fixture.Lifecycle.ResolveAsync(
            watermark,
            DiagnosticsIncidentResolutionCodes.SelfRecovered,
            actor,
            null,
            CancellationToken.None);

        var changed = await fixture.Lifecycle.ReopenAsync(
            watermark.IncidentId,
            actor,
            "corr_reopen_01",
            CancellationToken.None);

        Assert.True(changed);
        Assert.Empty(await fixture.Db.DiagnosticsIncidentDispositions.ToArrayAsync());

        var projection = await fixture.Projector.ProjectAsync(
            watermark,
            CancellationToken.None);
        Assert.Equal(DiagnosticsIncidentLifecycleStates.Open, projection.State);
        Assert.False(projection.Reopened);
        Assert.Null(projection.StoredDisposition);

        var audits = await fixture.Db.MemOperatorAuditEvents
            .AsNoTracking()
            .ToArrayAsync();
        Assert.Equal(2, audits.Length);
        Assert.Contains(audits, x =>
            x.EventType == "diagnostics.incident.resolved" &&
            x.ReasonCode == "inc_manual_reopen:self_recovered");
        Assert.Contains(audits, x =>
            x.EventType == "diagnostics.incident.reopened" &&
            x.ReasonCode == "inc_manual_reopen" &&
            x.CorrelationId == "corr_reopen_01");
    }

    [Fact]
    public async Task Snooze_is_bounded_to_thirty_days()
    {
        await using var fixture = await Fixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Lifecycle.SnoozeAsync(
                Watermark("inc_snooze_bound", Now, "evt_0001"),
                Now.AddDays(30).AddSeconds(1),
                Guid.NewGuid(),
                null,
                CancellationToken.None));
    }

    private static DiagnosticsIncidentEventWatermark Watermark(
        string incidentId,
        DateTimeOffset observedAtUtc,
        string eventId) =>
        new(incidentId, observedAtUtc, eventId);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private bool _disposed;

        private Fixture(
            string databasePath,
            MemDbContext db,
            MutableTimeProvider time,
            DiagnosticsIncidentLifecycleService lifecycle,
            DiagnosticsIncidentLifecycleProjector projector)
        {
            _databasePath = databasePath;
            Db = db;
            Time = time;
            Lifecycle = lifecycle;
            Projector = projector;
        }

        public MemDbContext Db { get; }
        public MutableTimeProvider Time { get; }
        public DiagnosticsIncidentLifecycleService Lifecycle { get; }
        public DiagnosticsIncidentLifecycleProjector Projector { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-diagnostics-incident-lifecycle-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var time = new MutableTimeProvider(Now);
            var audit = new MemOperatorAuditService(db, time);
            var lifecycle = new DiagnosticsIncidentLifecycleService(db, audit, time);
            var projector = new DiagnosticsIncidentLifecycleProjector(db, time);
            return new Fixture(databasePath, db, time, lifecycle, projector);
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

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
