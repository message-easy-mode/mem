using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnStartupSupervisionState
{
    private CoturnStartupSupervisionResponse _current;

    public CoturnStartupSupervisionState(
        MemControlPlaneRuntimeContext runtimeContext,
        TimeProvider timeProvider)
    {
        StartupBoundaryAtUtc = timeProvider.GetUtcNow();
        var enabled = CoturnStartupSupervisionPolicy.IsEnabled(runtimeContext);
        _current = new CoturnStartupSupervisionResponse(
            Source: "control-plane",
            Status: enabled
                ? CoturnStartupSupervisionStatuses.Waiting
                : CoturnStartupSupervisionStatuses.Disabled,
            RuntimeMode: runtimeContext.RuntimeMode,
            Enabled: enabled,
            Decision: CoturnStartupSupervisionDecisions.NotEvaluated,
            MutationPerformed: false,
            AutomaticRestartAttempted: false,
            CooldownActive: false,
            CooldownUntilUtc: null,
            OperationId: null,
            ObservedAtUtc: StartupBoundaryAtUtc,
            Detail: enabled
                ? "Coturn startup supervision is waiting for Docker and platform runtime evidence."
                : "Coturn automatic startup supervision is disabled outside an authorized containerized MEM mutation runtime.");
    }

    /// <summary>
    /// Process-local startup boundary used to distinguish abandoned Coturn
    /// operations from new operations accepted by this API process.
    /// </summary>
    public DateTimeOffset StartupBoundaryAtUtc { get; }

    public CoturnStartupSupervisionResponse Read() =>
        Volatile.Read(ref _current);

    public void Set(CoturnStartupSupervisionResponse value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Volatile.Write(ref _current, value);
    }
}
