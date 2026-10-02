using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.Workspace.PrivateTest;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted Restore Workspace private-test mutation.
///
/// The initiating HTTP request is deliberately excluded after the restore coordinator has
/// durably created the linked RuntimeOperation. Browser navigation, reverse-proxy disconnect,
/// or transport loss must not cancel an in-progress private PostgreSQL/Synapse staging run.
/// The accepted operation remains bounded by an explicit timeout and application shutdown.
/// </summary>
public sealed class RestoreWorkspacePrivateTestOperationLifetime
{
    // Private staging can import a real Synapse database and then wait for PostgreSQL and
    // Synapse readiness. Twenty minutes leaves bounded headroom beyond the internal readiness
    // windows without allowing a detached private-test mutation to run indefinitely.
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(20);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<RestoreWorkspacePrivateTestOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public RestoreWorkspacePrivateTestOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<RestoreWorkspacePrivateTestOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal RestoreWorkspacePrivateTestOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<RestoreWorkspacePrivateTestOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Private-test operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    internal RestoreWorkspacePrivateTestOperationScope BeginAfterAcceptance(
        string restoreSessionId,
        Guid runtimeOperationId,
        CancellationToken requestAborted)
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        var requestAbortRegistration = requestAborted.Register(() =>
        {
            _logger.LogWarning(
                "Private Restore Test HTTP request was aborted after the server-owned operation began. The private test will continue until completion, operation timeout, or application shutdown. RestoreSessionId={RestoreSessionId} OperationId={OperationId} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                restoreSessionId,
                runtimeOperationId,
                _operationTimeout.TotalMinutes);
        });

        return new RestoreWorkspacePrivateTestOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class RestoreWorkspacePrivateTestOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public RestoreWorkspacePrivateTestOperationScope(
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
