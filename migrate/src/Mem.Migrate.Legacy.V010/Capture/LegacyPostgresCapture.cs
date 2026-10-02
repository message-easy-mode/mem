using System.Globalization;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Legacy.V010.Capture;

public sealed class LegacyPostgresCapture(
    IProcessRunner processRunner,
    IBinaryProcessRunner binaryProcessRunner)
{
    public async Task<long> EstimateDatabaseBytesAsync(
        AssessmentOptions options,
        LegacyDatabaseCandidateObservation candidate,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                options.DockerCommand,
                BuildPsqlArguments(
                    candidate,
                    "SELECT pg_database_size(current_database())::text;"),
                TimeSpan.FromSeconds(options.CommandTimeoutSeconds),
                MaximumOutputCharacters: 64 * 1024),
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not estimate the legacy PostgreSQL database size: {SafeFailure(result)}");
        }

        var firstLine = result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault();

        if (!long.TryParse(
                firstLine,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var bytes) ||
            bytes < 0)
        {
            throw new InvalidOperationException(
                "The legacy PostgreSQL database size response was invalid.");
        }

        return bytes;
    }

    public async Task CaptureDumpAsync(
        AssessmentOptions options,
        LegacyDatabaseCandidateObservation candidate,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var result = await binaryProcessRunner.RunToFileAsync(
            new BinaryProcessRequest(
                options.DockerCommand,
                [
                    "exec",
                    "-i",
                    candidate.ContainerId,
                    "pg_dump",
                    "--format=custom",
                    "--no-owner",
                    "--no-privileges",
                    "--schema=app",
                    "--username",
                    candidate.DatabaseUser,
                    "--dbname",
                    candidate.DatabaseName
                ],
                destinationPath,
                TimeSpan.FromHours(6)),
            cancellationToken);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Legacy PostgreSQL pg_dump failed: {SafeFailure(result)}");
        }

        PrivateFilePermissions.EnsureFile(destinationPath);
    }

    private static string[] BuildPsqlArguments(
        LegacyDatabaseCandidateObservation candidate,
        string sql) =>
        [
            "exec",
            "-i",
            candidate.ContainerId,
            "psql",
            "--no-psqlrc",
            "--set",
            "ON_ERROR_STOP=1",
            "--tuples-only",
            "--no-align",
            "--username",
            candidate.DatabaseUser,
            "--dbname",
            candidate.DatabaseName,
            "--command",
            sql
        ];

    private static string SafeFailure(ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return AssessmentRedactor.RedactText(result.StandardError.Trim());
        }

        return result.ExitCode is null
            ? "the process did not start"
            : $"exit code {result.ExitCode}";
    }

    private static string SafeFailure(BinaryProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return AssessmentRedactor.RedactText(result.StandardError.Trim());
        }

        return result.ExitCode is null
            ? "the process did not start"
            : $"exit code {result.ExitCode}";
    }
}
