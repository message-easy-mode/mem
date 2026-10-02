using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteTargetPrivateStageJournal(string workspacePath) : ITargetPrivateStageJournal
{
    private readonly string _databasePath = Path.Combine(Path.GetFullPath(workspacePath), "target-private-stage-journal.db");

    public async Task InitializeAsync(CancellationToken ct)
    {
        PrivateFilePermissions.EnsureDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS target_private_stage_runs (
                stage_attempt_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                import_attempt_id TEXT NOT NULL,
                target_profile_name TEXT NOT NULL,
                target_base_url TEXT NOT NULL,
                catalog_entry_id TEXT NOT NULL,
                restore_session_id TEXT NULL,
                staging_id TEXT NULL,
                report_json TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(ct);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredTargetPrivateStageRun?> GetAsync(string id, CancellationToken ct)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT status,started_at_utc,completed_at_utc,import_attempt_id,target_profile_name,target_base_url,catalog_entry_id,restore_session_id,staging_id,report_json,error_code,error_message
            FROM target_private_stage_runs WHERE stage_attempt_id=$id
            """;
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new StoredTargetPrivateStageRun(
            id, reader.GetString(0), DateTimeOffset.Parse(reader.GetString(1)),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    public async Task StartAsync(string id, DateTimeOffset started, string importId, string profile, string target, string catalog, CancellationToken ct)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO target_private_stage_runs(stage_attempt_id,status,started_at_utc,import_attempt_id,target_profile_name,target_base_url,catalog_entry_id)
            VALUES($id,'Running',$started,$import,$profile,$target,$catalog)
            """;
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$started", started.ToString("O"));
        command.Parameters.AddWithValue("$import", importId); command.Parameters.AddWithValue("$profile", profile);
        command.Parameters.AddWithValue("$target", target); command.Parameters.AddWithValue("$catalog", catalog);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveProgressAsync(string id, string? restore, string? staging, CancellationToken ct)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE target_private_stage_runs SET restore_session_id=COALESCE($restore,restore_session_id), staging_id=COALESCE($staging,staging_id) WHERE stage_attempt_id=$id";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$restore", (object?)restore ?? DBNull.Value); command.Parameters.AddWithValue("$staging", (object?)staging ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public Task CompleteAsync(TargetPrivateStageReport report, string json, CancellationToken ct) => UpdateAsync(report.StageAttemptId, "Completed", report.CompletedAtUtc, json, null, null, ct);
    public Task FailAsync(string id, DateTimeOffset completed, string code, string message, CancellationToken ct) => UpdateAsync(id, "Failed", completed, null, code, AssessmentRedactor.RedactText(message), ct);

    private async Task UpdateAsync(string id, string status, DateTimeOffset completed, string? json, string? code, string? message, CancellationToken ct)
    {
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE target_private_stage_runs SET status=$status,completed_at_utc=$completed,report_json=$json,error_code=$code,error_message=$message WHERE stage_attempt_id=$id";
        command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$status", status); command.Parameters.AddWithValue("$completed", completed.ToString("O"));
        command.Parameters.AddWithValue("$json", (object?)json ?? DBNull.Value); command.Parameters.AddWithValue("$code", (object?)code ?? DBNull.Value); command.Parameters.AddWithValue("$message", (object?)message ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private }.ToString());
}
