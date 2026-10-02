using System.Text;

namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

internal sealed record PrivateStagingDatabaseImportPlan(
    string DumpFormat,
    string ImportMechanism,
    string WorkspaceFileName,
    string ContainerPath)
{
    public IReadOnlyList<string> BuildCommand(string databaseUser, string databaseName) =>
        DumpFormat switch
        {
            PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql =>
            [
                "psql",
                "-v", "ON_ERROR_STOP=1",
                "-U", databaseUser,
                "-d", databaseName,
                "-f", ContainerPath
            ],
            PrivateStagingDatabaseDumpFormats.PostgreSqlCustom =>
            [
                "pg_restore",
                "--exit-on-error",
                "--no-owner",
                "--no-privileges",
                "--username", databaseUser,
                "--dbname", databaseName,
                ContainerPath
            ],
            _ => throw new InvalidOperationException(
                $"Private staging database dump format '{DumpFormat}' is unsupported.")
        };
}

internal static class PrivateStagingDatabaseImportPolicy
{
    private static readonly byte[] PostgreSqlCustomSignature = Encoding.ASCII.GetBytes("PGDMP");

    public static string ResolveMigrationCandidateDumpFormat(string? artifactKind) =>
        artifactKind switch
        {
            "synapse-postgresql-conversion" =>
                PrivateStagingDatabaseDumpFormats.PostgreSqlCustom,
            null or "" => throw new InvalidDataException(
                "Migration candidate artifact kind is missing."),
            _ => throw new InvalidDataException(
                $"Migration candidate artifact kind '{artifactKind}' does not declare a supported private-staging database format.")
        };

    public static PrivateStagingDatabaseImportPlan Resolve(string? dumpFormat) =>
        dumpFormat switch
        {
            PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql =>
                new PrivateStagingDatabaseImportPlan(
                    DumpFormat: PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql,
                    ImportMechanism: "psql",
                    WorkspaceFileName: "synapse.sql",
                    ContainerPath: "/restore/database/synapse.sql"),
            PrivateStagingDatabaseDumpFormats.PostgreSqlCustom =>
                new PrivateStagingDatabaseImportPlan(
                    DumpFormat: PrivateStagingDatabaseDumpFormats.PostgreSqlCustom,
                    ImportMechanism: "pg_restore",
                    WorkspaceFileName: "synapse.dump",
                    ContainerPath: "/restore/database/synapse.dump"),
            null or "" => throw new InvalidDataException(
                "Private staging database dump format is missing from trusted source material."),
            _ => throw new InvalidDataException(
                $"Private staging database dump format '{dumpFormat}' is unsupported.")
        };

    public static void EnsureImportSucceeded(bool importSucceeded)
    {
        if (!importSucceeded)
        {
            throw new InvalidOperationException(
                "Private staging database import failed. Synapse was not started.");
        }
    }

    public static void ValidateCopiedDump(
        PrivateStagingDatabaseImportPlan plan,
        string databaseDumpPath)
    {
        if (!File.Exists(databaseDumpPath))
        {
            throw new FileNotFoundException(
                "Private staging database dump was not copied into the workspace.",
                databaseDumpPath);
        }

        var length = new FileInfo(databaseDumpPath).Length;
        if (length <= 0)
        {
            throw new InvalidDataException("Private staging database dump is empty.");
        }

        var hasCustomSignature = HasPostgreSqlCustomSignature(databaseDumpPath);

        if (plan.DumpFormat == PrivateStagingDatabaseDumpFormats.PostgreSqlCustom &&
            !hasCustomSignature)
        {
            throw new InvalidDataException(
                "Trusted source material declared a PostgreSQL custom-format dump, but the copied payload does not begin with the PGDMP signature.");
        }

        if (plan.DumpFormat == PrivateStagingDatabaseDumpFormats.PostgreSqlPlainSql &&
            hasCustomSignature)
        {
            throw new InvalidDataException(
                "Trusted source material declared a PostgreSQL plain-SQL dump, but the copied payload has the PGDMP custom-format signature.");
        }
    }

    private static bool HasPostgreSqlCustomSignature(string path)
    {
        Span<byte> header = stackalloc byte[5];
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        return stream.Read(header) == header.Length &&
               header.SequenceEqual(PostgreSqlCustomSignature);
    }
}
