using Microsoft.Extensions.Hosting;

namespace HostAgent.Runtime.Coturn;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted shared Coturn
/// maintenance mutation. The initiating HTTP request is deliberately excluded:
/// browser navigation or transport loss after HTTP 202 must not interrupt a
/// partially restarted or recreated TURN runtime.
/// </summary>
public sealed class CoturnPlatformMaintenanceOperationLifetime
{
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(20);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeSpan _operationTimeout;

    public CoturnPlatformMaintenanceOperationLifetime(
        IHostApplicationLifetime applicationLifetime)
        : this(applicationLifetime, DefaultOperationTimeout)
    {
    }

    internal CoturnPlatformMaintenanceOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Coturn maintenance operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _operationTimeout = operationTimeout;
    }

    internal CoturnPlatformMaintenanceOperationScope Begin()
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        return new CoturnPlatformMaintenanceOperationScope(
            operationSource,
            timeoutSource,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class CoturnPlatformMaintenanceOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationToken _applicationStopping;

    public CoturnPlatformMaintenanceOperationScope(
        CancellationTokenSource operationSource,
        CancellationTokenSource timeoutSource,
        CancellationToken applicationStopping)
    {
        _operationSource = operationSource;
        _timeoutSource = timeoutSource;
        _applicationStopping = applicationStopping;
    }

    public CancellationToken CancellationToken => _operationSource.Token;

    public bool OperationTimeoutRequested => _timeoutSource.IsCancellationRequested;

    public bool ApplicationStoppingRequested => _applicationStopping.IsCancellationRequested;

    public void Dispose()
    {
        _operationSource.Dispose();
        _timeoutSource.Dispose();
    }
}
