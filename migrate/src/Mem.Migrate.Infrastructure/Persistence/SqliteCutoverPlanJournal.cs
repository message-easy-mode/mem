using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteCutoverPlanJournal(string workspacePath)
    : ICutoverPlanJournal
{
    private readonly string _databasePath = Path.Combine(
        Path.GetFullPath(workspacePath),
        "cutover-plan-journal.db");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        PrivateFilePermissions.EnsureDirectory(
            Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS cutover_plan_runs (
                plan_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                input_binding_sha256 TEXT NOT NULL,
                expected_source_fingerprint TEXT NOT NULL,
                stage_attempt_id TEXT NOT NULL,
                source_fingerprint TEXT NULL,
                plan_hash TEXT NULL,
                report_json TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredCutoverPlanRun?> GetAsync(
        string planId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT status,
                   started_at_utc,
                   completed_at_utc,
                   input_binding_sha256,
                   expected_source_fingerprint,
                   stage_attempt_id,
                   source_fingerprint,
                   plan_hash,
                   report_json,
                   error_code,
                   error_message
            FROM cutover_plan_runs
            WHERE plan_id = $plan_id;
            """;
        command.Parameters.AddWithValue("$plan_id", planId);

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredCutoverPlanRun(
            planId,
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.IsDBNull(2)
                ? null
                : DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    public async Task StartAsync(
        string planId,
        DateTimeOffset startedAtUtc,
        string inputBindingSha256,
        string expectedSourceFingerprint,
        string stageAttemptId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO cutover_plan_runs (
                plan_id,
                status,
                started_at_utc,
                input_binding_sha256,
                expected_source_fingerprint,
                stage_attempt_id)
            VALUES (
                $plan_id,
                'Running',
                $started_at_utc,
                $input_binding_sha256,
                $expected_source_fingerprint,
                $stage_attempt_id);
            """;
        command.Parameters.AddWithValue("$plan_id", planId);
        command.Parameters.AddWithValue(
            "$started_at_utc",
            startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$input_binding_sha256",
            inputBindingSha256);
        command.Parameters.AddWithValue(
            "$expected_source_fingerprint",
            expectedSourceFingerprint);
        command.Parameters.AddWithValue("$stage_attempt_id", stageAttemptId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(
        CutoverPreparationReport report,
        string reportJson,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            report.Plan.PlanId,
            "Completed",
            DateTimeOffset.UtcNow,
            report.Plan.SourceFingerprint,
            report.PlanHash,
            reportJson,
            null,
            null,
            cancellationToken);

    public Task FailAsync(
        string planId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            planId,
            "Failed",
            completedAtUtc,
            null,
            null,
            null,
            errorCode,
            AssessmentRedactor.RedactText(errorMessage),
            cancellationToken);

    private async Task UpdateAsync(
        string planId,
        string status,
        DateTimeOffset completedAtUtc,
        string? sourceFingerprint,
        string? planHash,
        string? reportJson,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE cutover_plan_runs
            SET status = $status,
                completed_at_utc = $completed_at_utc,
                source_fingerprint = $source_fingerprint,
                plan_hash = $plan_hash,
                report_json = $report_json,
                error_code = $error_code,
                error_message = $error_message
            WHERE plan_id = $plan_id;
            """;
        command.Parameters.AddWithValue("$plan_id", planId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$completed_at_utc",
            completedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$source_fingerprint",
            (object?)sourceFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$plan_hash",
            (object?)planHash ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$report_json",
            (object?)reportJson ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$error_code",
            (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$error_message",
            (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() =>
        new(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private
            }.ToString());
}
