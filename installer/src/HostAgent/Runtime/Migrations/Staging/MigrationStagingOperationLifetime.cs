using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Migrations.Staging;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted private Migration staging run.
///
/// Before a staging run is durably accepted, the initiating HTTP request may cancel normal
/// validation and persistence work. Once the run exists in durable state, browser navigation,
/// transport loss, or fetch cancellation must not tear down the private-test operation. The
/// accepted operation remains bounded by an explicit timeout and Control Plane shutdown.
/// </summary>
public sealed class MigrationStagingOperationLifetime
{
    // A private test can restore a real Synapse database, copy media, start three containers,
    // and perform readiness checks. Keep the accepted operation bounded while leaving enough
    // headroom for large migrations on modest hardware.
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromHours(4);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<MigrationStagingOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public MigrationStagingOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<MigrationStagingOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal MigrationStagingOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<MigrationStagingOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Migration staging operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    internal MigrationStagingOperationScope BeginAfterAcceptance(
        string migrationId,
        string stagingRunId,
        CancellationToken requestAborted)
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        var requestAbortRegistration = requestAborted.Register(() =>
        {
            _logger.LogWarning(
                "Migration private-staging HTTP request was aborted after the durable run was accepted. Private staging will continue until completion, operation timeout, or Control Plane shutdown. MigrationId={MigrationId} StagingRunId={StagingRunId} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                migrationId,
                stagingRunId,
                _operationTimeout.TotalMinutes);
        });

        return new MigrationStagingOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class MigrationStagingOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public MigrationStagingOperationScope(
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
