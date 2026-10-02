namespace Shared.Diagnostics;

/// <summary>
/// Server-owned lifecycle boundary for incidents that can prove self-recovery.
/// This does not represent an operator action and must not create an operator audit actor.
/// </summary>
public interface IMemDiagnosticIncidentLifecycleWriter
{
    Task ResolveSelfRecoveredAsync(
        string incidentId,
        DateTimeOffset observedThroughAtUtc,
        string observedThroughEventId,
        CancellationToken cancellationToken = default);
}
