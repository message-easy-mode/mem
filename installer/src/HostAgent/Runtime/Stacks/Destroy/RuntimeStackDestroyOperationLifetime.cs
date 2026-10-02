using Microsoft.Extensions.Hosting;

namespace HostAgent.Runtime.Stacks.Destroy;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted stack-destroy mutation.
/// The initiating HTTP request is deliberately excluded: after the server returns
/// HTTP 202, browser navigation or transport loss must not interrupt a partially
/// destructive workflow.
/// </summary>
public sealed class RuntimeStackDestroyOperationLifetime
{
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(15);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly TimeSpan _operationTimeout;

    public RuntimeStackDestroyOperationLifetime(
        IHostApplicationLifetime applicationLifetime)
        : this(applicationLifetime, DefaultOperationTimeout)
    {
    }

    internal RuntimeStackDestroyOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Destroy-stack operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _operationTimeout = operationTimeout;
    }

    internal RuntimeStackDestroyOperationScope Begin()
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        return new RuntimeStackDestroyOperationScope(
            operationSource,
            timeoutSource,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class RuntimeStackDestroyOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationToken _applicationStopping;

    public RuntimeStackDestroyOperationScope(
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
