using HostAgent.Runtime.Migrations.Staging.Retirement;
using HostAgent.Runtime.Services.TemporaryStaging;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HostAgent.Tests.Runtime.Migrations.Staging.Retirement;

public sealed class MigrationStagingRetirementTests
{
    [Fact]
    public async Task GET_review_is_read_only_and_confirmation_is_bound_to_current_state()
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = f.NewDb();
        var service = f.Service(db);
        var review = await service.ReviewAsync("mig_one", "mst_one", default);
        Assert.True(review.CanRetire);
        Assert.NotEmpty(review.ReviewFingerprint!);
        Assert.Empty(await db.MigrationStagingRetirements.ToListAsync());
        Assert.Equal(0, f.Runtime.RetireCalls);
        var error = await Assert.ThrowsAsync<MigrationStagingRetirementException>(() => service.AcceptAsync(
            "mig_one", "mst_one", f.Actor, new("bad-fingerprint", true), default));
        Assert.Equal("review-stale", error.Code);
        Assert.Empty(await db.MigrationStagingRetirements.ToListAsync());
        await Assert.ThrowsAsync<MigrationStagingRetirementException>(() => service.AcceptAsync(
            "mig_one", "mst_one", f.Actor, new(review.ReviewFingerprint!, false), default));
        Assert.Equal(0, f.Runtime.RetireCalls);
    }

    [Fact]
    public async Task Acceptance_is_durable_idempotent_and_does_not_execute_on_the_HTTP_lifetime()
    {
        await using var f = await Fixture.CreateAsync();
        using var request = new CancellationTokenSource();
        var accepted = await f.AcceptAsync(request.Token);
        request.Cancel();
        await using (var db = f.NewDb())
        {
            var duplicate = await f.Service(db).AcceptAsync("mig_one", "mst_one", f.Actor, new("old-review", true), default);
            Assert.Equal(accepted.OperationId, duplicate.OperationId);
            Assert.Equal("queued", duplicate.Status);
            Assert.Single(await db.MigrationStagingRetirements.ToListAsync());
            Assert.Equal("retiring", (await db.MigrationStagingRuns.SingleAsync()).Status);
            Assert.Equal(0, f.Runtime.RetireCalls);
        }
        await f.ExecuteAsync(accepted.OperationId);
        await using var completedDb = f.NewDb();
        var completed = await completedDb.MigrationStagingRetirements.SingleAsync();
        var run = await completedDb.MigrationStagingRuns.SingleAsync();
        Assert.Equal("retired", completed.Status);
        Assert.NotNull(completed.CompletedAtUtc);
        Assert.Equal("destroyed", run.Status);
        Assert.Null(run.ActiveMigrationKey);
        Assert.NotNull(run.DestroyedAtUtc);
        Assert.True(run.DatabaseImportSucceeded); // historical verification is not erased
        Assert.True(run.SynapseHealthPassed);
        Assert.Single(await completedDb.MigrationCandidateArtifacts.ToListAsync());
        Assert.Equal(MigrationSessionLifecycleStatuses.Active, (await completedDb.MigrationIntakes.SingleAsync()).LifecycleStatus);
        Assert.Equal(2, await completedDb.MemOperatorAuditEvents.CountAsync());
        Assert.Equal(1, f.Runtime.RetireCalls);
        await f.ExecuteAsync(accepted.OperationId);
        Assert.Equal(1, f.Runtime.RetireCalls);
    }

    [Fact]
    public async Task Partial_failure_keeps_reservation_and_requires_fresh_explicit_retry_of_same_operation()
    {
        await using var f = await Fixture.CreateAsync();
        var accepted = await f.AcceptAsync();
        f.Runtime.Failure = new IOException("SECRET-HOST-PATH must not be projected");
        await f.ExecuteAsync(accepted.OperationId);
        await using (var db = f.NewDb())
        {
            var op = await db.MigrationStagingRetirements.SingleAsync();
            var run = await db.MigrationStagingRuns.SingleAsync();
            Assert.Equal("needs-attention", op.Status);
            Assert.Equal("cleanup-incomplete", op.FailureCode);
            Assert.Equal("retirement-needs-attention", run.Status);
            Assert.Equal("mig_one", run.ActiveMigrationKey);
            Assert.Null(run.DestroyedAtUtc);
            var service = f.Service(db);
            var review = await service.ReviewAsync("mig_one", "mst_one", default);
            Assert.True(review.CanRetire);
            Assert.DoesNotContain("SECRET", System.Text.Json.JsonSerializer.Serialize(review));
            var error = await Assert.ThrowsAsync<MigrationStagingRetirementException>(() => service.AcceptAsync(
                "mig_one", "mst_one", f.Actor, new(review.ReviewFingerprint!, true), default));
            Assert.Equal("reviewed-retry-required", error.Code);
            var retried = await service.AcceptAsync("mig_one", "mst_one", f.Actor, new(review.ReviewFingerprint!, true, true), default);
            Assert.Equal(accepted.OperationId, retried.OperationId);
        }
        f.Runtime.Failure = null;
        await f.ExecuteAsync(accepted.OperationId);
        await using var final = f.NewDb();
        Assert.Equal("retired", (await final.MigrationStagingRetirements.SingleAsync()).Status);
        Assert.Equal(2, (await final.MigrationStagingRetirements.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Shutdown_retains_running_receipt_and_a_fresh_scope_resumes_it()
    {
        await using var f = await Fixture.CreateAsync();
        var accepted = await f.AcceptAsync();
        using var stopping = new CancellationTokenSource();
        f.Runtime.DuringRetirement = () => stopping.Cancel();
        await f.ExecuteAsync(accepted.OperationId, stopping.Token);
        await using (var db = f.NewDb())
        {
            var op = await db.MigrationStagingRetirements.SingleAsync();
            Assert.Equal("running", op.Status);
            Assert.Equal("control-plane-restarting", op.FailureCode);
            Assert.Null((await db.MigrationStagingRuns.SingleAsync()).DestroyedAtUtc);
        }
        f.Runtime.DuringRetirement = null;
        await f.ExecuteAsync(accepted.OperationId);
        await using var restarted = f.NewDb();
        Assert.Equal("retired", (await restarted.MigrationStagingRetirements.SingleAsync()).Status);
    }

    [Fact]
    public async Task Changed_membership_after_acceptance_blocks_worker_before_any_runtime_mutation()
    {
        await using var f = await Fixture.CreateAsync();
        var accepted = await f.AcceptAsync();
        f.Runtime.Plan = f.Runtime.Plan with { SynapseContainerId = new string('f', 64) };
        await f.ExecuteAsync(accepted.OperationId);
        Assert.Equal(0, f.Runtime.RetireCalls);
        await using var db = f.NewDb();
        var op = await db.MigrationStagingRetirements.SingleAsync();
        Assert.Equal("needs-attention", op.Status);
        Assert.Equal("ownership-changed", op.FailureCode);
        Assert.Equal("mig_one", (await db.MigrationStagingRuns.SingleAsync()).ActiveMigrationKey);
    }

    [Fact]
    public async Task Runtime_inspection_failure_is_not_absence_and_cannot_be_accepted()
    {
        await using var f = await Fixture.CreateAsync();
        f.Runtime.InspectionFailure = new IOException("secret token host path");
        await using var db = f.NewDb();
        var review = await f.Service(db).ReviewAsync("mig_one", "mst_one", default);
        Assert.False(review.CanRetire);
        Assert.Null(review.ReviewFingerprint);
        Assert.Equal("inspection-unavailable", review.BlockerCode);
        Assert.Equal(0, f.Runtime.RetireCalls);
        Assert.Empty(await db.MigrationStagingRetirements.ToListAsync());
    }

    [Theory]
    [InlineData("ownership")]
    [InlineData("containers")]
    [InlineData("networks")]
    [InlineData("history")]
    [InlineData("wrong-owner")]
    public async Task Incomplete_or_ambiguous_inventory_never_grants_retirement(string unavailable)
    {
        await using var f = await Fixture.CreateAsync();
        f.Source.Unavailable = unavailable;
        await using var db = f.NewDb();
        Assert.False((await f.Service(db).ReviewAsync("mig_one", "mst_one", default)).CanRetire);
        Assert.Equal(0, f.Runtime.RetireCalls);
    }

    [Fact]
    public async Task Root_and_other_child_writes_are_blocked_while_retirement_is_reserved_but_unrelated_sessions_are_not()
    {
        await using var f = await Fixture.CreateAsync();
        var accepted = await f.AcceptAsync();
        await using (var db = f.NewDb())
        {
            var root = await db.MigrationIntakes.SingleAsync();
            root.DisplayName = "competing write";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = f.NewDb())
        {
            var candidate = await db.MigrationCandidateArtifacts.SingleAsync();
            candidate.RetentionState = "retired";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = f.NewDb())
        {
            db.MigrationIntakes.Add(new MigrationIntakeEntity { Id = Guid.NewGuid(), IntakeId = "mig_other", DisplayName = "Unrelated", CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await f.ExecuteAsync(accepted.OperationId);
        await using var final = f.NewDb();
        var active = await final.MigrationIntakes.SingleAsync(x => x.IntakeId == "mig_one");
        active.DisplayName = "continued after retirement";
        await final.SaveChangesAsync();
        Assert.Equal(2, await final.MigrationIntakes.CountAsync());
    }

    [Fact]
    public async Task A_stale_review_cannot_claim_a_session_after_a_competing_lifecycle_write()
    {
        await using var f = await Fixture.CreateAsync();
        await using var first = f.NewDb();
        var service = f.Service(first);
        var review = await service.ReviewAsync("mig_one", "mst_one", default);
        await using (var other = f.NewDb())
        {
            (await other.MigrationIntakes.SingleAsync()).DisplayName = "Changed";
            await other.SaveChangesAsync();
        }
        var ex = await Assert.ThrowsAsync<MigrationStagingRetirementException>(() => service.AcceptAsync(
            "mig_one", "mst_one", f.Actor, new(review.ReviewFingerprint!, true), default));
        Assert.Equal("review-stale", ex.Code);
        await using var check = f.NewDb();
        Assert.Empty(await check.MigrationStagingRetirements.ToListAsync());
    }

    [Theory]
    [InlineData("adoption", "private-target-recovery-required")]
    [InlineData("completed", "completed-recovery-required")]
    [InlineData("public", "public-resource")]
    [InlineData("busy", "workflow-busy")]
    [InlineData("cancelled", "session-closed")]
    public async Task Lifecycle_admission_preserves_private_target_completion_public_and_cancellation_boundaries(string scenario, string expected)
    {
        await using var f = await Fixture.CreateAsync();
        await using var db = f.NewDb();
        var run = await db.MigrationStagingRuns.Include(x => x.MigrationIntake)
            .Include(x => x.CandidateArtifact).ThenInclude(x => x.ConversionAttempt).SingleAsync();
        if (scenario == "adoption") run.MigrationIntake.ProductionAdoption = new MigrationProductionAdoptionEntity();
        if (scenario == "completed") run.MigrationIntake.LifecycleStatus = "completed";
        if (scenario == "public") run.PublicRoutesCreated = true;
        if (scenario == "busy") run.Status = "running";
        if (scenario == "cancelled") run.MigrationIntake.CancelledAtUtc = DateTime.UtcNow;
        Assert.Equal(expected, MigrationStagingRetirementService.LifecycleBlocker(run));
        Assert.Equal(0, f.Runtime.RetireCalls);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public Guid Actor { get; } = Guid.NewGuid();
        public FakeRuntime Runtime { get; } = new();
        public FakeSource Source { get; } = new();
        public MemDbContext NewDb() => new(new DbContextOptionsBuilder<MemDbContext>().UseSqlite(connection)
            .AddInterceptors(new MigrationSessionLifecycleInterceptor(TimeProvider.System)).Options);
        public MigrationStagingRetirementService Service(MemDbContext db) => new(db, Source, Runtime, TimeProvider.System);
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.connection.OpenAsync();
            await using var db = fixture.NewDb();
            await db.Database.EnsureCreatedAsync();
            var now = DateTime.UtcNow;
            var intake = new MigrationIntakeEntity { Id = Guid.NewGuid(), IntakeId = "mig_one", DisplayName = "Example", CreatedAtUtc = now };
            var conversion = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(), ConversionAttemptId = "conv_one", MigrationIntake = intake,
                SourcePackageSha256 = new string('a', 64), SourceAdapterId = "mem-v010", SourceAdapterVersion = "0.1.0",
                ConverterId = "worker", ConverterVersion = "v2", Status = "completed", CurrentStep = "candidate-created", CreatedAtUtc = now, UpdatedAtUtc = now
            };
            var candidate = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(), CandidateArtifactId = "mca_one", ConversionAttempt = conversion,
                ArtifactKind = "synapse-postgresql-conversion", ArtifactSchemaVersion = "v1", SourcePackageSha256 = new string('a', 64),
                ArtifactSha256 = new string('b', 64), ManifestSha256 = new string('c', 64), ChecksumsSha256 = new string('d', 64), ProvenanceJson = "{}",
                VerificationStatus = "verified", RetentionState = "active", StorageKind = "server-filesystem", ArtifactPath = "/retained/candidate", CreatedAtUtc = now
            };
            db.MigrationStagingRuns.Add(new MigrationStagingRunEntity
            {
                Id = Guid.NewGuid(), StagingRunId = "mst_one", MigrationIntake = intake, CandidateArtifact = candidate,
                PrivateRuntimeStagingId = "stage_one", Status = "verified", CurrentStep = "verified", PrivateOnly = true,
                DatabaseImportSucceeded = true, SynapseHealthPassed = true, ActiveMigrationKey = "mig_one", CreatedAtUtc = now, UpdatedAtUtc = now
            });
            await db.SaveChangesAsync();
            return fixture;
        }
        public async Task<MigrationStagingRetirementOperation> AcceptAsync(CancellationToken ct = default)
        {
            await using var db = NewDb();
            var service = Service(db);
            var review = await service.ReviewAsync("mig_one", "mst_one", ct);
            Assert.True(review.CanRetire, review.BlockerCode);
            return await service.AcceptAsync("mig_one", "mst_one", Actor, new(review.ReviewFingerprint!, true), ct);
        }
        public async Task ExecuteAsync(Guid id, CancellationToken ct = default)
        {
            await using var db = NewDb();
            await Service(db).ExecuteAsync(id, ct);
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
    private sealed class FakeSource : ITemporaryStagingInventorySource
    {
        public string? Unavailable { get; set; }
        public Task<StagingInventorySnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new StagingInventorySnapshot(
            Unavailable != "containers", Unavailable != "networks", Unavailable != "ownership", Unavailable != "history", [],
            [new StagingRecordedRuntime("stage_one", "migration-candidate", null, "ready", false, [])],
            [new StagingRecordedOwner("stage_one", "migration", Unavailable == "wrong-owner" ? "mig_other" : "mig_one", "Example", "mca_one", true, false, "mst_one")], []));
    }
    private sealed class FakeRuntime : IMigrationStagingRetirementRuntime
    {
        public MigrationStagingRetirementPlan Plan { get; set; } = new("stage_one", "mca_one", "identity", new string('a',64), new string('b',64), new string('c',64), new string('d',64));
        public Exception? Failure { get; set; }
        public Exception? InspectionFailure { get; set; }
        public Action? DuringRetirement { get; set; }
        public int RetireCalls { get; private set; }
        public Task<MigrationStagingRetirementInspection> InspectAsync(string stagingId, string candidateId, CancellationToken ct)
        {
            if (InspectionFailure is not null) throw InspectionFailure;
            return Task.FromResult(new MigrationStagingRetirementInspection(Plan, 3, 1, true));
        }
        public async Task RetireAsync(MigrationStagingRetirementPlan plan, Func<string,CancellationToken,Task> progress, CancellationToken ct)
        {
            RetireCalls++;
            await progress("remove-element", ct);
            DuringRetirement?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Failure is not null) throw Failure;
            await progress("record-evidence", ct);
        }
    }
}
