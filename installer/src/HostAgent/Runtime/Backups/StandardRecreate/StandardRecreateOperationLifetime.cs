using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted Standard Recreate mutation.
///
/// The initiating HTTP request is deliberately excluded after target claims and the
/// durable runtime operation have been committed. Browser navigation, a reverse-proxy
/// disconnect, or transport loss must not cancel a partially-mutated restore. The
/// accepted operation remains bounded by an explicit timeout and application shutdown.
/// </summary>
public sealed class StandardRecreateOperationLifetime
{
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(20);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<StandardRecreateOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public StandardRecreateOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<StandardRecreateOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal StandardRecreateOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<StandardRecreateOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Standard Recreate operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    /// <summary>
    /// Begins the server-owned lifetime after the restore coordinator has durably
    /// reserved targets and created the RuntimeOperation row. An already-aborted
    /// HTTP request is therefore evidence to record, not authority to undo or stop
    /// the accepted mutation.
    /// </summary>
    internal StandardRecreateOperationScope BeginAfterAcceptance(
        string restoreSessionId,
        string recreateId,
        Guid runtimeOperationId,
        CancellationToken requestAborted)
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        void LogRequestAbort()
        {
            _logger.LogWarning(
                "Standard Recreate HTTP request was aborted after the server-owned operation began. The restore will continue until completion, operation timeout, or application shutdown. RestoreSessionId={RestoreSessionId} RecreateId={RecreateId} OperationId={OperationId} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                restoreSessionId,
                recreateId,
                runtimeOperationId,
                _operationTimeout.TotalMinutes);
        }

        // CancellationToken.Register invokes the callback immediately when the
        // request was already aborted, so one registration covers both the
        // already-aborted and later-disconnect cases without duplicate evidence.
        var requestAbortRegistration = requestAborted.Register(LogRequestAbort);

        return new StandardRecreateOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class StandardRecreateOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public StandardRecreateOperationScope(
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
