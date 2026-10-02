using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Services;

/// <summary>
/// Owns the bounded server-side lifetime for one create-chat-stack mutation.
///
/// The initiating HTTP request is intentionally not linked into the mutation
/// cancellation token. Once the command has been accepted, a browser disconnect
/// must not tear down a partially-mutated Docker/Postgres workflow. The operation
/// remains bounded by an explicit timeout and by application shutdown.
/// </summary>
public sealed class CreateChatStackRuntimeOperationLifetime
{
    // Current readiness verification can legitimately consume several minutes,
    // and image pulls/startup add additional bounded headroom. Twenty minutes is
    // intentionally well above those normal stage windows while still preventing
    // a detached create-stack mutation from running indefinitely.
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(20);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<CreateChatStackRuntimeOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public CreateChatStackRuntimeOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<CreateChatStackRuntimeOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal CreateChatStackRuntimeOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<CreateChatStackRuntimeOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Create-stack operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    internal CreateChatStackRuntimeOperationScope Begin(
        Guid stackId,
        string stackSlug,
        CancellationToken requestAborted)
    {
        // Before any durable/mutating operation begins, an already-dead request
        // has not earned a server-owned mutation lifetime.
        requestAborted.ThrowIfCancellationRequested();

        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        var requestAbortRegistration = requestAborted.Register(() =>
        {
            _logger.LogWarning(
                "Create-stack HTTP request was aborted after the server-owned operation began. The mutation will continue until completion, operation timeout, or application shutdown. StackId={StackId} StackSlug={StackSlug} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                stackId,
                stackSlug,
                _operationTimeout.TotalMinutes);
        });

        return new CreateChatStackRuntimeOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class CreateChatStackRuntimeOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public CreateChatStackRuntimeOperationScope(
        CancellationTokenSource operationSource,
        CancellationTokenSource timeoutSource,
        CancellationTokenRegistration requestAbortRegistration,
        CancellationToken requestAborted,
        CancellationToken applicationStopping)
    {
        _operationSource = operationSource;
        _timeoutSource = timeoutSource;
        _requestAbortRegistration = requestAbortRegistration;
        _requestAborted = requestAborted;
        _applicationStopping = applicationStopping;
    }

    public CancellationToken CancellationToken => _operationSource.Token;

    public bool RequestAborted => _requestAborted.IsCancellationRequested;

    public bool OperationTimeoutRequested => _timeoutSource.IsCancellationRequested;

    public bool ApplicationStoppingRequested => _applicationStopping.IsCancellationRequested;

    public void Dispose()
    {
        _requestAbortRegistration.Dispose();
        _operationSource.Dispose();
        _timeoutSource.Dispose();
    }
}
