using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteCutoverFreezeJournal(string workspacePath)
    : ICutoverFreezeJournal
{
    private readonly string _databasePath = Path.Combine(
        Path.GetFullPath(workspacePath),
        "cutover-freeze-journal.db");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        PrivateFilePermissions.EnsureDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS cutover_freeze_runs (
                freeze_attempt_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                plan_id TEXT NOT NULL,
                plan_hash TEXT NOT NULL,
                source_fingerprint TEXT NULL,
                report_json TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredCutoverFreezeRun?> GetAsync(
        string freezeAttemptId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT status, started_at_utc, completed_at_utc, plan_id, plan_hash,
                   source_fingerprint, report_json, error_code, error_message
            FROM cutover_freeze_runs
            WHERE freeze_attempt_id = $id;
            """;
        command.Parameters.AddWithValue("$id", freezeAttemptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredCutoverFreezeRun(
            freezeAttemptId,
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    public async Task StartAsync(
        string freezeAttemptId,
        DateTimeOffset startedAtUtc,
        string planId,
        string planHash,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO cutover_freeze_runs (
                freeze_attempt_id, status, started_at_utc, plan_id, plan_hash)
            VALUES ($id, 'Running', $started, $plan_id, $plan_hash);
            """;
        command.Parameters.AddWithValue("$id", freezeAttemptId);
        command.Parameters.AddWithValue("$started", startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$plan_id", planId);
        command.Parameters.AddWithValue("$plan_hash", planHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(
        CutoverFreezeReport report,
        string reportJson,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            report.FreezeAttemptId,
            "Completed",
            report.CompletedAtUtc,
            report.SourceFingerprint,
            reportJson,
            null,
            null,
            cancellationToken);

    public Task FailAsync(
        string freezeAttemptId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            freezeAttemptId,
            "Failed",
            completedAtUtc,
            null,
            null,
            errorCode,
            AssessmentRedactor.RedactText(errorMessage),
            cancellationToken);

    private async Task UpdateAsync(
        string id,
        string status,
        DateTimeOffset completedAtUtc,
        string? sourceFingerprint,
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
            UPDATE cutover_freeze_runs
            SET status = $status,
                completed_at_utc = $completed,
                source_fingerprint = $fingerprint,
                report_json = $report,
                error_code = $error_code,
                error_message = $error_message
            WHERE freeze_attempt_id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completed", completedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$fingerprint", (object?)sourceFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$report", (object?)reportJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_code", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_message", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private
        }.ToString());
}
