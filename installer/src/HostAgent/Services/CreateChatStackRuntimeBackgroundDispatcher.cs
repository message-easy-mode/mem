using System.Threading.Channels;
using HostAgent.Commands;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Services;

/// <summary>
/// Executes accepted create-stack commands outside the initiating HTTP request
/// scope. The durable RuntimeOperation row is created before an item is queued,
/// so the browser can poll progress immediately after receiving HTTP 202.
/// </summary>
public sealed class CreateChatStackRuntimeBackgroundDispatcher : BackgroundService
{
    private const int QueueCapacity = 16;
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly Channel<CreateChatStackRuntimeWorkItem> _queue =
        Channel.CreateBounded<CreateChatStackRuntimeWorkItem>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CreateChatStackRuntimeBackgroundDispatcher> _logger;

    public CreateChatStackRuntimeBackgroundDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<CreateChatStackRuntimeBackgroundDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool TryEnqueue(
        CreateChatStackRuntimeCommand command,
        Guid operationId) =>
        _queue.Writer.TryWrite(new CreateChatStackRuntimeWorkItem(command, operationId));

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
            // Normal host shutdown. In-flight work is cancelled by the
            // server-owned operation lifetime; queued work is failed below.
        }
        finally
        {
            while (_queue.Reader.TryRead(out var queued))
            {
                await MarkDispatchFailureAsync(
                    queued,
                    "MEM stopped before the queued stack-creation operation could begin.");
            }
        }
    }

    private async Task ExecuteItemAsync(CreateChatStackRuntimeWorkItem item)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        try
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<ICreateChatStackRuntimeHandler>();

            await handler.HandleAcceptedAsync(item.Command, item.OperationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Accepted create-stack operation terminated. OperationId={OperationId} StackId={StackId} StackSlug={StackSlug}",
                item.OperationId,
                item.Command.StackId,
                item.Command.StackSlug);

            // The handler normally journals its own terminal failure. This
            // fallback protects failures that occur before handler resolution
            // or outside its normal failure-finalization boundary.
            try
            {
                var operations = scope.ServiceProvider
                    .GetRequiredService<RuntimeOperationStore>();
                var detail = await operations.FindByIdAsync(
                    item.OperationId,
                    CancellationToken.None);

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
                            failureKind = "create-stack-background-dispatch-failed",
                            exceptionType = ex.GetType().FullName
                        },
                        journalTimeout.Token);
                }
            }
            catch (Exception journalEx)
            {
                _logger.LogError(
                    journalEx,
                    "Could not persist fallback failure for create-stack operation {OperationId}.",
                    item.OperationId);
            }
        }
    }

    private async Task MarkDispatchFailureAsync(
        CreateChatStackRuntimeWorkItem item,
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
                    failureKind = "create-stack-queued-work-abandoned"
                },
                journalTimeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Could not mark queued create-stack operation {OperationId} as failed during shutdown.",
                item.OperationId);
        }
    }

    private sealed record CreateChatStackRuntimeWorkItem(
        CreateChatStackRuntimeCommand Command,
        Guid OperationId);
}
