using Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Persistence;

/// <summary>
/// The production registration shared by the API and file-backed lifetime tests.
/// The factory is singleton; DbContext, interceptor and live connections are not.
/// </summary>
public static class ControlPlaneSqliteServiceCollectionExtensions
{
    public static IServiceCollection AddControlPlaneSqlite(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            // WAL provides reader/writer concurrency without shared-cache
            // table locks. Do not combine this authority with Cache=Shared.
            Cache = SqliteCacheMode.Private,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        services.AddSingleton(new SqliteConnectionFactory(connectionString));
        services.AddScoped<MigrationSessionLifecycleInterceptor>();
        services.AddDbContext<MemDbContext>((sp, options) =>
        {
            // Resolve dependencies before opening a native handle. The options
            // callback retains ownership until configuration has completed.
            var interceptor = sp.GetRequiredService<MigrationSessionLifecycleInterceptor>();
            var connection = sp.GetRequiredService<SqliteConnectionFactory>().CreateConnection();
            try
            {
                options.UseSqlite(connection, contextOwnsConnection: true);
                options.AddInterceptors(interceptor);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        });

        return services;
    }
}
