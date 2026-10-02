namespace HostAgent.Runtime.Stacks.Diagnostics;

public sealed class RuntimeStackDoctorExecutionLimiter
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    private readonly TimeSpan _timeout;

    public RuntimeStackDoctorExecutionLimiter()
        : this(DefaultTimeout)
    {
    }

    internal RuntimeStackDoctorExecutionLimiter(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken requestCancellation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            requestCancellation);
        var operationTask = operation(executionCancellation.Token);

        try
        {
            return await operationTask.WaitAsync(_timeout, requestCancellation);
        }
        catch (TimeoutException ex)
        {
            executionCancellation.Cancel();
            _ = ObserveAbandonedOperationAsync(operationTask);
            throw new RuntimeStackDoctorTimeoutException(_timeout, ex);
        }
    }

    private static async Task ObserveAbandonedOperationAsync(Task operationTask)
    {
        try
        {
            await operationTask;
        }
        catch
        {
            // The operator-visible timeout is already authoritative. Observe any
            // later cancellation/fault so it cannot surface as an unobserved task.
        }
    }
}

public sealed class RuntimeStackDoctorTimeoutException : TimeoutException
{
    public RuntimeStackDoctorTimeoutException(TimeSpan timeout, Exception innerException)
        : base(
            $"Runtime stack Doctor exceeded its {Math.Round(timeout.TotalSeconds)}-second execution limit.",
            innerException)
    {
        Timeout = timeout;
    }

    public TimeSpan Timeout { get; }
}
