using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modules.Setup.InstallRuns;

/// <summary>
/// Owns installation execution for the lifetime of one Control Plane API process.
///
/// The Control Plane deliberately runs one mutable installation worker. A durable
/// installation may survive an API/container restart, but two background tasks in
/// the same process must never execute the same installation concurrently.
/// </summary>
public sealed class InstallRunCoordinator : BackgroundService, IInstallRunCoordinator
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private readonly ConcurrentDictionary<Guid, byte> _ownedInstallations = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InstallRunCoordinator> _logger;

    public InstallRunCoordinator(
        IServiceScopeFactory scopeFactory,
        ILogger<InstallRunCoordinator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public InstallRunQueueResult Queue(Guid installationId)
    {
        if (!_ownedInstallations.TryAdd(installationId, 0))
        {
            return new InstallRunQueueResult(
                installationId,
                Queued: false,
                AlreadyOwned: true);
        }

        if (_queue.Writer.TryWrite(installationId))
        {
            _logger.LogInformation(
                "Installation {InstallationId} was queued for server-owned execution",
                installationId);

            return new InstallRunQueueResult(
                installationId,
                Queued: true,
                AlreadyOwned: false);
        }

        _ownedInstallations.TryRemove(installationId, out _);

        throw new InvalidOperationException(
            $"Installation '{installationId}' could not be queued because the installation worker is unavailable.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var installationId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var runner = scope.ServiceProvider.GetRequiredService<IInstallRunner>();

                    await runner.RunAsync(installationId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation(
                        "Installation worker stopped while installation {InstallationId} was active. Durable recovery will run when the Control Plane starts again.",
                        installationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unhandled server-owned installation worker failure for installation {InstallationId}",
                        installationId);

                    await MarkUnexpectedFailureAsync(
                        installationId,
                        ex,
                        CancellationToken.None);
                }
                finally
                {
                    _ownedInstallations.TryRemove(installationId, out _);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown. Any installation that was durably Running is
            // reconstructed by InstallationRecoveryHostedService on the next start.
        }
    }

    private async Task MarkUnexpectedFailureAsync(
        Guid installationId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<InstallationRunRecoveryService>();

            await recovery.MarkUnexpectedRunnerFailureAsync(
                installationId,
                exception,
                cancellationToken);
        }
        catch (Exception recoveryException)
        {
            _logger.LogCritical(
                recoveryException,
                "MEM could not persist the unexpected installation worker failure for installation {InstallationId}",
                installationId);
        }
    }
}
