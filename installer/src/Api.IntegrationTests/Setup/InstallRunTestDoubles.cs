using System.Collections.Concurrent;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

internal sealed class RecordingInstallRunCoordinator : IInstallRunCoordinator
{
    private readonly ConcurrentDictionary<Guid, byte> _owned = new();
    private int _queueCalls;
    private int _acceptedQueueCount;

    public int QueueCalls => Volatile.Read(ref _queueCalls);
    public int AcceptedQueueCount => Volatile.Read(ref _acceptedQueueCount);

    public InstallRunQueueResult Queue(Guid installationId)
    {
        Interlocked.Increment(ref _queueCalls);

        var queued = _owned.TryAdd(installationId, 0);
        if (queued)
        {
            Interlocked.Increment(ref _acceptedQueueCount);
        }

        return new InstallRunQueueResult(
            installationId,
            Queued: queued,
            AlreadyOwned: !queued);
    }
}

internal sealed class BlockingInstallRunner : IInstallRunner
{
    private readonly TaskCompletionSource<bool> _started = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private int _runCount;

    public Task Started => _started.Task;
    public int RunCount => Volatile.Read(ref _runCount);

    public void Release() => _release.TrySetResult(true);

    public async Task RunAsync(Guid installationId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _runCount);
        _started.TrySetResult(true);
        await _release.Task.WaitAsync(cancellationToken);
    }
}
