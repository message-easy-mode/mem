using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modules.Shared.Domains.Issuance;

public sealed class DomainCertificateIssueWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    DomainCertificateIssueWakeSignal wakeSignal,
    ILogger<DomainCertificateIssueWorker> logger) : BackgroundService
{
    internal static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunScanSafelyAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var interval = Task.Delay(ScanInterval, timeProvider, waitCancellation.Token);
                var signal = wakeSignal.WaitAsync(waitCancellation.Token).AsTask();

                await Task.WhenAny(interval, signal);
                await waitCancellation.CancelAsync();
                wakeSignal.Drain();

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await RunScanSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    internal async Task RunScanSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider
                .GetRequiredService<DomainCertificateIssueOrchestrator>();
            await orchestrator.ProcessPendingAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Durable certificate issuance worker scan failed. FailureType={FailureType}",
                exception.GetType().Name);
        }
    }
}
