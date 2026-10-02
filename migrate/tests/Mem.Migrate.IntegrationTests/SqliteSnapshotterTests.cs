using Microsoft.Data.Sqlite;
using Mem.Migrate.Infrastructure.Sqlite;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteSnapshotterTests
{
    [Fact]
    public async Task Creates_a_consistent_read_only_snapshot_from_a_wal_database()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-sqlite-snapshot-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcePath = Path.Combine(root, "homeserver.db");
            var destinationPath = Path.Combine(root, "snapshot", "homeserver.db");
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllTextAsync(
                destinationPath + ".partial-wal",
                "stale-wal");
            await File.WriteAllTextAsync(
                destinationPath + ".partial-shm",
                "stale-shm");
            await File.WriteAllTextAsync(
                destinationPath + ".partial-journal",
                "stale-journal");
            await using var source = new SqliteConnection(
                $"Data Source={sourcePath};Pooling=False");
            await source.OpenAsync();
            await using (var setup = source.CreateCommand())
            {
                setup.CommandText =
                    """
                    PRAGMA journal_mode = WAL;
                    CREATE TABLE events (id INTEGER PRIMARY KEY, body TEXT NOT NULL);
                    INSERT INTO events(body) VALUES ('one'), ('two'), ('three');
                    """;
                await setup.ExecuteNonQueryAsync();
            }

            await new SqliteSnapshotter().CreateConsistentSnapshotAsync(
                sourcePath,
                destinationPath,
                CancellationToken.None);

            await using var snapshot = new SqliteConnection(
                $"Data Source={destinationPath};Mode=ReadOnly;Pooling=False");
            await snapshot.OpenAsync();
            await using var count = snapshot.CreateCommand();
            count.CommandText = "SELECT count(*) FROM events;";
            Assert.Equal(3L, (long)(await count.ExecuteScalarAsync())!);
            Assert.False(File.Exists(destinationPath + ".partial"));
            Assert.False(File.Exists(destinationPath + ".partial-wal"));
            Assert.False(File.Exists(destinationPath + ".partial-shm"));
            Assert.False(File.Exists(destinationPath + ".partial-journal"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
