using System.Threading.Channels;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Coturn;

/// <summary>
/// Executes accepted Restart & Verify / Repair work outside the initiating HTTP
/// request scope. RuntimeOperation persistence exists before enqueue, so browser
/// navigation or transport loss cannot cancel an already accepted mutation.
/// </summary>
public sealed class CoturnPlatformMaintenanceBackgroundDispatcher : BackgroundService,
    ICoturnPlatformMaintenanceDispatcher
{
    private const int QueueCapacity = 4;
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<WorkItem> _queue = Channel.CreateBounded<WorkItem>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CoturnPlatformMaintenanceBackgroundDispatcher> _logger;

    public CoturnPlatformMaintenanceBackgroundDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<CoturnPlatformMaintenanceBackgroundDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool TryEnqueue(
        Guid operationId,
        CoturnPlatformMaintenanceRequest request) =>
        _queue.Writer.TryWrite(new WorkItem(operationId, request));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ExecuteItemAsync(item);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during normal Control Plane shutdown.
        }
        finally
        {
            while (_queue.Reader.TryRead(out var queued))
            {
                await FailQueuedAsync(
                    queued,
                    "MEM stopped before the queued platform TURN maintenance operation could begin.");
            }
        }
    }

    private async Task ExecuteItemAsync(WorkItem item)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        try
        {
            var processor = scope.ServiceProvider
                .GetRequiredService<CoturnPlatformMaintenanceOperationProcessor>();
            await processor.ExecuteAsync(item.OperationId, item.Request);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Accepted platform TURN maintenance operation terminated unexpectedly. OperationId={OperationId} Action={Action}",
                item.OperationId,
                item.Request.Action);

            try
            {
                var operations = scope.ServiceProvider
                    .GetRequiredService<RuntimeOperationStore>();
                using var lookupTimeout = new CancellationTokenSource(FailureJournalTimeout);
                var detail = await operations.FindByIdAsync(
                    item.OperationId,
                    lookupTimeout.Token);

                if (detail?.CompletedAtUtc is null)
                {
                    using var journalTimeout = new CancellationTokenSource(FailureJournalTimeout);
                    await operations.FailAsync(
                        item.OperationId,
                        currentStep: detail?.CurrentStep ?? "background-dispatch",
                        error: "The accepted platform TURN maintenance operation terminated unexpectedly. Review Diagnostics for bounded evidence.",
                        evidence: new
                        {
                            action = item.Request.Action,
                            failureKind = "coturn-maintenance-background-dispatch-failed",
                            exceptionType = ex.GetType().FullName
                        },
                        journalTimeout.Token);
                }
            }
            catch (Exception journalEx)
            {
                _logger.LogError(
                    journalEx,
                    "Could not persist fallback failure for platform TURN maintenance operation {OperationId}.",
                    item.OperationId);
            }
        }
    }

    private async Task FailQueuedAsync(WorkItem item, string message)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var operations = scope.ServiceProvider.GetRequiredService<RuntimeOperationStore>();
            using var journalTimeout = new CancellationTokenSource(FailureJournalTimeout);
            await operations.FailAsync(
                item.OperationId,
                currentStep: "queued",
                error: message,
                evidence: new
                {
                    action = item.Request.Action,
                    failureKind = "coturn-maintenance-queued-work-abandoned"
                },
                journalTimeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not mark queued platform TURN maintenance operation {OperationId} as failed during shutdown.",
                item.OperationId);
        }
    }

    private sealed record WorkItem(
        Guid OperationId,
        CoturnPlatformMaintenanceRequest Request);
}
