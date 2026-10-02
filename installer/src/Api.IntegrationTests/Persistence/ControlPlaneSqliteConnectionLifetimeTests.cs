using System.Collections.Concurrent;
using System.Data;
using System.Runtime.InteropServices;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Health;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.IntegrationTests.Persistence;

public sealed class ControlPlaneSqliteConnectionLifetimeTests
{
    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_registration_preserves_durability_without_shared_cache_or_pooling()
    {
        await using var store = new TestStore();
        await using var scope = store.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var connection = GetConnection(db);
        var settings = new SqliteConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(store.DatabasePath, settings.DataSource);
        Assert.Equal(SqliteOpenMode.ReadWriteCreate, settings.Mode);
        Assert.Equal(SqliteCacheMode.Private, settings.Cache);
        Assert.False(settings.Pooling);
        Assert.Equal(ConnectionState.Open, connection.State);
        Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
        Assert.Equal(2L, await ScalarAsync(connection, "PRAGMA synchronous;"));
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(5000L, await ScalarAsync(connection, "PRAGMA busy_timeout;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_scope_disposal_closes_native_handle_not_just_managed_wrapper(bool asyncDisposal)
    {
        await using var store = new TestStore();
        for (var iteration = 0; iteration < 16; iteration++)
        {
            await using var scope = store.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            Assert.Same(db, scope.ServiceProvider.GetRequiredService<MemDbContext>());
            var connection = GetConnection(db);
            var handle = GetHandle(connection);
            await db.Database.ExecuteSqlRawAsync("SELECT 1;");

            if (asyncDisposal)
            {
                await scope.DisposeAsync();
            }
            else
            {
                scope.Dispose();
            }

            // Closed wrappers alone did not prove that the old native handle
            // could not be reissued to another request by the provider pool.
            Assert.Equal(ConnectionState.Closed, connection.State);
            Assert.True(handle.IsClosed);
        }
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_disposing_an_overlapping_scope_does_not_invalidate_a_live_reader()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        await using var survivorScope = store.Services.CreateAsyncScope();
        var survivor = survivorScope.ServiceProvider.GetRequiredService<MemDbContext>();
        var survivorConnection = GetConnection(survivor);
        var survivorHandle = GetHandle(survivorConnection);

        for (var iteration = 0; iteration < 24; iteration++)
        {
            SafeHandle releasedHandle;
            await using (var scope = store.Services.CreateAsyncScope())
            {
                var other = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                var otherConnection = GetConnection(other);
                releasedHandle = GetHandle(otherConnection);
                Assert.NotSame(survivorConnection, otherConnection);
                Assert.NotSame(survivorHandle, releasedHandle);
                Assert.Same(
                    store.Factory,
                    scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>());
                Assert.Equal(1, await other.MigrationIntakes.CountAsync());
            }

            Assert.True(releasedHandle.IsClosed);
            Assert.False(survivorHandle.IsClosed);
            Assert.Equal("seed", await survivor.MigrationIntakes.AsNoTracking()
                .Select(item => item.DisplayName).SingleAsync());
        }
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_wal_reader_sees_committed_data_while_another_scope_holds_a_write()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        await using var writerScope = store.Services.CreateAsyncScope();
        await using var readerScope = store.Services.CreateAsyncScope();
        var writer = writerScope.ServiceProvider.GetRequiredService<MemDbContext>();
        var reader = readerScope.ServiceProvider.GetRequiredService<MemDbContext>();
        // Resolve both independent connections before acquiring the write lock.
        // This deadline is test-only: the normal PRAGMA/timeout policy is unchanged.
        var readerHandle = GetHandle(GetConnection(reader));
        reader.Database.SetCommandTimeout(1);

        await using var transaction = await writer.Database.BeginTransactionAsync();
        var intake = await writer.MigrationIntakes.SingleAsync();
        intake.DisplayName = "not-yet-committed";
        await writer.SaveChangesAsync();
        try
        {
            // A WAL/private-cache reader must not take the shared-cache table
            // lock held by the writer or observe its uncommitted update.
            Assert.Equal("seed", await reader.MigrationIntakes.AsNoTracking()
                .Select(item => item.DisplayName).SingleAsync());
            // Exercise the disposal boundary seen in the live log while the
            // other scope still holds its transaction; do not release the writer
            // first and accidentally reduce this to a sequential-close test.
            await readerScope.DisposeAsync();
            Assert.True(readerHandle.IsClosed);
            Assert.False(GetHandle(GetConnection(writer)).IsClosed);
            using var probe = store.Factory.CreateConnection();
            Assert.Equal(1L, await ScalarAsync(probe, "SELECT 1;"));
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        await using var finalScope = store.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<MemDbContext>();
        Assert.Equal("seed", await finalDb.MigrationIntakes.AsNoTracking()
            .Select(item => item.DisplayName).SingleAsync());
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01A_concurrent_first_opens_establish_wal_without_a_barrier_deadlock()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "mem-sqlite-first-open-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var databasePath = Path.Combine(root, "control-plane.db");
            var factory = new SqliteConnectionFactory(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Cache = SqliteCacheMode.Private,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());

            const int workers = 8;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
            {
                await start.Task.WaitAsync(timeout.Token);
                var connection = factory.CreateConnection();
                Assert.Equal("wal", await ScalarAsync(connection, "PRAGMA journal_mode;"));
                Assert.Equal(5000L, await ScalarAsync(connection, "PRAGMA busy_timeout;"));
                return connection;
            }, timeout.Token)).ToArray();

            start.TrySetResult(true);
            var connections = await Task.WhenAll(tasks).WaitAsync(timeout.Token);
            try
            {
                Assert.Equal(workers, connections.Length);
                Assert.Equal(
                    workers,
                    connections.Select(GetHandle).Distinct(ReferenceEqualityComparer.Instance).Count());
            }
            finally
            {
                foreach (var connection in connections)
                {
                    connection.Dispose();
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_concurrent_request_scopes_read_write_and_dispose_independently()
    {
        await using var store = new TestStore();
        await store.SeedAsync();
        const int workers = 6;
        const int writers = 2;
        const int iterations = 16;
        var handles = new ConcurrentBag<SafeHandle>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstScopesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var tasks = Enumerable.Range(0, workers).Select(worker => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < iterations; iteration++)
            {
                timeout.Token.ThrowIfCancellationRequested();
                SafeHandle handle;
                await using (var scope = store.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                    handle = GetHandle(GetConnection(db));
                    handles.Add(handle);
                    if (iteration == 0)
                    {
                        if (Interlocked.Increment(ref readyCount) == workers)
                        {
                            firstScopesReady.TrySetResult(true);
                        }

                        await start.Task.WaitAsync(timeout.Token);
                    }

                    if (worker < writers)
                    {
                        var intake = NewIntake($"writer-{worker}-{iteration}");
                        db.MigrationIntakes.Add(intake);
                        await db.SaveChangesAsync(timeout.Token);
                        Assert.Equal(1L, intake.StateVersion);
                        Assert.NotEqual(default(DateTime), intake.UpdatedAtUtc);
                    }
                    else
                    {
                        Assert.True(await db.MigrationIntakes.AsNoTracking().AnyAsync(timeout.Token));
                    }
                }

                Assert.True(handle.IsClosed);
                await Task.Yield();
            }
        })).ToArray();

        try
        {
            // Guarantee that scopes actually overlap; no sleeps or reliance on
            // task scheduling to manufacture concurrency. Cancellation releases
            // the wait even if opening a scope fails.
            await firstScopesReady.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            start.TrySetResult(true);
            // Join all workers before disposing the provider or test database.
            await Task.WhenAll(tasks);
        }

        Assert.Equal(workers * iterations, handles.Count);
        Assert.Equal(handles.Count, new HashSet<SafeHandle>(handles, ReferenceEqualityComparer.Instance).Count);
        Assert.All(handles, handle => Assert.True(handle.IsClosed));
        await using var finalScope = store.Services.CreateAsyncScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<MemDbContext>();
        Assert.Equal(1 + writers * iterations, await finalDb.MigrationIntakes.CountAsync());
        Assert.Equal("ok", await ScalarAsync(GetConnection(finalDb), "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_factory_does_not_honor_an_opt_in_to_native_pooling()
    {
        await using var store = new TestStore();
        var factory = new SqliteConnectionFactory(new SqliteConnectionStringBuilder
        {
            DataSource = store.DatabasePath,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString());

        using var connection = factory.CreateConnection();
        var handle = GetHandle(connection);
        Assert.False(new SqliteConnectionStringBuilder(connection.ConnectionString).Pooling);
        connection.Close();
        Assert.True(handle.IsClosed);
        using var replacement = factory.CreateConnection();
        Assert.NotSame(handle, GetHandle(replacement));
        Assert.Equal(1L, await ScalarAsync(replacement, "SELECT 1;"));
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_factory_preserves_explicit_shared_memory_semantics()
    {
        var factory = new SqliteConnectionFactory(new SqliteConnectionStringBuilder
        {
            DataSource = $"mem-lifetime-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        using var owner = factory.CreateConnection();
        await using (var command = owner.CreateCommand())
        {
            command.CommandText = "CREATE TABLE SharedProbe (Id INTEGER PRIMARY KEY); INSERT INTO SharedProbe VALUES (1);";
            await command.ExecuteNonQueryAsync();
        }
        using var reader = factory.CreateConnection();
        Assert.Equal(1L, await ScalarAsync(reader, "SELECT COUNT(*) FROM SharedProbe;"));
        Assert.False(new SqliteConnectionStringBuilder(reader.ConnectionString).Pooling);
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_failed_initialization_does_not_poison_subsequent_connections()
    {
        await using var store = new TestStore();
        var invalidBytes = System.Text.Encoding.UTF8.GetBytes("deliberately-not-a-sqlite-database" + new string('x', 1024));
        await File.WriteAllBytesAsync(store.DatabasePath, invalidBytes);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var error = Assert.Throws<SqliteException>(() => store.Factory.CreateConnection());
            Assert.Equal(26, error.SqliteErrorCode); // SQLITE_NOTADB: preserve the real failure.
        }

        Assert.Equal(invalidBytes, await File.ReadAllBytesAsync(store.DatabasePath));
        // This is a deliberately invalid disposable test file, not a recovery
        // procedure for a live Control Plane database.
        File.Delete(store.DatabasePath);
        using var recovered = store.Factory.CreateConnection();
        Assert.Equal(1L, await ScalarAsync(recovered, "SELECT 1;"));
        Assert.Equal("wal", await ScalarAsync(recovered, "PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task CONTROL_PLANE_SQLITE_CONNECTION_LIFETIME_CORR_01_health_probe_disposal_preserves_live_request_and_foreign_key_enforcement()
    {
        await using var store = new TestStore();
        await using var scope = store.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var connection = GetConnection(db);
        var handle = GetHandle(connection);
        var health = new SqliteHealthCheck(store.Factory);
        Assert.Equal(HealthStatus.Healthy, (await health.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.False(handle.IsClosed);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE LifetimeParent (Id INTEGER PRIMARY KEY);
            CREATE TABLE LifetimeChild (ParentId INTEGER NOT NULL REFERENCES LifetimeParent(Id));
            """);
        var error = await Assert.ThrowsAsync<SqliteException>(() =>
            db.Database.ExecuteSqlRawAsync("INSERT INTO LifetimeChild (ParentId) VALUES (1);"));
        Assert.Equal(19, error.SqliteErrorCode); // SQLITE_CONSTRAINT is not hidden/retried.
        Assert.Equal(1L, await ScalarAsync(connection, "PRAGMA foreign_keys;"));
        Assert.Equal(1L, await ScalarAsync(connection, "SELECT 1;"));
    }

    private static SqliteConnection GetConnection(MemDbContext db) =>
        Assert.IsType<SqliteConnection>(db.Database.GetDbConnection());

    private static SafeHandle GetHandle(SqliteConnection connection)
    {
        Assert.NotNull(connection.Handle);
        return connection.Handle!;
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static MigrationIntakeEntity NewIntake(string name) => new()
    {
        Id = Guid.NewGuid(),
        IntakeId = $"mig-sqlite-{name}",
        DisplayName = name,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private sealed class TestStore : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(), "mem-sqlite-concurrency-tests", Guid.NewGuid().ToString("N"));

        public TestStore()
        {
            Directory.CreateDirectory(_root);
            DatabasePath = Path.Combine(_root, "control-plane.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            // Use the production registration, not a copied look-alike fixture.
            services.AddControlPlaneSqlite(DatabasePath);
            Services = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
        }

        public string DatabasePath { get; }
        public ServiceProvider Services { get; }
        public SqliteConnectionFactory Factory => Services.GetRequiredService<SqliteConnectionFactory>();

        public async Task SeedAsync()
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.MigrationIntakes.Add(NewIntake("seed"));
            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            Directory.Delete(_root, recursive: true);
        }
    }
}
