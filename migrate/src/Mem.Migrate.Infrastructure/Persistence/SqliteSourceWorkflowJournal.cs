using System.Globalization;
using Microsoft.Data.Sqlite;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteSourceWorkflowJournal : ISourceWorkflowJournal
{
    private const string MigrationId = "20260728_source_workflow_runs_v1";
    private const string StableIdentityMigrationId =
        "20260919_source_workflow_stable_identity_v2";
    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private bool _initialized;

    public SqliteSourceWorkflowJournal(string workspacePath)
    {
        var workspace = Path.GetFullPath(workspacePath);
        PrivateFilePermissions.EnsureDirectory(workspace);
        _databasePath = Path.Combine(workspace, "mem-migrate.sqlite");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initializeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = FULL;

            CREATE TABLE IF NOT EXISTS journal_schema_migrations (
                migration_id TEXT PRIMARY KEY,
                applied_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS source_workflow_runs (
                workflow_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                current_stage TEXT NOT NULL,
                assessment_id TEXT NOT NULL,
                source_fingerprint TEXT NOT NULL,
                stable_source_identity TEXT NULL,
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

            CREATE INDEX IF NOT EXISTS ix_source_workflow_runs_updated
                ON source_workflow_runs(updated_at_utc DESC);

            CREATE INDEX IF NOT EXISTS ix_source_workflow_runs_status
                ON source_workflow_runs(status, updated_at_utc DESC);

            INSERT OR IGNORE INTO journal_schema_migrations (
                migration_id,
                applied_at_utc
            ) VALUES (
                $migrationId,
                $appliedAtUtc
            );

            UPDATE source_workflow_runs
            SET
                status = $failedStatus,
                current_stage = $recoveryStage,
                updated_at_utc = $appliedAtUtc,
                completed_at_utc = $appliedAtUtc,
                failure_code = 'operation_interrupted',
                failure_summary = 'The Source Assistant stopped while this operation was running. Inspect retained source artifacts before retrying.',
                revision = revision + 1
            WHERE status = $runningStatus;
            """;
        command.Parameters.AddWithValue("$migrationId", MigrationId);
        command.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$failedStatus",
            SourceWorkflowStatus.Failed.ToString());
        command.Parameters.AddWithValue(
            "$recoveryStage",
            SourceWorkflowStage.RecoveryRequired.ToString());
        command.Parameters.AddWithValue(
            "$runningStatus",
            SourceWorkflowStatus.Running.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureStableSourceIdentityColumnAsync(connection, cancellationToken);
            PrivateFilePermissions.EnsureFile(_databasePath);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<SourceWorkflowRecord?> GetLatestAsync(
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                workflow_id,
                status,
                current_stage,
                assessment_id,
                source_fingerprint,
                stable_source_identity,
                selected_source_stack_id,
                intake_id,
                package_revision_id,
                request_kind,
                age_recipient,
                recipient_fingerprint,
                request_expires_at_utc,
                target_control_plane_version,
                encryption_readiness_acknowledged_at_utc,
                capture_id,
                package_report_json,
                created_at_utc,
                updated_at_utc,
                completed_at_utc,
                cancellation_requested_at_utc,
                failure_code,
                failure_summary,
                revision
            FROM source_workflow_runs
            ORDER BY updated_at_utc DESC
            LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Read(reader)
            : null;
    }

    public async Task<IReadOnlyList<SourceWorkflowRecord>> ListRecentAsync(
        string selectedSourceStackId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedSourceStackId))
        {
            throw new ArgumentException(
                "A selected source stack ID is required.",
                nameof(selectedSourceStackId));
        }

        if (limit is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "The recent workflow limit must be between 1 and 10.");
        }

        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                workflow_id,
                status,
                current_stage,
                assessment_id,
                source_fingerprint,
                stable_source_identity,
                selected_source_stack_id,
                intake_id,
                package_revision_id,
                request_kind,
                age_recipient,
                recipient_fingerprint,
                request_expires_at_utc,
                target_control_plane_version,
                encryption_readiness_acknowledged_at_utc,
                capture_id,
                package_report_json,
                created_at_utc,
                updated_at_utc,
                completed_at_utc,
                cancellation_requested_at_utc,
                failure_code,
                failure_summary,
                revision
            FROM source_workflow_runs
            WHERE selected_source_stack_id COLLATE NOCASE = $selectedSourceStackId
            ORDER BY updated_at_utc DESC, workflow_id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue(
            "$selectedSourceStackId",
            selectedSourceStackId.Trim());
        command.Parameters.AddWithValue("$limit", limit);

        var workflows = new List<SourceWorkflowRecord>(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            workflows.Add(Read(reader));
        }

        return workflows;
    }

    public async Task<SourceWorkflowRecord?> GetAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                workflow_id,
                status,
                current_stage,
                assessment_id,
                source_fingerprint,
                stable_source_identity,
                selected_source_stack_id,
                intake_id,
                package_revision_id,
                request_kind,
                age_recipient,
                recipient_fingerprint,
                request_expires_at_utc,
                target_control_plane_version,
                encryption_readiness_acknowledged_at_utc,
                capture_id,
                package_report_json,
                created_at_utc,
                updated_at_utc,
                completed_at_utc,
                cancellation_requested_at_utc,
                failure_code,
                failure_summary,
                revision
            FROM source_workflow_runs
            WHERE workflow_id = $workflowId;
            """;
        command.Parameters.AddWithValue("$workflowId", workflowId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? Read(reader)
            : null;
    }

    public async Task SaveAsync(
        SourceWorkflowRecord workflow,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_workflow_runs (
                workflow_id,
                status,
                current_stage,
                assessment_id,
                source_fingerprint,
                stable_source_identity,
                selected_source_stack_id,
                intake_id,
                package_revision_id,
                request_kind,
                age_recipient,
                recipient_fingerprint,
                request_expires_at_utc,
                target_control_plane_version,
                encryption_readiness_acknowledged_at_utc,
                capture_id,
                package_report_json,
                created_at_utc,
                updated_at_utc,
                completed_at_utc,
                cancellation_requested_at_utc,
                failure_code,
                failure_summary,
                revision
            ) VALUES (
                $workflowId,
                $status,
                $stage,
                $assessmentId,
                $sourceFingerprint,
                $stableSourceIdentity,
                $selectedSourceStackId,
                $intakeId,
                $packageRevisionId,
                $requestKind,
                $ageRecipient,
                $recipientFingerprint,
                $requestExpiresAtUtc,
                $targetControlPlaneVersion,
                $encryptionReadinessAcknowledgedAtUtc,
                $captureId,
                $packageReportJson,
                $createdAtUtc,
                $updatedAtUtc,
                $completedAtUtc,
                $cancellationRequestedAtUtc,
                $failureCode,
                $failureSummary,
                $revision
            )
            ON CONFLICT(workflow_id) DO UPDATE SET
                status = excluded.status,
                current_stage = excluded.current_stage,
                assessment_id = excluded.assessment_id,
                source_fingerprint = excluded.source_fingerprint,
                stable_source_identity = excluded.stable_source_identity,
                selected_source_stack_id = excluded.selected_source_stack_id,
                intake_id = excluded.intake_id,
                package_revision_id = excluded.package_revision_id,
                request_kind = excluded.request_kind,
                age_recipient = excluded.age_recipient,
                recipient_fingerprint = excluded.recipient_fingerprint,
                request_expires_at_utc = excluded.request_expires_at_utc,
                target_control_plane_version = excluded.target_control_plane_version,
                encryption_readiness_acknowledged_at_utc = excluded.encryption_readiness_acknowledged_at_utc,
                capture_id = excluded.capture_id,
                package_report_json = excluded.package_report_json,
                updated_at_utc = excluded.updated_at_utc,
                completed_at_utc = excluded.completed_at_utc,
                cancellation_requested_at_utc = excluded.cancellation_requested_at_utc,
                failure_code = excluded.failure_code,
                failure_summary = excluded.failure_summary,
                revision = excluded.revision;
            """;
        command.Parameters.AddWithValue("$workflowId", workflow.WorkflowId);
        command.Parameters.AddWithValue("$status", workflow.Status.ToString());
        command.Parameters.AddWithValue("$stage", workflow.Stage.ToString());
        command.Parameters.AddWithValue("$assessmentId", workflow.AssessmentId);
        command.Parameters.AddWithValue("$sourceFingerprint", workflow.SourceFingerprint);
        command.Parameters.AddWithValue(
            "$stableSourceIdentity",
            (object?)workflow.StableSourceIdentity ?? DBNull.Value);
        command.Parameters.AddWithValue("$selectedSourceStackId", workflow.SelectedSourceStackId);
        command.Parameters.AddWithValue("$intakeId", workflow.IntakeId);
        command.Parameters.AddWithValue(
            "$packageRevisionId",
            (object?)workflow.PackageRevisionId ?? DBNull.Value);
        command.Parameters.AddWithValue("$requestKind", workflow.RequestKind);
        command.Parameters.AddWithValue("$ageRecipient", workflow.AgeRecipient);
        command.Parameters.AddWithValue("$recipientFingerprint", workflow.RecipientFingerprint);
        command.Parameters.AddWithValue(
            "$requestExpiresAtUtc",
            workflow.RequestExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$targetControlPlaneVersion",
            workflow.TargetControlPlaneVersion);
        command.Parameters.AddWithValue(
            "$encryptionReadinessAcknowledgedAtUtc",
            workflow.EncryptionReadinessAcknowledgedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$captureId",
            (object?)workflow.CaptureId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$packageReportJson",
            (object?)workflow.PackageReportJson ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$createdAtUtc",
            workflow.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$updatedAtUtc",
            workflow.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$completedAtUtc",
            workflow.CompletedAtUtc is null
                ? DBNull.Value
                : workflow.CompletedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$cancellationRequestedAtUtc",
            workflow.CancellationRequestedAtUtc is null
                ? DBNull.Value
                : workflow.CancellationRequestedAtUtc.Value.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$failureCode",
            (object?)workflow.FailureCode ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$failureSummary",
            (object?)workflow.FailureSummary ?? DBNull.Value);
        command.Parameters.AddWithValue("$revision", workflow.Revision);
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    private static SourceWorkflowRecord Read(SqliteDataReader reader) =>
        new(
            WorkflowId: reader.GetString(0),
            Status: Enum.Parse<SourceWorkflowStatus>(reader.GetString(1), ignoreCase: true),
            Stage: Enum.Parse<SourceWorkflowStage>(reader.GetString(2), ignoreCase: true),
            AssessmentId: reader.GetString(3),
            SourceFingerprint: reader.GetString(4),
            SelectedSourceStackId: reader.GetString(6),
            IntakeId: reader.GetString(7),
            PackageRevisionId: reader.IsDBNull(8) ? null : reader.GetString(8),
            RequestKind: reader.GetString(9),
            AgeRecipient: reader.GetString(10),
            RecipientFingerprint: reader.GetString(11),
            RequestExpiresAtUtc: ParseTimestamp(reader.GetString(12)),
            TargetControlPlaneVersion: reader.GetString(13),
            EncryptionReadinessAcknowledgedAtUtc: ParseTimestamp(reader.GetString(14)),
            CaptureId: reader.IsDBNull(15) ? null : reader.GetString(15),
            PackageReportJson: reader.IsDBNull(16) ? null : reader.GetString(16),
            CreatedAtUtc: ParseTimestamp(reader.GetString(17)),
            UpdatedAtUtc: ParseTimestamp(reader.GetString(18)),
            CompletedAtUtc: reader.IsDBNull(19) ? null : ParseTimestamp(reader.GetString(19)),
            CancellationRequestedAtUtc: reader.IsDBNull(20) ? null : ParseTimestamp(reader.GetString(20)),
            FailureCode: reader.IsDBNull(21) ? null : reader.GetString(21),
            FailureSummary: reader.IsDBNull(22) ? null : reader.GetString(22),
            Revision: reader.GetInt64(23),
            StableSourceIdentity: reader.IsDBNull(5) ? null : reader.GetString(5));

    private static async Task EnsureStableSourceIdentityColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var present = false;
        await using (var inspect = connection.CreateCommand())
        {
            inspect.CommandText = "PRAGMA table_info(source_workflow_runs);";
            await using var reader = await inspect.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(
                        reader.GetString(1),
                        "stable_source_identity",
                        StringComparison.OrdinalIgnoreCase))
                {
                    present = true;
                    break;
                }
            }
        }

        if (!present)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText =
                "ALTER TABLE source_workflow_runs ADD COLUMN stable_source_identity TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var recordMigration = connection.CreateCommand();
        recordMigration.CommandText =
            """
            INSERT OR IGNORE INTO journal_schema_migrations (
                migration_id,
                applied_at_utc
            ) VALUES (
                $migrationId,
                $appliedAtUtc
            );
            """;
        recordMigration.Parameters.AddWithValue(
            "$migrationId",
            StableIdentityMigrationId);
        recordMigration.Parameters.AddWithValue(
            "$appliedAtUtc",
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await recordMigration.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
