using Microsoft.Extensions.Hosting;

namespace Api.Diagnostics;

public sealed class ControlPlaneStartupDiagnosticService(
    IHostApplicationLifetime applicationLifetime,
    ControlPlaneStartupDiagnosticPublisher publisher) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!applicationLifetime.ApplicationStarted.IsCancellationRequested)
        {
            var started = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = applicationLifetime.ApplicationStarted.Register(
                () => started.TrySetResult(true));

            try
            {
                await started.Task.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        await publisher.PublishAsync(stoppingToken);
    }
}
