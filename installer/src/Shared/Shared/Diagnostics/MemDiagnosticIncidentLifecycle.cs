namespace Shared.Diagnostics;

/// <summary>
/// Append-only Diagnostics event hints used by server-owned workflows that can
/// prove an incident has self-recovered without inventing an operator action.
/// </summary>
public static class MemDiagnosticIncidentLifecycle
{
    public const string StateDetailKey = "incidentLifecycleState";
    public const string ResolutionCodeDetailKey = "incidentResolutionCode";

    public const string ResolvedState = "resolved";
    public const string SelfRecoveredResolutionCode = "self_recovered";
}
