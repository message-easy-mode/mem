using HostAgent.Matrix.Federation;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Stacks.Turn;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class CriticalWorkflowDiagnosticProjectionTests
{
    [Fact]
    public async Task Runtime_operation_lifecycle_emits_correlated_safe_events_and_a_terminal_incident()
    {
        using var diagnostics = new MemDiagnosticTestFixture();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var stack = new RuntimeStackEntity
        {
            Id = Guid.NewGuid(),
            Slug = "diagnostic-stack",
            DisplayName = "Diagnostic Stack",
            Status = "ready",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MatrixInstanceId = Guid.NewGuid()
        };
        db.RuntimeStacks.Add(stack);
        await db.SaveChangesAsync();

        var store = new RuntimeOperationStore(db, diagnostics.Writer);
        var operationId = await store.StartAsync(
            stack.Id,
            RuntimeStackFederationApplyService.OperationName,
            "diag-operation-key",
            "owner",
            "filesystem,docker",
            new { password = "must-not-escape" },
            CancellationToken.None);
        await store.UpdateStepAsync(
            operationId,
            "verifying-readiness",
            CancellationToken.None);
        await store.FailAsync(
            operationId,
            "verifying-readiness",
            "Container password=must-not-escape did not become ready.",
            evidence: null,
            CancellationToken.None);

        var page = await diagnostics.Reader.QueryAsync(new MemDiagnosticQuery(
            Feature: "federation",
            OperationId: operationId,
            PageSize: 20));

        Assert.Equal(3, page.Events.Count);
        Assert.Contains(page.Events, item => item.EventCode == "runtime.operation.started");
        Assert.Contains(page.Events, item => item.EventCode == "runtime.operation.step_changed");
        var failed = Assert.Single(page.Events.Where(item =>
            item.EventCode == "runtime.operation.failed"));
        Assert.NotNull(failed.IncidentId);
        Assert.Equal("federation", failed.Resource!.Kind);
        Assert.Equal(stack.Id.ToString("D"), failed.Resource.StackId);
        Assert.Equal(stack.Slug, failed.Resource.StackSlug);
        Assert.Equal("synapse", failed.Resource.Service);
        Assert.DoesNotContain(
            "must-not-escape",
            System.Text.Json.JsonSerializer.Serialize(page.Events),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turn_operation_failure_projects_stack_owned_turn_incident()
    {
        using var diagnostics = new MemDiagnosticTestFixture();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var stack = new RuntimeStackEntity
        {
            Id = Guid.NewGuid(),
            Slug = "turn-stack",
            DisplayName = "TURN Stack",
            Status = "ready",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MatrixInstanceId = Guid.NewGuid()
        };
        db.RuntimeStacks.Add(stack);
        await db.SaveChangesAsync();

        var store = new RuntimeOperationStore(db, diagnostics.Writer);
        var operationId = await store.StartAsync(
            stack.Id,
            RuntimeStackTurnConnectionService.OperationName,
            "turn-operation-key",
            "owner",
            "filesystem,docker",
            input: null,
            CancellationToken.None);
        await store.FailAsync(
            operationId,
            "verify-turn",
            "TURN verification failed.",
            evidence: null,
            CancellationToken.None);

        var page = await diagnostics.Reader.QueryAsync(new MemDiagnosticQuery(
            Feature: "turn",
            OperationId: operationId,
            PageSize: 20));

        var failed = Assert.Single(page.Events.Where(item =>
            item.EventCode == "runtime.operation.failed"));
        Assert.Equal($"inc_op_{operationId:N}", failed.IncidentId);
        Assert.Equal("turn", failed.Resource!.Kind);
        Assert.Equal(stack.Slug, failed.Resource.StackSlug);
        Assert.Equal("synapse", failed.Resource.Service);
        Assert.Equal($"/stacks/{stack.Slug}/services", failed.Resource.WorkspacePath);
    }

    [Fact]
    public async Task Migration_production_failure_projects_one_migration_owned_incident()
    {
        using var diagnostics = new MemDiagnosticTestFixture();
        const string migrationId = "mig_diagnostic_test";

        await MigrationProductionDiagnosticEvents.RecordAsync(
            diagnostics.Writer,
            migrationId,
            eventCode: "migration.production.verification.failed",
            severity: MemDiagnosticSeverities.Error,
            stage: "production-verification",
            message: "Migration production verification failed.",
            targetStackSlug: "migrated-stack",
            createIncident: true,
            exception: new InvalidOperationException("token=must-not-escape"),
            service: "synapse",
            observed: new Dictionary<string, string?>
            {
                ["status"] = "failed"
            },
            retryable: true,
            suggestedAction: "Open the Migration Workspace.");

        var page = await diagnostics.Reader.QueryAsync(new MemDiagnosticQuery(
            Feature: "migration",
            PageSize: 20));

        var failed = Assert.Single(page.Events);
        Assert.NotNull(failed.IncidentId);
        Assert.Equal("migration", failed.Resource!.Kind);
        Assert.Equal(migrationId, failed.Resource.Id);
        Assert.Equal("migrated-stack", failed.Resource.StackSlug);
        Assert.Equal($"/migrations/{migrationId}", failed.Resource.WorkspacePath);
        Assert.DoesNotContain(
            "must-not-escape",
            System.Text.Json.JsonSerializer.Serialize(failed),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_error_is_bridged_once_with_restore_ownership_and_redaction()
    {
        using var diagnostics = new MemDiagnosticTestFixture();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-restore-diagnostics-{Guid.NewGuid():N}");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();
            var workspace = new RestoreAttemptWorkspaceStore(configuration);
            var restoreSessionId = $"rst_{Guid.NewGuid():N}";
            var attempt = new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = restoreSessionId,
                SourceKind = "backup-catalog",
                SourceKey = "catalog:test",
                ActiveSourceKey = "catalog:test",
                SourceCatalogEntryIdSnapshot = "cat_test",
                SourceDisplayNameSnapshot = "Test backup",
                SourceOriginKindSnapshot = "local",
                SourceStackSlugSnapshot = "test-stack",
                Status = "needs-attention",
                CurrentStage = "private-test",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                SessionDirectoryPath = workspace.GetSessionDirectoryPath(restoreSessionId),
                LogDirectoryPath = Path.Combine(
                    workspace.GetSessionDirectoryPath(restoreSessionId),
                    "logs")
            };
            db.RestoreAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var logs = new RestoreStructuredLogService(
                db,
                workspace,
                NullLogger<RestoreStructuredLogService>.Instance,
                diagnostics.Writer);
            var operationId = Guid.NewGuid();
            await logs.RecordAsync(
                attempt.Id,
                operationId: operationId,
                stage: "private-test",
                severity: RestoreLogSeverities.Error,
                eventCode: "restore.private-test.failed",
                message: "Private restore test failed with token=must-not-escape.",
                details: new Dictionary<string, string?>
                {
                    ["token"] = "must-not-escape",
                    ["errorCode"] = "private_test_failed"
                },
                ct: CancellationToken.None,
                updateAttemptSummary: false);

            var page = await diagnostics.Reader.QueryAsync(new MemDiagnosticQuery(
                Feature: "restore",
                IncidentId: null,
                PageSize: 20));
            var bridged = Assert.Single(page.Events);
            Assert.Equal("restore.private-test.failed", bridged.EventCode);
            Assert.Equal($"inc_op_{operationId:N}", bridged.IncidentId);
            Assert.Equal("restore", bridged.Resource!.Kind);
            Assert.Equal(restoreSessionId, bridged.Resource.Id);
            Assert.Equal("[redacted]", bridged.Details!["token"]);
            Assert.DoesNotContain(
                "must-not-escape",
                System.Text.Json.JsonSerializer.Serialize(bridged),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Workflow_diagnostic_failure_never_escapes_into_the_observed_workflow()
    {
        var result = await new ThrowingDiagnosticWriter()
            .TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Error,
                EventCode: "diagnostics.test.workflow_failure",
                Source: "tests",
                Feature: "diagnostics",
                Message: "Test event"));

        Assert.NotNull(result);
        Assert.False(result!.Stored);
        Assert.Equal(MemDiagnosticCodes.StoreWriteFailed, result.WarningCode);
    }

    private sealed class ThrowingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic diagnostic writer failure.");
    }
}
