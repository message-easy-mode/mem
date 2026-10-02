using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsActiveContextServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 3, 1, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Workflow_incident_wins_running_work_and_uses_server_authored_links()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMigrationAsync("mig_running", Now.AddMinutes(-1));

        var result = await fixture.ResolveAsync(
        [
            Event(
                severity: "error",
                incidentId: "inc_restore_1",
                feature: "restore",
                timestamp: Now,
                resource: new MemDiagnosticResource(
                    Kind: "restore",
                    Id: "restore_123",
                    DisplayName: "Family restore",
                    WorkspacePath: "https://untrusted.example.test/restore"))
        ]);

        var context = Assert.IsType<Modules.Operator.Diagnostics.Contracts.DiagnosticsActiveContext>(
            result.Context);
        Assert.Equal("attention", context.State);
        Assert.Equal("workflow_requires_attention", context.Code);
        Assert.Equal("/restores/restore_123", context.WorkspaceHref);
        Assert.Equal("/diagnostics/logs?incident=inc_restore_1", context.IncidentHref);
        Assert.DoesNotContain("untrusted.example.test", context.WorkspaceHref, StringComparison.Ordinal);
        Assert.Null(result.WarningCode);
    }

    [Fact]
    public async Task Latest_running_workflow_is_projected_when_no_incident_requires_attention()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMigrationAsync("mig_older", Now.AddMinutes(-4));
        await fixture.AddRestoreAsync("restore_newer", Now.AddMinutes(-1));
        await fixture.AddInstallationAsync(Now.AddMinutes(-3));

        var result = await fixture.ResolveAsync([]);

        Assert.Equal("restore_active", result.Context?.Code);
        Assert.Equal("restore", result.Context?.Kind);
        Assert.Equal("private-test", result.Context?.Stage);
        Assert.Equal("/restores/restore_newer", result.Context?.WorkspaceHref);
    }

    [Fact]
    public async Task Active_migration_uses_the_canonical_migration_workspace()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMigrationAsync("mig_active_1", Now);

        var result = await fixture.ResolveAsync([]);

        Assert.Equal("migration_active", result.Context?.Code);
        Assert.Equal("Current family migration", result.Context?.ResourceName);
        Assert.Equal("/migrations/mig_active_1", result.Context?.WorkspaceHref);
        Assert.Null(result.Context?.IncidentHref);
    }

    [Fact]
    public async Task Federation_and_turn_runtime_operations_use_stack_scoped_workspaces()
    {
        await using var fixture = await Fixture.CreateAsync();
        var stackId = await fixture.AddStackAsync("family-chat");
        await fixture.AddRuntimeOperationAsync(
            stackId,
            "apply-federation-policy",
            Now.AddMinutes(-2));

        var federation = await fixture.ResolveAsync([]);

        Assert.Equal("federation_operation_active", federation.Context?.Code);
        Assert.Equal("/stacks/family-chat/federation", federation.Context?.WorkspaceHref);

        fixture.Db.RuntimeOperations.RemoveRange(fixture.Db.RuntimeOperations);
        await fixture.Db.SaveChangesAsync();
        await fixture.AddRuntimeOperationAsync(
            stackId,
            "connect-stack-turn",
            Now.AddMinutes(-1));

        var turn = await fixture.ResolveAsync([]);

        Assert.Equal("turn_operation_active", turn.Context?.Code);
        Assert.Equal("/stacks/family-chat/services", turn.Context?.WorkspaceHref);
    }

    [Fact]
    public async Task Seq_runtime_operation_uses_the_dedicated_seq_workspace()
    {
        await using var fixture = await Fixture.CreateAsync();
        var stackId = await fixture.AddStackAsync("context-owner");
        await fixture.AddRuntimeOperationAsync(
            stackId,
            "seq.restart",
            Now);

        var result = await fixture.ResolveAsync([]);

        Assert.Equal("seq_operation_active", result.Context?.Code);
        Assert.Equal("seq", result.Context?.Kind);
        Assert.Equal("logging", result.Context?.Feature);
        Assert.Equal("Seq", result.Context?.ResourceName);
        Assert.Equal("/diagnostics/seq", result.Context?.WorkspaceHref);
        Assert.Null(result.Context?.IncidentHref);
    }

    [Fact]
    public async Task Workflow_feature_without_a_resource_still_wins_running_work()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMigrationAsync("mig_running", Now.AddMinutes(-1));

        var result = await fixture.ResolveAsync(
        [
            Event(
                severity: "error",
                incidentId: "inc_migration_without_resource",
                feature: "migration",
                timestamp: Now,
                resource: null)
        ]);

        Assert.Equal("workflow_requires_attention", result.Context?.Code);
        Assert.Equal("/diagnostics/logs?incident=inc_migration_without_resource", result.Context?.IncidentHref);
        Assert.Null(result.Context?.WorkspaceHref);
    }

    [Fact]
    public async Task Generic_recent_incident_is_used_only_when_no_workflow_is_running()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.ResolveAsync(
        [
            Event(
                severity: "warning",
                incidentId: "inc_runtime_1",
                feature: "runtime",
                timestamp: Now,
                resource: null)
        ]);

        Assert.Equal("incident_requires_attention", result.Context?.Code);
        Assert.Equal("/diagnostics/logs?incident=inc_runtime_1", result.Context?.IncidentHref);
        Assert.Null(result.Context?.WorkspaceHref);
    }

    [Fact]
    public async Task Database_projection_failure_keeps_safe_incident_and_reports_partial_warning()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Db.DisposeAsync();

        var result = await fixture.ResolveAsync(
        [
            Event(
                severity: "error",
                incidentId: "inc_safe_fallback",
                feature: "runtime",
                timestamp: Now,
                resource: null)
        ]);

        Assert.Equal("incident_requires_attention", result.Context?.Code);
        Assert.Equal("diagnostics.active_context_unavailable", result.WarningCode);
        await fixture.DisposeAsync();
    }

    private static MemDiagnosticEvent Event(
        string severity,
        string incidentId,
        string feature,
        DateTimeOffset timestamp,
        MemDiagnosticResource? resource) =>
        new(
            SchemaVersion: 1,
            EventId: $"evt_{Guid.NewGuid():N}",
            TimestampUtc: timestamp,
            Severity: severity,
            EventCode: "workflow.failed",
            Source: "test",
            Feature: feature,
            Stage: "test-stage",
            Message: "A safe workflow summary requires attention.",
            IncidentId: incidentId,
            TraceId: null,
            SpanId: null,
            RequestId: null,
            CorrelationId: null,
            OperationId: null,
            Resource: resource,
            Expected: null,
            Observed: null,
            Details: null,
            Exception: null,
            SuggestedAction: null,
            Retryable: true,
            RedactionsApplied: true,
            Truncated: false);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private bool _disposed;

        private Fixture(string databasePath, MemDbContext db)
        {
            _databasePath = databasePath;
            Db = db;
        }

        public MemDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-diagnostics-context-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            return new Fixture(databasePath, db);
        }

        public Task<DiagnosticsActiveContextResolution> ResolveAsync(
            IReadOnlyList<MemDiagnosticEvent> events)
        {
            var service = new DiagnosticsActiveContextService(
                Db,
                new DiagnosticsWorkspaceLinkBuilder(),
                new FakeTimeProvider(Now),
                NullLogger<DiagnosticsActiveContextService>.Instance);
            return service.ResolveAsync(events, CancellationToken.None);
        }

        public async Task AddMigrationAsync(string intakeId, DateTimeOffset updatedAtUtc)
        {
            Db.MigrationIntakes.Add(new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = intakeId,
                DisplayName = "Current family migration",
                CreatedAtUtc = updatedAtUtc.AddMinutes(-5).UtcDateTime,
                UpdatedAtUtc = updatedAtUtc.UtcDateTime,
                LifecycleStatus = MigrationSessionLifecycleStatuses.Active,
                StateVersion = 1
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddRestoreAsync(string restoreSessionId, DateTimeOffset updatedAtUtc)
        {
            Db.RestoreAttempts.Add(new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = restoreSessionId,
                SourceKind = "backup-catalog",
                SourceKey = $"backup-catalog:{Guid.NewGuid():N}",
                ActiveSourceKey = $"backup-catalog:{Guid.NewGuid():N}",
                SourceCatalogEntryIdSnapshot = "bkp_context",
                SourceDisplayNameSnapshot = "Family backup",
                SourceOriginKindSnapshot = "local-captured",
                Status = "private-test",
                CurrentStage = "private-test",
                CreatedAtUtc = updatedAtUtc.AddMinutes(-10).UtcDateTime,
                UpdatedAtUtc = updatedAtUtc.UtcDateTime,
                LastEventAtUtc = updatedAtUtc.UtcDateTime,
                SessionDirectoryPath = "/private/not-projected"
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddInstallationAsync(DateTimeOffset updatedAtUtc)
        {
            Db.Installations.Add(new InstallationEntity
            {
                Id = Guid.NewGuid(),
                Status = "Running",
                CreatedAtUtc = updatedAtUtc.AddMinutes(-10).UtcDateTime,
                UpdatedAtUtc = updatedAtUtc.UtcDateTime,
                StartedAtUtc = updatedAtUtc.AddMinutes(-5).UtcDateTime
            });
            await Db.SaveChangesAsync();
        }

        public async Task<Guid> AddStackAsync(string slug)
        {
            var id = Guid.NewGuid();
            Db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = id,
                Slug = slug,
                DisplayName = slug,
                Status = "ready",
                CreatedAtUtc = Now.AddHours(-1).UtcDateTime,
                UpdatedAtUtc = Now.UtcDateTime,
                MatrixInstanceId = Guid.NewGuid()
            });
            await Db.SaveChangesAsync();
            return id;
        }

        public async Task AddRuntimeOperationAsync(
            Guid stackId,
            string operation,
            DateTimeOffset startedAtUtc)
        {
            Db.RuntimeOperations.Add(new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = stackId,
                Operation = operation,
                Status = "running",
                RequestedAtUtc = startedAtUtc.UtcDateTime,
                StartedAtUtc = startedAtUtc.UtcDateTime,
                CurrentStep = "verifying-effective-state",
                AttemptCount = 1,
                LockedUntilUtc = Now.AddMinutes(5).UtcDateTime
            });
            await Db.SaveChangesAsync();
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

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
