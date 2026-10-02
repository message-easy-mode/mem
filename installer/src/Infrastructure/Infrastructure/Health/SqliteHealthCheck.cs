using Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Infrastructure.Health;

public sealed class SqliteHealthCheck : IHealthCheck
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteHealthCheck(SqliteConnectionFactory factory) => _factory = factory;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var conn = _factory.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            var result = cmd.ExecuteScalar();

            return Task.FromResult(
                (result is long l && l == 1) || (result is int i && i == 1)
                    ? HealthCheckResult.Healthy("SQLite OK")
                    : HealthCheckResult.Degraded("SQLite returned unexpected result"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("SQLite unhealthy", ex));
        }
    }
}