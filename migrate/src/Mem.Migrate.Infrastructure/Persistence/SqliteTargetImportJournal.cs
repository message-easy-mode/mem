using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteTargetImportJournal(string workspacePath) :
    ITargetImportJournal
{
    private readonly string _databasePath = Path.Combine(
        Path.GetFullPath(workspacePath),
        "target-import-journal.db");

    public async Task InitializeAsync(
        CancellationToken cancellationToken)
    {
        PrivateFilePermissions.EnsureDirectory(
            Path.GetDirectoryName(_databasePath)!);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS target_import_runs (
                    attempt_id TEXT PRIMARY KEY,
                    status TEXT NOT NULL,
                    started_at_utc TEXT NOT NULL,
                    completed_at_utc TEXT NULL,
                    manifest_sha256 TEXT NOT NULL,
                    stack_export_sha256 TEXT NOT NULL,
                    target_profile_name TEXT NULL,
                    target_base_url TEXT NULL,
                    intake_id TEXT NULL,
                    validation_id TEXT NULL,
                    catalog_entry_id TEXT NULL,
                    artifact_id TEXT NULL,
                    report_json TEXT NULL,
                    error_code TEXT NULL,
                    error_message TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureColumnAsync(
            connection,
            "target_profile_name",
            cancellationToken);
        await EnsureColumnAsync(
            connection,
            "target_base_url",
            cancellationToken);

        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredTargetImportRun?> GetAsync(
        string attemptId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                status,
                started_at_utc,
                completed_at_utc,
                manifest_sha256,
                stack_export_sha256,
                target_profile_name,
                target_base_url,
                intake_id,
                validation_id,
                catalog_entry_id,
                artifact_id,
                report_json,
                error_code,
                error_message
            FROM target_import_runs
            WHERE attempt_id=$id
            """;
        command.Parameters.AddWithValue("$id", attemptId);

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredTargetImportRun(
            attemptId,
            reader.GetString(0),
            DateTimeOffset.Parse(
                reader.GetString(1),
                null,
                System.Globalization.DateTimeStyles.RoundtripKind),
            reader.IsDBNull(2)
                ? null
                : DateTimeOffset.Parse(
                    reader.GetString(2),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    public async Task StartAsync(
        string attemptId,
        DateTimeOffset startedAtUtc,
        string manifestSha256,
        string stackExportSha256,
        string targetProfileName,
        string targetBaseUrl,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO target_import_runs(
                attempt_id,
                status,
                started_at_utc,
                manifest_sha256,
                stack_export_sha256,
                target_profile_name,
                target_base_url)
            VALUES(
                $id,
                'Running',
                $started,
                $manifest,
                $zip,
                $profile,
                $target)
            """;
        command.Parameters.AddWithValue("$id", attemptId);
        command.Parameters.AddWithValue(
            "$started",
            startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$manifest", manifestSha256);
        command.Parameters.AddWithValue("$zip", stackExportSha256);
        command.Parameters.AddWithValue("$profile", targetProfileName);
        command.Parameters.AddWithValue("$target", targetBaseUrl);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveProgressAsync(
        string attemptId,
        string? intakeId,
        string? validationId,
        string? catalogEntryId,
        string? artifactId,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE target_import_runs
            SET
                intake_id=COALESCE($intake,intake_id),
                validation_id=COALESCE($validation,validation_id),
                catalog_entry_id=COALESCE($catalog,catalog_entry_id),
                artifact_id=COALESCE($artifact,artifact_id)
            WHERE attempt_id=$id
            """;
        command.Parameters.AddWithValue("$id", attemptId);
        command.Parameters.AddWithValue(
            "$intake",
            (object?)intakeId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$validation",
            (object?)validationId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$catalog",
            (object?)catalogEntryId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$artifact",
            (object?)artifactId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(
        TargetImportReport report,
        string reportJson,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            report.AttemptId,
            "Completed",
            report.CompletedAtUtc,
            reportJson,
            null,
            null,
            cancellationToken);

    public Task FailAsync(
        string attemptId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken) =>
        UpdateAsync(
            attemptId,
            "Failed",
            completedAtUtc,
            null,
            errorCode,
            AssessmentRedactor.RedactText(errorMessage),
            cancellationToken);

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(target_import_runs)";
        await using var reader = await inspect.ExecuteReaderAsync(
            cancellationToken);

        var exists = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(
                    reader.GetString(1),
                    columnName,
                    StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        await reader.DisposeAsync();

        if (exists)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = columnName switch
        {
            "target_profile_name" =>
                "ALTER TABLE target_import_runs ADD COLUMN " +
                "target_profile_name TEXT NULL",
            "target_base_url" =>
                "ALTER TABLE target_import_runs ADD COLUMN " +
                "target_base_url TEXT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(columnName))
        };
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpdateAsync(
        string id,
        string status,
        DateTimeOffset completed,
        string? report,
        string? errorCode,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE target_import_runs
            SET
                status=$status,
                completed_at_utc=$completed,
                report_json=$report,
                error_code=$code,
                error_message=$message
            WHERE attempt_id=$id
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue(
            "$completed",
            completed.ToString("O"));
        command.Parameters.AddWithValue(
            "$report",
            (object?)report ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$code",
            (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$message",
            (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() =>
        new($"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Private");
}
