using Microsoft.Data.Sqlite;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Sqlite;

public sealed class SqliteSnapshotter : ISqliteSnapshotter
{
    public async Task CreateConsistentSnapshotAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(sourcePath);
        var destination = Path.GetFullPath(destinationPath);
        var sourceInfo = new FileInfo(source);

        if (!sourceInfo.Exists)
        {
            throw new FileNotFoundException(
                "The Synapse SQLite database was not found.",
                source);
        }

        UnixFileTypeSafety.EnsureRegularFile(sourceInfo.FullName);

        if ((sourceInfo.Attributes & FileAttributes.ReparsePoint) != 0 ||
            sourceInfo.LinkTarget is not null)
        {
            throw new InvalidDataException(
                "The Synapse SQLite database may not be a symbolic link.");
        }

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "SQLite snapshot destination has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(parent);
        if (File.Exists(destination))
        {
            throw new IOException(
                $"SQLite snapshot destination already exists: {destination}");
        }

        var partialPath = destination + ".partial";
        DeletePartialArtifacts(partialPath);
        var ownsDestination = false;

        var sourceConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = source,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 60
        }.ToString();
        var destinationConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = partialPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 60
        }.ToString();

        try
        {
            await using (var sourceConnection =
                new SqliteConnection(sourceConnectionString))
            await using (var destinationConnection =
                new SqliteConnection(destinationConnectionString))
            {
                await sourceConnection.OpenAsync(cancellationToken);
                await destinationConnection.OpenAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                sourceConnection.BackupDatabase(destinationConnection);
            }

            await ValidateIntegrityAsync(partialPath, cancellationToken);
            DeleteTemporarySidecars(partialPath);
            PrivateFilePermissions.EnsureFile(partialPath);
            File.Move(partialPath, destination);
            ownsDestination = true;
            PrivateFilePermissions.EnsureFile(destination);
        }
        catch
        {
            DeletePartialArtifacts(partialPath);

            if (ownsDestination)
            {
                File.Delete(destination);
            }

            throw;
        }
    }

    private static void DeletePartialArtifacts(string partialPath)
    {
        File.Delete(partialPath);
        DeleteTemporarySidecars(partialPath);
    }

    private static void DeleteTemporarySidecars(string partialPath)
    {
        foreach (var sidecarPath in
                 SqliteSnapshotArtifactPolicy.GetTemporarySidecarPaths(
                     partialPath))
        {
            File.Delete(sidecarPath);
        }
    }

    private static async Task ValidateIntegrityAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 60
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken);

        if (!string.Equals(
                Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture),
                "ok",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The consistent Synapse SQLite snapshot failed integrity_check.");
        }
    }
}
