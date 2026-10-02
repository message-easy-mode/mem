namespace HostAgent.Runtime.Coturn;

/// <summary>
/// Serializes admission of shared platform-Coturn mutations inside the one MEM
/// Control Plane process. RuntimeOperation persistence remains the durable
/// source of truth after admission; this gate closes the small in-process race
/// between conflict detection and RuntimeOperation creation.
/// </summary>
public sealed class CoturnPlatformMutationAdmissionGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
