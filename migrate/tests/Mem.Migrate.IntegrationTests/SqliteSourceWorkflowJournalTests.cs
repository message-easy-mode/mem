using Microsoft.Data.Sqlite;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteSourceWorkflowJournalTests
{
    [Fact]
    public async Task Persists_and_reloads_a_source_workflow()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-source-workflow-{Guid.NewGuid():N}");
        try
        {
            var journal = new SqliteSourceWorkflowJournal(root);
            var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
            var workflow = NewWorkflow(now) with
            {
                Status = SourceWorkflowStatus.Completed,
                Stage = SourceWorkflowStage.ReadyForDownload,
                CaptureId = "capture-01",
                PackageReportJson = "{}",
                CompletedAtUtc = now.AddMinutes(2),
                Revision = 4
            };

            await journal.SaveAsync(workflow, CancellationToken.None);
            var stored = await journal.GetLatestAsync(CancellationToken.None);

            Assert.Equal(workflow, stored);
            Assert.True(File.Exists(Path.Combine(root, "mem-migrate.sqlite")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Lists_recent_workflows_for_selected_stack_in_activity_order_with_a_bounded_limit()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-source-workflow-{Guid.NewGuid():N}");
        try
        {
            var journal = new SqliteSourceWorkflowJournal(root);
            var stackId = Guid.NewGuid().ToString("D");
            var otherStackId = Guid.NewGuid().ToString("D");
            var now = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
            var oldest = NewWorkflow(
                now,
                "source-20260728-000001Z-11111111111111111111111111111111",
                stackId);
            var middle = NewWorkflow(
                now.AddMinutes(1),
                "source-20260728-000002Z-22222222222222222222222222222222",
                stackId);
            var newest = NewWorkflow(
                now.AddMinutes(2),
                "source-20260728-000003Z-33333333333333333333333333333333",
                stackId);
            var other = NewWorkflow(
                now.AddMinutes(3),
                "source-20260728-000004Z-44444444444444444444444444444444",
                otherStackId);

            await journal.SaveAsync(oldest, CancellationToken.None);
            await journal.SaveAsync(middle, CancellationToken.None);
            await journal.SaveAsync(newest, CancellationToken.None);
            await journal.SaveAsync(other, CancellationToken.None);

            var recent = await journal.ListRecentAsync(
                stackId.ToUpperInvariant(),
                2,
                CancellationToken.None);

            Assert.Equal(
                [newest.WorkflowId, middle.WorkflowId],
                recent.Select(workflow => workflow.WorkflowId));
            Assert.DoesNotContain(
                recent,
                workflow => string.Equals(
                    workflow.SelectedSourceStackId,
                    otherStackId,
                    StringComparison.OrdinalIgnoreCase));

            var requested = await journal.GetAsync(
                oldest.WorkflowId,
                CancellationToken.None);
            Assert.Equal(oldest, requested);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Upgrades_an_existing_source_workflow_journal_with_nullable_stable_identity()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-source-workflow-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "mem-migrate.sqlite");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE journal_schema_migrations (
                        migration_id TEXT PRIMARY KEY,
                        applied_at_utc TEXT NOT NULL
                    );
                    CREATE TABLE source_workflow_runs (
                        workflow_id TEXT PRIMARY KEY,
                        status TEXT NOT NULL,
                        current_stage TEXT NOT NULL,
                        assessment_id TEXT NOT NULL,
                        source_fingerprint TEXT NOT NULL,
                        selected_source_stack_id TEXT NOT NULL,
                        intake_id TEXT NOT NULL,
                        package_revision_id TEXT NULL,
                        request_kind TEXT NOT NULL,
                        age_recipient TEXT NOT NULL,
                        recipient_fingerprint TEXT NOT NULL,
                        request_expires_at_utc TEXT NOT NULL,
                        target_control_plane_version TEXT NOT NULL,
                        encryption_readiness_acknowledged_at_utc TEXT NOT NULL,
                        capture_id TEXT NULL,
                        package_report_json TEXT NULL,
                        created_at_utc TEXT NOT NULL,
                        updated_at_utc TEXT NOT NULL,
                        completed_at_utc TEXT NULL,
                        cancellation_requested_at_utc TEXT NULL,
                        failure_code TEXT NULL,
                        failure_summary TEXT NULL,
                        revision INTEGER NOT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var journal = new SqliteSourceWorkflowJournal(root);
            var identity = new string('f', 64);
            var workflow = NewWorkflow(DateTimeOffset.UtcNow) with
            {
                StableSourceIdentity = identity
            };

            await journal.SaveAsync(workflow, CancellationToken.None);
            var stored = await journal.GetLatestAsync(CancellationToken.None);

            Assert.Equal(identity, stored?.StableSourceIdentity);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Marks_a_running_workflow_for_recovery_on_new_process_initialization()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-source-workflow-{Guid.NewGuid():N}");
        try
        {
            var first = new SqliteSourceWorkflowJournal(root);
            var workflow = NewWorkflow(DateTimeOffset.UtcNow) with
            {
                Status = SourceWorkflowStatus.Running,
                Stage = SourceWorkflowStage.Capturing
            };
            await first.SaveAsync(workflow, CancellationToken.None);

            var restarted = new SqliteSourceWorkflowJournal(root);
            await restarted.InitializeAsync(CancellationToken.None);
            var stored = await restarted.GetLatestAsync(CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal(SourceWorkflowStatus.Failed, stored.Status);
            Assert.Equal(SourceWorkflowStage.RecoveryRequired, stored.Stage);
            Assert.Equal("operation_interrupted", stored.FailureCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static SourceWorkflowRecord NewWorkflow(
        DateTimeOffset now,
        string? workflowId = null,
        string? stackId = null) =>
        new(
            WorkflowId: workflowId ?? "source-20260728-000000Z-11111111111111111111111111111111",
            Status: SourceWorkflowStatus.Pending,
            Stage: SourceWorkflowStage.RequestValidated,
            AssessmentId: "assessment-01",
            SourceFingerprint: new string('a', 64),
            SelectedSourceStackId: stackId ?? Guid.NewGuid().ToString("D"),
            IntakeId: "mig_20260728_preview",
            PackageRevisionId: null,
            RequestKind: "preview",
            AgeRecipient: "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f",
            RecipientFingerprint: "1111-2222-3333-4444",
            RequestExpiresAtUtc: now.AddHours(2),
            TargetControlPlaneVersion: "0.2.0",
            EncryptionReadinessAcknowledgedAtUtc: now,
            CaptureId: null,
            PackageReportJson: null,
            CreatedAtUtc: now,
            UpdatedAtUtc: now,
            CompletedAtUtc: null,
            CancellationRequestedAtUtc: null,
            FailureCode: null,
            FailureSummary: null,
            Revision: 1,
            StableSourceIdentity: new string('f', 64));
}
