using System.Data;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Persistence;

public sealed class SqliteConnectionLifetimePersistenceTests
{
    [Fact]
    public async Task Ef_owned_factory_connections_close_deterministically_and_keep_pragmas()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-sqlite-lifetime-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var databasePath = Path.Combine(root, "control-plane.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Cache = SqliteCacheMode.Shared,
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            var factory = new SqliteConnectionFactory(connectionString);

            for (var attempt = 0; attempt < 32; attempt++)
            {
                var connection = factory.CreateConnection();
                Assert.Equal(ConnectionState.Open, connection.State);

                var options = new DbContextOptionsBuilder<MemDbContext>()
                    .UseSqlite(connection, contextOwnsConnection: true)
                    .Options;

                await using (var db = new MemDbContext(options))
                {
                    await db.Database.ExecuteSqlRawAsync("SELECT 1;");

                    Assert.Equal("wal", await ReadTextPragmaAsync(connection, "journal_mode"));
                    Assert.Equal(2L, await ReadIntegerPragmaAsync(connection, "synchronous"));
                    Assert.Equal(1L, await ReadIntegerPragmaAsync(connection, "foreign_keys"));
                    Assert.Equal(5000L, await ReadIntegerPragmaAsync(connection, "busy_timeout"));
                }

                // Passing a pre-opened DbConnection to UseSqlite without
                // context ownership leaves it open after DbContext disposal.
                // MEM creates one of these during scoped DbContext resolution,
                // so deterministic ownership is required to prevent native
                // handles accumulating until GC/finalization.
                Assert.Equal(ConnectionState.Closed, connection.State);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<string> ReadTextPragmaAsync(
        SqliteConnection connection,
        string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private static async Task<long> ReadIntegerPragmaAsync(
        SqliteConnection connection,
        string pragma)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Assert.IsType<long>(await command.ExecuteScalarAsync());
    }
}
