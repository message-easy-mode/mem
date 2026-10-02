using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Migrations.Staging.Retirement;

/// <summary>
/// One dispatcher under MEM's existing exclusive mutable-Control-Plane contract.
/// The durable table, not this process or an HTTP request, is the queue. At startup
/// it replays accepted running work; ordinary failures require a fresh reviewed retry.
/// </summary>
public sealed class MigrationStagingRetirementWorker(IServiceScopeFactory scopes, ILogger<MigrationStagingRetirementWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recoverRunning = true;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Guid[] pending;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                    pending = await db.MigrationStagingRetirements.AsNoTracking()
                        .Where(x => x.Status == "queued" || (recoverRunning && x.Status == "running"))
                        .OrderBy(x => x.RequestedAtUtc).Select(x => x.Id).ToArrayAsync(stoppingToken);
                }
                foreach (var id in pending)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<MigrationStagingRetirementService>().ExecuteAsync(id, stoppingToken);
                }
                recoverRunning = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // Do not leak raw database/host details or silently discard durable work.
                logger.LogWarning("Migration staging retirement dispatcher could not finish its durable scan. It will retry.");
                recoverRunning = true;
            }
            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
