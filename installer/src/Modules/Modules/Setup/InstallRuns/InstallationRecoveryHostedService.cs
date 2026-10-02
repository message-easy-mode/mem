using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modules.Setup.InstallRuns;

/// <summary>
/// Reconstructs durable Running installations after the Control Plane database
/// has been initialised, then hands execution back to the server-owned worker.
/// </summary>
public sealed class InstallationRecoveryHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IInstallRunCoordinator _coordinator;
    private readonly ILogger<InstallationRecoveryHostedService> _logger;

    public InstallationRecoveryHostedService(
        IServiceScopeFactory scopeFactory,
        IInstallRunCoordinator coordinator,
        ILogger<InstallationRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _coordinator = coordinator;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<InstallationRunRecoveryService>();
            var installationIds = await recovery.RecoverInterruptedRunsAsync(cancellationToken);

            foreach (var installationId in installationIds)
            {
                var queueResult = _coordinator.Queue(installationId);

                _logger.LogInformation(
                    "Startup recovery for installation {InstallationId}: Queued={Queued} AlreadyOwned={AlreadyOwned}",
                    installationId,
                    queueResult.Queued,
                    queueResult.AlreadyOwned);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Recovery must never make the private Control Plane unavailable. A
            // failed recovery remains durable and observable so the operator can
            // inspect Diagnostics instead of losing the only troubleshooting UI.
            _logger.LogError(
                ex,
                "MEM could not reconstruct interrupted installation work during Control Plane startup. The Control Plane will remain available for diagnostics.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
