using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Security;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteSourceRestorationJournal(string workspacePath)
    : ISourceRestorationJournal
{
    private readonly string _databasePath = Path.Combine(
        Path.GetFullPath(workspacePath),
        "source-restoration-journal.db");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        PrivateFilePermissions.EnsureDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS source_restoration_runs (
                restoration_attempt_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                source_handoff_id TEXT NOT NULL,
                source_handoff_sha256 TEXT NOT NULL,
                freeze_attempt_id TEXT NOT NULL,
                report_json TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredSourceRestorationRun?> GetAsync(
        string restorationAttemptId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT status, started_at_utc, completed_at_utc,
                   source_handoff_id, source_handoff_sha256, freeze_attempt_id,
                   report_json, error_code, error_message
            FROM source_restoration_runs
            WHERE restoration_attempt_id = $id;
            """;
        command.Parameters.AddWithValue("$id", restorationAttemptId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredSourceRestorationRun(
            restorationAttemptId,
            reader.GetString(0),
            DateTimeOffset.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8));
    }

    public async Task StartAsync(
        string restorationAttemptId,
        DateTimeOffset startedAtUtc,
        string sourceHandoffId,
        string sourceHandoffSha256,
        string freezeAttemptId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO source_restoration_runs (
                restoration_attempt_id, status, started_at_utc,
                source_handoff_id, source_handoff_sha256, freeze_attempt_id)
            VALUES ($id, 'Running', $started, $handoff_id, $handoff_sha, $freeze_id);
            """;
        command.Parameters.AddWithValue("$id", restorationAttemptId);
        command.Parameters.AddWithValue("$started", startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$handoff_id", sourceHandoffId);
        command.Parameters.AddWithValue("$handoff_sha", sourceHandoffSha256);
        command.Parameters.AddWithValue("$freeze_id", freezeAttemptId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(
        SourceRestorationReport report,
        string reportJson,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            report.RestorationAttemptId,
            "Completed",
            report.CompletedAtUtc,
            reportJson,
            null,
            null,
            cancellationToken);

    public Task FailAsync(
        string restorationAttemptId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            restorationAttemptId,
            "Failed",
            completedAtUtc,
            null,
            errorCode,
            AssessmentRedactor.RedactText(errorMessage),
            cancellationToken);

    private async Task UpdateAsync(
        string id,
        string status,
        DateTimeOffset completedAtUtc,
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
            UPDATE source_restoration_runs
            SET status = $status,
                completed_at_utc = $completed,
                report_json = $report,
                error_code = $error_code,
                error_message = $error_message
            WHERE restoration_attempt_id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$completed", completedAtUtc.ToString("O"));
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
