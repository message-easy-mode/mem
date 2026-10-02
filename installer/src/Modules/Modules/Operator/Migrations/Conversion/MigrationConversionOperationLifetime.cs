using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Modules.Operator.Migrations.Conversion;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted Migration conversion.
///
/// The initiating HTTP request deliberately stops owning cancellation once the
/// durable conversion attempt has been created. Browser navigation, transport
/// loss, or a cancelled fetch must not turn an accepted conversion into a false
/// failure. The accepted operation remains bounded by an explicit timeout and
/// by Control Plane shutdown.
/// </summary>
public sealed class MigrationConversionOperationLifetime
{
    // Conversion can process a real Synapse database and may legitimately take
    // substantially longer than ordinary readiness checks. Four hours leaves
    // bounded headroom for large conversion jobs without allowing a detached
    // worker to run indefinitely.
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromHours(4);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<MigrationConversionOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public MigrationConversionOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<MigrationConversionOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal MigrationConversionOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<MigrationConversionOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Migration conversion operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    internal MigrationConversionOperationScope BeginAfterAcceptance(
        string migrationId,
        string conversionAttemptId,
        CancellationToken requestAborted)
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        var requestAbortRegistration = requestAborted.Register(() =>
        {
            _logger.LogWarning(
                "Migration conversion HTTP request was aborted after the durable attempt was accepted. The conversion will continue until completion, operation timeout, or Control Plane shutdown. MigrationId={MigrationId} ConversionAttemptId={ConversionAttemptId} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                migrationId,
                conversionAttemptId,
                _operationTimeout.TotalMinutes);
        });

        return new MigrationConversionOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class MigrationConversionOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public MigrationConversionOperationScope(
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
