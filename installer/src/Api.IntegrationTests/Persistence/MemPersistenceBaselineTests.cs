using Infrastructure.HostedServices;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Api.IntegrationTests.Persistence;

public sealed class MemPersistenceBaselineTests
{
    [Fact]
    public async Task Canonical_MEM_020_baseline_is_one_initial_migration_and_materialises_the_current_model()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);

            var migration = Assert.Single(db.Database.GetMigrations());
            Assert.True(
                migration.EndsWith("_MEM_020_Initial", StringComparison.Ordinal),
                $"Expected the canonical MEM 0.2.0 initial migration, found '{migration}'.");

            await db.Database.MigrateAsync();

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            Assert.Equal(new[] { migration }, applied);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var expectedTables = db.Model.GetEntityTypes()
                .Select(entityType => entityType.GetTableName())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            var actualTables = await ReadUserTablesAsync(databasePath);
            var missing = expectedTables
                .Where(table => !actualTables.Contains(table))
                .OrderBy(table => table, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                missing.Length == 0,
                "The initial migration did not materialise all current model tables. Missing: " +
                string.Join(", ", missing));
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public async Task Startup_initializer_is_idempotent_on_the_canonical_baseline()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await using (var first = CreateServices(databasePath))
            {
                await CreateInitializer(first).StartAsync(CancellationToken.None);
            }

            await using (var second = CreateServices(databasePath))
            {
                await CreateInitializer(second).StartAsync(CancellationToken.None);
            }

            var history = await ReadMigrationHistoryAsync(databasePath);
            var migration = Assert.Single(history);
            Assert.True(
                migration.EndsWith("_MEM_020_Initial", StringComparison.Ordinal),
                $"Expected the canonical MEM 0.2.0 initial migration, found '{migration}'.");
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public async Task Startup_initializer_rejects_superseded_pre_release_migration_history()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await ExecuteSqlAsync(
                databasePath,
                """
                CREATE TABLE "LegacyProbe" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_LegacyProbe" PRIMARY KEY
                );
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260924064210_MIGRATION_STAGING_RETIREMENT_01B', '8.0.4');
                """);

            await using var services = CreateServices(databasePath);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateInitializer(services).StartAsync(CancellationToken.None));

            Assert.Contains("superseded MEM 0.2.0 pre-release persistence baseline", error.Message);
            Assert.Contains("does not upgrade pre-release Control Plane databases in place", error.Message);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public async Task Startup_initializer_rejects_application_tables_without_migration_history()
    {
        var databasePath = TemporaryDatabasePath();
        try
        {
            await ExecuteSqlAsync(
                databasePath,
                """
                CREATE TABLE "LegacyProbe" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_LegacyProbe" PRIMARY KEY
                );
                """);

            await using var services = CreateServices(databasePath);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => CreateInitializer(services).StartAsync(CancellationToken.None));

            Assert.Contains("superseded MEM 0.2.0 pre-release persistence baseline", error.Message);
            Assert.Contains("no EF migration history", error.Message);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    private static ServiceProvider CreateServices(string databasePath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddControlPlaneSqlite(databasePath);
        return services.BuildServiceProvider();
    }

    private static SqlitePragmaInitializer CreateInitializer(IServiceProvider services) =>
        new(
            services.GetRequiredService<SqliteConnectionFactory>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<ILogger<SqlitePragmaInitializer>>());

    private static async Task<HashSet<string>> ReadUserTablesAsync(string databasePath)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
              AND name <> '__EFMigrationsHistory';
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static async Task<string[]> ReadMigrationHistoryAsync(string databasePath)
    {
        var migrations = new List<string>();
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "MigrationId"
            FROM "__EFMigrationsHistory"
            ORDER BY "MigrationId";
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            migrations.Add(reader.GetString(0));
        }

        return migrations.ToArray();
    }

    private static async Task ExecuteSqlAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string TemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"mem-persistence-baseline-{Guid.NewGuid():N}.db");

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort fixture cleanup only.
            }
        }
    }
}
