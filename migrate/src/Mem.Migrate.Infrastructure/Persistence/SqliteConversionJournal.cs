using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Security;
using Microsoft.Data.Sqlite;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteConversionJournal(string workspacePath) : IConversionJournal
{
    private readonly string _databasePath = Path.Combine(
        Path.GetFullPath(workspacePath), "conversion-journal.db");

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        PrivateFilePermissions.EnsureDirectory(Path.GetDirectoryName(_databasePath)!);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS conversion_runs (
                conversion_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                archive_sha256 TEXT NOT NULL,
                source_stack_id TEXT NOT NULL,
                report_json TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredConversionRun?> GetAsync(string conversionId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, started_at_utc, completed_at_utc, archive_sha256, source_stack_id, report_json, error_code, error_message FROM conversion_runs WHERE conversion_id = $id";
        command.Parameters.AddWithValue("$id", conversionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new StoredConversionRun(
            conversionId,
            Enum.Parse<ConversionLifecycleStatus>(reader.GetString(0)),
            DateTimeOffset.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind),
            reader.GetString(3),
            Guid.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }

    public async Task StartAsync(string conversionId, DateTimeOffset startedAtUtc, string archiveSha256, Guid sourceStackId, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO conversion_runs(conversion_id,status,started_at_utc,archive_sha256,source_stack_id) VALUES($id,$status,$started,$sha,$stack)";
        command.Parameters.AddWithValue("$id", conversionId);
        command.Parameters.AddWithValue("$status", ConversionLifecycleStatus.Running.ToString());
        command.Parameters.AddWithValue("$started", startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$sha", archiveSha256);
        command.Parameters.AddWithValue("$stack", sourceStackId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task CompleteAsync(ConversionReport report, string reportJson, CancellationToken cancellationToken) =>
        UpdateAsync(report.ConversionId, ConversionLifecycleStatus.Completed, report.CompletedAtUtc, reportJson, null, null, cancellationToken);

    public Task FailAsync(string conversionId, DateTimeOffset completedAtUtc, string errorCode, string errorMessage, CancellationToken cancellationToken) =>
        UpdateAsync(conversionId, ConversionLifecycleStatus.Failed, completedAtUtc, null, errorCode, AssessmentRedactor.RedactText(errorMessage), cancellationToken);

    private async Task UpdateAsync(string id, ConversionLifecycleStatus status, DateTimeOffset completed, string? report, string? errorCode, string? errorMessage, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE conversion_runs SET status=$status, completed_at_utc=$completed, report_json=$report, error_code=$code, error_message=$message WHERE conversion_id=$id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$completed", completed.ToString("O"));
        command.Parameters.AddWithValue("$report", (object?)report ?? DBNull.Value);
        command.Parameters.AddWithValue("$code", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$message", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() => new($"Data Source={_databasePath};Mode=ReadWriteCreate;Cache=Private");
}
