using System.Threading.Channels;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Stacks.Destroy;

/// <summary>
/// Executes accepted stack-destroy requests outside the initiating HTTP request
/// scope. The durable RuntimeOperation is created before enqueue, allowing the
/// browser to receive HTTP 202 and poll progress while teardown continues.
/// </summary>
public sealed class RuntimeStackDestroyBackgroundDispatcher : BackgroundService
{
    private const int QueueCapacity = 16;
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<RuntimeStackDestroyWorkItem> _queue =
        Channel.CreateBounded<RuntimeStackDestroyWorkItem>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RuntimeStackDestroyBackgroundDispatcher> _logger;

    public RuntimeStackDestroyBackgroundDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<RuntimeStackDestroyBackgroundDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    internal bool TryEnqueue(
        RuntimeStackDestroyAcceptance acceptance,
        Guid operationId) =>
        _queue.Writer.TryWrite(new RuntimeStackDestroyWorkItem(
            acceptance,
            operationId));

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
            // Expected during normal host shutdown.
        }
        finally
        {
            while (_queue.Reader.TryRead(out var queued))
            {
                await MarkDispatchFailureAsync(
                    queued,
                    "MEM stopped before the queued stack-destroy operation could begin.");
            }
        }
    }

    private async Task ExecuteItemAsync(RuntimeStackDestroyWorkItem item)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        try
        {
            var service = scope.ServiceProvider
                .GetRequiredService<RuntimeStackDestroyService>();

            await service.DestroyAcceptedAsync(
                item.Acceptance,
                item.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Accepted destroy-stack operation terminated. OperationId={OperationId} RuntimeStackId={RuntimeStackId} StackSlug={StackSlug}",
                item.OperationId,
                item.Acceptance.RuntimeStackId,
                item.Acceptance.Slug);

            try
            {
                var operations = scope.ServiceProvider
                    .GetRequiredService<RuntimeOperationStore>();
                using var lookupTimeout =
                    new CancellationTokenSource(FailureJournalTimeout);
                var detail = await operations.FindByIdAsync(
                    item.OperationId,
                    lookupTimeout.Token);

                if (detail?.CompletedAtUtc is null)
                {
                    using var journalTimeout =
                        new CancellationTokenSource(FailureJournalTimeout);
                    await operations.FailAsync(
                        item.OperationId,
                        currentStep: detail?.CurrentStep ?? "background-dispatch",
                        error: ex.Message,
                        evidence: new
                        {
                            failureKind = "destroy-stack-background-dispatch-failed",
                            exceptionType = ex.GetType().FullName,
                            item.Acceptance.RuntimeStackId,
                            item.Acceptance.Slug
                        },
                        ct: journalTimeout.Token);
                }
            }
            catch (Exception journalEx)
            {
                _logger.LogError(
                    journalEx,
                    "Could not persist fallback failure for destroy-stack operation {OperationId}.",
                    item.OperationId);
            }
        }
    }

    private async Task MarkDispatchFailureAsync(
        RuntimeStackDestroyWorkItem item,
        string message)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var operations = scope.ServiceProvider
                .GetRequiredService<RuntimeOperationStore>();
            using var journalTimeout =
                new CancellationTokenSource(FailureJournalTimeout);
            await operations.FailAsync(
                item.OperationId,
                currentStep: "queued",
                error: message,
                evidence: new
                {
                    failureKind = "destroy-stack-queued-work-abandoned",
                    item.Acceptance.RuntimeStackId,
                    item.Acceptance.Slug
                },
                ct: journalTimeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not mark queued destroy-stack operation {OperationId} as failed during shutdown.",
                item.OperationId);
        }
    }

    private sealed record RuntimeStackDestroyWorkItem(
        RuntimeStackDestroyAcceptance Acceptance,
        Guid OperationId);
}
