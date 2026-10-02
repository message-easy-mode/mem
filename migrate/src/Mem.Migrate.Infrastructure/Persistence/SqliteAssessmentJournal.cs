using Microsoft.Data.Sqlite;
using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SqliteAssessmentJournal : IAssessmentJournal
{
    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteAssessmentJournal(string workspacePath)
    {
        var fullWorkspace = Path.GetFullPath(workspacePath);
        Directory.CreateDirectory(fullWorkspace);
        EnsureDirectoryPrivate(fullWorkspace);

        _databasePath = Path.Combine(fullWorkspace, "mem-migrate.sqlite");

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

            CREATE TABLE IF NOT EXISTS assessment_runs (
                assessment_id TEXT PRIMARY KEY,
                completed_at_utc TEXT NOT NULL,
                classification TEXT NOT NULL,
                source_fingerprint TEXT NOT NULL,
                public_json TEXT NOT NULL,
                public_markdown TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_assessment_runs_completed
                ON assessment_runs(completed_at_utc DESC);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        EnsureFilePrivate(_databasePath);
    }

    public async Task SaveAsync(
        AssessmentResult result,
        string publicJson,
        string publicMarkdown,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO assessment_runs (
                assessment_id,
                completed_at_utc,
                classification,
                source_fingerprint,
                public_json,
                public_markdown
            )
            VALUES (
                $assessmentId,
                $completedAtUtc,
                $classification,
                $sourceFingerprint,
                $publicJson,
                $publicMarkdown
            )
            ON CONFLICT(assessment_id) DO UPDATE SET
                completed_at_utc = excluded.completed_at_utc,
                classification = excluded.classification,
                source_fingerprint = excluded.source_fingerprint,
                public_json = excluded.public_json,
                public_markdown = excluded.public_markdown;
            """;

        command.Parameters.AddWithValue(
            "$assessmentId",
            result.AssessmentId);
        command.Parameters.AddWithValue(
            "$completedAtUtc",
            result.CompletedAtUtc.ToString("O"));
        command.Parameters.AddWithValue(
            "$classification",
            result.Classification.ToString());
        command.Parameters.AddWithValue(
            "$sourceFingerprint",
            result.SourceFingerprint);
        command.Parameters.AddWithValue("$publicJson", publicJson);
        command.Parameters.AddWithValue("$publicMarkdown", publicMarkdown);

        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
        EnsureFilePrivate(_databasePath);
    }

    public async Task<StoredAssessmentReport?> GetLatestAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                assessment_id,
                completed_at_utc,
                classification,
                source_fingerprint,
                public_json,
                public_markdown
            FROM assessment_runs
            ORDER BY completed_at_utc DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var classificationText = reader.GetString(2);

        if (!Enum.TryParse<AssessmentClassification>(
                classificationText,
                ignoreCase: true,
                out var classification))
        {
            classification = AssessmentClassification.ExecutionFailed;
        }

        return new StoredAssessmentReport(
            AssessmentId: reader.GetString(0),
            CompletedAtUtc: DateTimeOffset.Parse(
                reader.GetString(1),
                System.Globalization.CultureInfo.InvariantCulture),
            Classification: classification,
            SourceFingerprint: reader.GetString(3),
            PublicJson: reader.GetString(4),
            PublicMarkdown: reader.GetString(5));
    }

    private static void EnsureDirectoryPrivate(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not enforce private permissions on workspace '{path}'.",
                ex);
        }
    }

    private static void EnsureFilePrivate(string path)
    {
        if (!File.Exists(path) ||
            (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Could not enforce private permissions on journal '{path}'.",
                ex);
        }
    }
}
