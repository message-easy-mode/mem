using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.StandardRecreate.Reconciliation;

/// <summary>
/// Startup-only reconciliation boundary. Any Standard Recreate still persisted
/// as running before this process starts belonged to a previous API process and
/// therefore cannot still have an in-process executor.
/// </summary>
public sealed class AbandonedStandardRecreateStartupReconciliationService
    : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AbandonedStandardRecreateStartupReconciliationService> _logger;

    public AbandonedStandardRecreateStartupReconciliationService(
        IServiceScopeFactory scopeFactory,
        ILogger<AbandonedStandardRecreateStartupReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var reconciliation = scope.ServiceProvider
            .GetRequiredService<AbandonedStandardRecreateReconciliationService>();

        var result = await reconciliation.ReconcileAfterControlPlaneStartAsync(
            cancellationToken);

        if (result.ReconciledCount > 0)
        {
            _logger.LogWarning(
                "Reconciled {Count} abandoned Standard Recreate operation(s) after Control Plane startup.",
                result.ReconciledCount);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
