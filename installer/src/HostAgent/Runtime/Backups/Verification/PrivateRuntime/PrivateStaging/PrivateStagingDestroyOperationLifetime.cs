using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted private-staging retirement.
///
/// Retiring a private restore test is a destructive host mutation. Once the retained
/// staging record has been resolved and retirement begins, browser navigation,
/// reverse-proxy disconnect, or fetch cancellation must not stop cleanup halfway through.
/// The operation remains bounded by an explicit timeout and application shutdown.
/// </summary>
public sealed class PrivateStagingDestroyOperationLifetime
{
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(5);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<PrivateStagingDestroyOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public PrivateStagingDestroyOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<PrivateStagingDestroyOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal PrivateStagingDestroyOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<PrivateStagingDestroyOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Private-staging retirement timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    internal PrivateStagingDestroyOperationScope BeginAfterAcceptance(
        string stagingId,
        CancellationToken requestAborted)
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        var requestAbortRegistration = requestAborted.Register(() =>
        {
            _logger.LogWarning(
                "Private Restore Test retirement HTTP request was aborted after cleanup began. Retirement will continue until completion, operation timeout, or application shutdown. StagingId={StagingId} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                stagingId,
                _operationTimeout.TotalMinutes);
        });

        return new PrivateStagingDestroyOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class PrivateStagingDestroyOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public PrivateStagingDestroyOperationScope(
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
