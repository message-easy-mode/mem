using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Persistence;

public sealed class SourceCaptureArchiveDiscovery(string workspacePath)
    : ISourceCaptureArchiveDiscovery
{
    private readonly string _workspacePath = Path.GetFullPath(workspacePath);

    public async Task<IReadOnlyList<EligibleSourceCapture>> ListEligibleArchivesAsync(
        CancellationToken cancellationToken,
        bool requireFinalFrozen = false)
    {
        var databasePath = Path.Combine(_workspacePath, "mem-migrate.sqlite");
        if (!File.Exists(databasePath))
        {
            return [];
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString();

        var candidates = new List<EligibleSourceCapture>();
        try
        {
            await using var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            if (await IsCurrentJournalAwaitingFirstCaptureAsync(
                    connection,
                    cancellationToken))
            {
                return [];
            }

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    capture_id,
                    completed_at_utc,
                    archive_path,
                    report_json
                FROM capture_runs
                WHERE status = $status
                ORDER BY completed_at_utc DESC;
                """;
            command.Parameters.AddWithValue(
                "$status",
                CaptureLifecycleStatus.Completed.ToString());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
                {
                    continue;
                }

                try
                {
                    var captureId = reader.GetString(0);
                    var completedAtUtc = DateTimeOffset.Parse(
                        reader.GetString(1),
                        CultureInfo.InvariantCulture);
                    var journalArchivePath = Path.GetFullPath(reader.GetString(2));
                    var report = JsonSerializer.Deserialize<CaptureReport>(
                        reader.GetString(3),
                        CaptureJson.Options);

                    if (report is null ||
                        report.SchemaVersion != 2 ||
                        report.SourceStackId == Guid.Empty ||
                        string.IsNullOrWhiteSpace(report.SourceStackSlug) ||
                        string.IsNullOrWhiteSpace(report.MatrixServerName) ||
                        report.Status != CaptureLifecycleStatus.Completed ||
                        report.SourceChangedDuringCapture ||
                        !report.PlaintextArchiveRetained ||
                        requireFinalFrozen && report.RehearsalOnly)
                    {
                        continue;
                    }

                    var archivePath = Path.GetFullPath(report.ArchivePath);
                    if (!archivePath.EndsWith(
                            ".memmigration.zip",
                            StringComparison.OrdinalIgnoreCase) ||
                        !File.Exists(archivePath) ||
                        !string.Equals(
                            journalArchivePath,
                            archivePath,
                            OperatingSystem.IsWindows()
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var archiveInfo = new FileInfo(archivePath);
                    candidates.Add(new EligibleSourceCapture(
                        CaptureId: captureId,
                        SourceStackId: report.SourceStackId,
                        SourceStackSlug: report.SourceStackSlug,
                        MatrixServerName: report.MatrixServerName,
                        CompletedAtUtc: completedAtUtc,
                        ArchivePath: archivePath,
                        ArchiveBytes: archiveInfo.Length,
                        ArchiveSha256: report.ArchiveSha256,
                        SourceFingerprint: report.StartSourceFingerprint,
                        RehearsalOnly: report.RehearsalOnly,
                        StableSourceIdentity: report.StableSourceIdentity));
                }
                catch (Exception exception) when (
                    exception is JsonException or FormatException or
                    ArgumentException or NotSupportedException or IOException)
                {
                    // A malformed or unavailable historical row is never eligible.
                }
            }
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException(
                $"The source journal in '{_workspacePath}' could not be read as a current capture journal. " +
                "Use --workspace to select the workspace used by the current mem-migrate capture, " +
                "or --archive to select a verified plaintext migration archive explicitly.",
                exception);
        }

        return candidates;
    }

    private static async Task<bool> IsCurrentJournalAwaitingFirstCaptureAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                EXISTS (
                    SELECT 1
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name = 'capture_runs'
                ),
                EXISTS (
                    SELECT 1
                    FROM sqlite_master
                    WHERE type = 'table'
                      AND name IN (
                          'assessment_runs',
                          'source_workflow_runs',
                          'journal_schema_migrations'
                      )
                );
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        var captureJournalExists = reader.GetInt64(0) != 0;
        var currentJournalEvidenceExists = reader.GetInt64(1) != 0;
        return !captureJournalExists && currentJournalEvidenceExists;
    }

    public async Task<EligibleSourceCapture> ResolveEligibleArchiveAsync(
        string captureId,
        CancellationToken cancellationToken,
        bool requireFinalFrozen = false)
    {
        if (!CaptureIdentifier.IsValid(captureId))
        {
            throw new ArgumentException(
                "Capture ID contains unsupported characters.",
                nameof(captureId));
        }

        var candidates = await ListEligibleArchivesAsync(
            cancellationToken,
            requireFinalFrozen);
        var selected = candidates.FirstOrDefault(candidate =>
            string.Equals(
                candidate.CaptureId,
                captureId,
                StringComparison.Ordinal));

        return selected ?? throw new InvalidOperationException(
            requireFinalFrozen
                ? "The selected capture is not an eligible final frozen plaintext source capture."
                : "The selected capture is not an eligible completed plaintext source capture.");
    }

    public async Task<string> ResolveSingleEligibleArchiveAsync(
        CancellationToken cancellationToken,
        bool requireFinalFrozen = false)
    {
        var databasePath = Path.Combine(_workspacePath, "mem-migrate.sqlite");
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException(
                $"No source-capture journal was found in '{_workspacePath}'. " +
                "Run a source capture first, or use --workspace or --archive as an advanced override.");
        }

        var candidates = (await ListEligibleArchivesAsync(
                cancellationToken,
                requireFinalFrozen))
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                requireFinalFrozen
                    ? $"No eligible final frozen plaintext source capture was found in '{_workspacePath}'. " +
                      "Run source capture with --final-frozen and its reviewed freeze report first, " +
                      "or use --archive as an advanced override."
                    : $"No eligible completed plaintext source capture was found in '{_workspacePath}'. " +
                      "The normal package command requires one retained plaintext capture that completed without source drift. " +
                      "Run a source capture first, or use --archive as an advanced override.");
        }

        var finalCandidates = candidates
            .Where(candidate => !candidate.RehearsalOnly)
            .ToArray();
        var selectionPool = requireFinalFrozen
            ? finalCandidates
            : finalCandidates.Length > 0
                ? finalCandidates
                : candidates;

        if (selectionPool.Length > 1)
        {
            var choices = string.Join(
                Environment.NewLine,
                selectionPool.Select(candidate =>
                    $"  {candidate.CaptureId} ({candidate.CompletedAtUtc:O}, " +
                    $"{(candidate.RehearsalOnly ? "live rehearsal" : "final frozen")}): " +
                    candidate.ArchivePath));
            throw new InvalidOperationException(
                (requireFinalFrozen
                    ? "Multiple eligible final frozen plaintext source captures were found. "
                    : "Multiple eligible plaintext source captures were found. ") +
                "Re-run with --archive <path> to select one explicitly." +
                Environment.NewLine + choices);
        }

        return selectionPool[0].ArchivePath;
    }
}

public static class SourceWorkspaceLocator
{
    private const string WorkspaceEnvironmentVariable = "MEM_MIGRATE_WORKSPACE";

    public static string Resolve(string? explicitWorkspacePath)
    {
        if (!string.IsNullOrWhiteSpace(explicitWorkspacePath))
        {
            return Path.GetFullPath(explicitWorkspacePath);
        }

        var environmentWorkspace = Environment.GetEnvironmentVariable(
            WorkspaceEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentWorkspace))
        {
            return Path.GetFullPath(environmentWorkspace);
        }

        var pointerPath = GetPointerPath();
        try
        {
            if (File.Exists(pointerPath))
            {
                var remembered = File.ReadAllText(pointerPath).Trim();
                if (remembered.Length > 0 &&
                    File.Exists(Path.Combine(remembered, "mem-migrate.sqlite")))
                {
                    return Path.GetFullPath(remembered);
                }
            }
        }
        catch (IOException)
        {
            // Fall back to conventional workspaces.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall back to conventional workspaces.
        }

        foreach (var candidate in GetConventionalWorkspaceCandidates())
        {
            if (File.Exists(Path.Combine(candidate, "mem-migrate.sqlite")))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            ".workspace"));
    }

    public static void TryRemember(string workspacePath)
    {
        try
        {
            var fullWorkspacePath = Path.GetFullPath(workspacePath);
            var pointerPath = GetPointerPath();
            var pointerDirectory = Path.GetDirectoryName(pointerPath)
                ?? throw new IOException(
                    "The mem-migrate workspace pointer had no parent directory.");
            PrivateFilePermissions.EnsureDirectory(pointerDirectory);

            var partialPath = pointerPath + ".partial";
            TryDelete(partialPath);
            File.WriteAllText(partialPath, fullWorkspacePath + Environment.NewLine);
            PrivateFilePermissions.EnsureFile(partialPath);
            File.Move(partialPath, pointerPath, overwrite: true);
            PrivateFilePermissions.EnsureFile(pointerPath);
        }
        catch (IOException)
        {
            // Workspace remembrance is a convenience. Assessment and capture
            // remain authoritative even when the stable pointer cannot be written.
        }
        catch (UnauthorizedAccessException)
        {
            // Workspace remembrance is a convenience. Assessment and capture
            // remain authoritative even when the stable pointer cannot be written.
        }
    }

    private static IEnumerable<string> GetConventionalWorkspaceCandidates()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.GetFullPath(Path.Combine(
                home,
                "mem-migrate-work"));
        }

        yield return Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            ".workspace"));
    }

    private static string GetPointerPath()
    {
        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrWhiteSpace(stateHome))
        {
            var home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
            stateHome = Path.Combine(home, ".local", "state");
        }

        return Path.Combine(
            stateHome,
            "mem-migrate",
            "source-workspace");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup before replacing the pointer.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup before replacing the pointer.
        }
    }
}
