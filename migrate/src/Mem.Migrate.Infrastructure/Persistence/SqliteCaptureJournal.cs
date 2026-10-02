using System.Globalization;
using Microsoft.Data.Sqlite;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteCaptureJournal : ICaptureJournal
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteCaptureJournal(string workspacePath)
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
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;
            PRAGMA synchronous = FULL;

            CREATE TABLE IF NOT EXISTS capture_runs (
                capture_id TEXT PRIMARY KEY,
                status TEXT NOT NULL,
                started_at_utc TEXT NOT NULL,
                completed_at_utc TEXT NULL,
                source_fingerprint TEXT NOT NULL,
                archive_path TEXT NULL,
                archive_sha256 TEXT NULL,
                report_json TEXT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_capture_runs_started
                ON capture_runs(started_at_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task<StoredCaptureRun?> GetAsync(
        string captureId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                capture_id,
                status,
                started_at_utc,
                completed_at_utc,
                source_fingerprint,
                archive_path,
                archive_sha256,
                report_json,
                error_code,
                error_message
            FROM capture_runs
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredCaptureRun(
            CaptureId: reader.GetString(0),
            Status: ParseStatus(reader.GetString(1)),
            StartedAtUtc: ParseTimestamp(reader.GetString(2)),
            CompletedAtUtc: reader.IsDBNull(3)
                ? null
                : ParseTimestamp(reader.GetString(3)),
            SourceFingerprint: reader.GetString(4),
            ArchivePath: reader.IsDBNull(5) ? null : reader.GetString(5),
            ArchiveSha256: reader.IsDBNull(6) ? null : reader.GetString(6),
            ReportJson: reader.IsDBNull(7) ? null : reader.GetString(7),
            ErrorCode: reader.IsDBNull(8) ? null : reader.GetString(8),
            ErrorMessage: reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    public async Task StartAsync(
        string captureId,
        DateTimeOffset startedAtUtc,
        string sourceFingerprint,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO capture_runs (
                capture_id,
                status,
                started_at_utc,
                source_fingerprint
            )
            VALUES (
                $captureId,
                $status,
                $startedAtUtc,
                $sourceFingerprint
            )
            ON CONFLICT(capture_id) DO UPDATE SET
                status = excluded.status,
                started_at_utc = excluded.started_at_utc,
                completed_at_utc = NULL,
                source_fingerprint = excluded.source_fingerprint,
                archive_path = NULL,
                archive_sha256 = NULL,
                report_json = NULL,
                error_code = NULL,
                error_message = NULL;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        command.Parameters.AddWithValue(
            "$status",
            CaptureLifecycleStatus.Running.ToString());
        command.Parameters.AddWithValue("$startedAtUtc", startedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$sourceFingerprint", sourceFingerprint);
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task CompleteAsync(
        CaptureReport report,
        string reportJson,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE capture_runs
            SET
                status = $status,
                completed_at_utc = $completedAtUtc,
                archive_path = $archivePath,
                archive_sha256 = $archiveSha256,
                report_json = $reportJson,
                error_code = NULL,
                error_message = NULL
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", report.CaptureId);
        command.Parameters.AddWithValue("$status", report.Status.ToString());
        command.Parameters.AddWithValue(
            "$completedAtUtc",
            report.CompletedAtUtc.ToString("O"));
        var retainedArchivePath = report.PlaintextArchiveRetained
            ? report.ArchivePath
            : report.EncryptedArchivePath;
        var retainedArchiveSha256 = report.PlaintextArchiveRetained
            ? report.ArchiveSha256
            : report.EncryptedArchiveSha256;
        command.Parameters.AddWithValue(
            "$archivePath",
            retainedArchivePath
                ?? throw new InvalidOperationException(
                    "A completed capture has no retained archive path."));
        command.Parameters.AddWithValue(
            "$archiveSha256",
            retainedArchiveSha256
                ?? throw new InvalidOperationException(
                    "A completed capture has no retained archive SHA-256."));
        command.Parameters.AddWithValue("$reportJson", reportJson);
        var changed = await command.ExecuteNonQueryAsync(cancellationToken);

        if (changed != 1)
        {
            throw new InvalidOperationException(
                "The capture journal did not contain the running capture.");
        }

        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    public async Task FailAsync(
        string captureId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE capture_runs
            SET
                status = $status,
                completed_at_utc = $completedAtUtc,
                error_code = $errorCode,
                error_message = $errorMessage
            WHERE capture_id = $captureId;
            """;
        command.Parameters.AddWithValue("$captureId", captureId);
        var status = string.Equals(
                errorCode,
                "capture_cancelled",
                StringComparison.Ordinal)
            ? CaptureLifecycleStatus.Cancelled
            : CaptureLifecycleStatus.Failed;
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue(
            "$completedAtUtc",
            completedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$errorCode", errorCode);
        command.Parameters.AddWithValue("$errorMessage", errorMessage);
        await command.ExecuteNonQueryAsync(cancellationToken);
        PrivateFilePermissions.EnsureFile(_databasePath);
    }

    private static CaptureLifecycleStatus ParseStatus(string value) =>
        Enum.TryParse<CaptureLifecycleStatus>(value, ignoreCase: true, out var parsed)
            ? parsed
            : CaptureLifecycleStatus.Failed;

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
