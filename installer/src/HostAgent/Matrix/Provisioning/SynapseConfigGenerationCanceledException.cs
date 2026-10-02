namespace HostAgent.Matrix.Provisioning;

public sealed class SynapseConfigGenerationCanceledException : OperationCanceledException
{
    public SynapseConfigGenerationCanceledException(
        Guid instanceId,
        string stage,
        string? containerId,
        bool callerCancellationRequested,
        bool exceptionCancellationRequested,
        TimeSpan elapsed,
        Exception innerException)
        : base(
            $"Synapse configuration generation was cancelled during '{stage}'. " +
            $"Caller cancellation requested: {callerCancellationRequested}.",
            innerException)
    {
        InstanceId = instanceId;
        Stage = stage;
        ContainerId = containerId;
        CallerCancellationRequested = callerCancellationRequested;
        ExceptionCancellationRequested = exceptionCancellationRequested;
        Elapsed = elapsed;
    }

    public Guid InstanceId { get; }
    public string Stage { get; }
    public string? ContainerId { get; }
    public bool CallerCancellationRequested { get; }
    public bool ExceptionCancellationRequested { get; }
    public TimeSpan Elapsed { get; }
}
