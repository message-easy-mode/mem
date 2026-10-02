using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Infrastructure.HostedServices;

/// <summary>
/// Applies SQLite connection invariants and advances the canonical MEM schema.
/// MEM 0.2.0 establishes a new EF migration baseline. Pre-release Control Plane
/// databases from the earlier multi-migration history are deliberately not
/// upgraded in place; they must be reset/recreated before this baseline is used.
/// </summary>
public sealed class SqlitePragmaInitializer : IHostedService
{
    private const string MigrationHistoryTable = "__EFMigrationsHistory";
    private const string CanonicalBaselineMigrationName = "MEM_020_Initial";

    private readonly SqliteConnectionFactory _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SqlitePragmaInitializer> _log;

    public SqlitePragmaInitializer(
        SqliteConnectionFactory factory,
        IServiceScopeFactory scopeFactory,
        ILogger<SqlitePragmaInitializer> log)
    {
        _factory = factory;
        _scopeFactory = scopeFactory;
        _log = log;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var connection = _factory.CreateConnection())
        {
            _log.LogInformation("SQLite connection opened and PRAGMAs applied.");

            var schemaState = await InspectSchemaStateAsync(connection, cancellationToken);
            await EnsureCompatibleMigrationBaselineAsync(connection, schemaState, cancellationToken);
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        await db.Database.MigrateAsync(cancellationToken);
        await EnsureCanonicalBaselineAppliedAsync(db, cancellationToken);
        await EnsureCurrentModelTablesPresentAsync(db, cancellationToken);

        _log.LogInformation("SQLite schema migrated successfully from the canonical MEM 0.2.0 baseline.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureCompatibleMigrationBaselineAsync(
        SqliteConnection connection,
        SqliteSchemaState schemaState,
        CancellationToken ct)
    {
        if (schemaState.HasUserTables && !schemaState.HasMigrationHistory)
        {
            throw SupersededPreReleaseSchema(
                "application tables exist but no EF migration history is present");
        }

        if (!schemaState.HasMigrationHistory)
        {
            return;
        }

        var applied = await ReadAppliedMigrationIdsAsync(connection, ct);
        if (applied.Count == 0)
        {
            if (schemaState.HasUserTables)
            {
                throw SupersededPreReleaseSchema(
                    "application tables exist but the EF migration history is empty");
            }

            return;
        }

        if (!IsCanonicalBaselineMigrationId(applied[0]))
        {
            throw SupersededPreReleaseSchema(
                $"the first applied migration is '{applied[0]}' rather than the canonical MEM 0.2.0 baseline");
        }
    }

    private static InvalidOperationException SupersededPreReleaseSchema(string reason) =>
        new(
            "SQLite database uses a superseded MEM 0.2.0 pre-release persistence baseline: " + reason + ". " +
            "MEM 0.2.0 does not upgrade pre-release Control Plane databases in place after the canonical baseline reset. " +
            "Back up any evidence you need, then reset/recreate the Control Plane database before starting MEM.");

    private async Task EnsureCanonicalBaselineAppliedAsync(
        MemDbContext db,
        CancellationToken ct)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (applied.Length == 0 || !IsCanonicalBaselineMigrationId(applied[0]))
        {
            throw new InvalidOperationException(
                "SQLite migration completed without the canonical MEM 0.2.0 initial migration as the first applied migration. " +
                "Refusing to continue with an ambiguous persistence baseline.");
        }
    }

    private async Task EnsureCurrentModelTablesPresentAsync(
        MemDbContext db,
        CancellationToken ct)
    {
        var expectedTables = db.Model.GetEntityTypes()
            .Select(entityType => entityType.GetTableName())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        using var connection = _factory.CreateConnection();
        var schemaState = await InspectSchemaStateAsync(connection, ct);

        var missing = expectedTables
            .Where(table => !schemaState.UserTables.Contains(table))
            .OrderBy(table => table, StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                "SQLite migration history is present but the current MEM model is not fully materialised. " +
                "Refusing to continue with an incomplete schema. Missing tables: " +
                string.Join(", ", missing) + ".");
        }
    }

    private async Task<SqliteSchemaState> InspectSchemaStateAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        var tables = new HashSet<string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%';
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            tables.Add(reader.GetString(0));
        }

        return new SqliteSchemaState(
            HasMigrationHistory: tables.Contains(MigrationHistoryTable),
            UserTables: tables
                .Where(name => !string.Equals(name, MigrationHistoryTable, StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedMigrationIdsAsync(
        SqliteConnection connection,
        CancellationToken ct)
    {
        var migrationIds = new List<string>();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "MigrationId"
            FROM "__EFMigrationsHistory"
            ORDER BY "MigrationId";
            """;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            migrationIds.Add(reader.GetString(0));
        }

        return migrationIds;
    }

    private static bool IsCanonicalBaselineMigrationId(string migrationId) =>
        migrationId.EndsWith($"_{CanonicalBaselineMigrationName}", StringComparison.Ordinal);

    private sealed record SqliteSchemaState(
        bool HasMigrationHistory,
        IReadOnlySet<string> UserTables)
    {
        public bool HasUserTables => UserTables.Count > 0;
    }
}
