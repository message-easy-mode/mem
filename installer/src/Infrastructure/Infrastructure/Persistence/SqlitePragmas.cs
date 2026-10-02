using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public static class SqlitePragmas
{
    /// <summary>
    /// Applies invariants that belong to each individual SQLite connection.
    /// Keep busy_timeout first so any later pragma that needs the database lock
    /// has the same bounded wait policy as normal commands.
    /// </summary>
    public static void ApplyConnection(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        PRAGMA busy_timeout=5000;
        PRAGMA synchronous=FULL;
        PRAGMA foreign_keys=ON;
        """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// WAL is a persistent database-level setting. It must not be negotiated by
    /// every request connection: concurrent PRAGMA journal_mode transitions can
    /// contend before a scoped DbContext has even reached its real query.
    /// </summary>
    public static void EnsureWriteAheadLogging(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        var mode = Convert.ToString(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SQLite refused WAL journal mode for the Control Plane database (observed '{mode ?? "<null>"}').");
        }
    }

    /// <summary>
    /// Compatibility helper for callers that deliberately want to establish all
    /// invariants on one known database connection.
    /// </summary>
    public static void Apply(SqliteConnection conn)
    {
        ApplyConnection(conn);
        EnsureWriteAheadLogging(conn);
    }
}
