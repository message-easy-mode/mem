using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly bool _ensureWalOnFirstOpen;
    private readonly object _journalModeGate = new();
    private int _journalModeInitialized;

    public SqliteConnectionFactory(string connectionString)
    {
        // Each returned connection owns its native handle until the caller
        // disposes it. Do not return handles to a process-wide provider pool:
        // a request/health-check scope must not inherit another scope's handle
        // or depend on pooled connection reset succeeding during disposal.
        // Keep the supplied cache mode so explicit shared in-memory fixtures
        // retain their lifetime semantics. The Control Plane registers Private
        // cache for its file-backed WAL authority.
        var settings = new SqliteConnectionStringBuilder(connectionString)
        {
            Pooling = false,
        };
        _connectionString = settings.ToString();

        // WAL is meaningful for file-backed databases and persists in the
        // database header. In-memory SQLite uses its own journal mode and must
        // retain the explicit shared-memory semantics used by focused fixtures.
        _ensureWalOnFirstOpen = settings.Mode != SqliteOpenMode.Memory
            && !string.Equals(settings.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase);
    }

    public SqliteConnection CreateConnection()
    {
        // Establish the persistent database-level journal mode exactly once per
        // factory before concurrent request scopes are allowed to open through
        // this factory. The earlier implementation ran PRAGMA journal_mode=WAL
        // on every connection, so a burst of new scopes could all contend while
        // merely resolving MemDbContext.
        if (_ensureWalOnFirstOpen && Volatile.Read(ref _journalModeInitialized) == 0)
        {
            lock (_journalModeGate)
            {
                if (Volatile.Read(ref _journalModeInitialized) == 0)
                {
                    var first = OpenConfiguredConnection();
                    try
                    {
                        SqlitePragmas.EnsureWriteAheadLogging(first);
                        Volatile.Write(ref _journalModeInitialized, 1);
                        return first;
                    }
                    catch
                    {
                        first.Dispose();
                        throw;
                    }
                }
            }
        }

        return OpenConfiguredConnection();
    }

    private SqliteConnection OpenConfiguredConnection()
    {
        var conn = new SqliteConnection(_connectionString);

        try
        {
            conn.Open();

            // These settings belong to each opened connection. busy_timeout is
            // intentionally applied before any database-level initialization.
            SqlitePragmas.ApplyConnection(conn);

            return conn;
        }
        catch
        {
            // The factory owns the connection until it is successfully handed
            // to its caller. Never leave a partially-opened or pragma-failed
            // native SQLite handle for finalization to clean up later.
            conn.Dispose();
            throw;
        }
    }
}
