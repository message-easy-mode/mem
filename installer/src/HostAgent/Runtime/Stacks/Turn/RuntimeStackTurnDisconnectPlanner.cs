namespace HostAgent.Runtime.Stacks.Turn;

public static class RuntimeStackTurnDisconnectPlanner
{
    public static RuntimeStackTurnDisconnectPlan Plan(
        RuntimeStackTurnInspectionResponse inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        if (!inspection.MatrixRuntime.Exists ||
            !inspection.MatrixRuntime.Running ||
            !inspection.MatrixRuntime.IdentityMatches)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_disconnect_matrix_runtime_unavailable",
                "The active Matrix runtime could not be verified safely.");
        }

        if (string.Equals(
                inspection.State,
                RuntimeStackTurnStates.NotConnected,
                StringComparison.Ordinal))
        {
            return new RuntimeStackTurnDisconnectPlan(
                ConfigurationChangeRequired: false,
                RestartRequired: false,
                Detail: "This stack is already disconnected from TURN.");
        }

        if (string.Equals(
                inspection.State,
                RuntimeStackTurnStates.Connected,
                StringComparison.Ordinal) &&
            string.Equals(
                inspection.Management,
                RuntimeStackTurnManagementKinds.MemManaged,
                StringComparison.Ordinal) &&
            inspection.PersistedMetadata.Recorded &&
            inspection.PersistedMetadata.Configured == true &&
            inspection.LiveConfiguration is
            {
                Supported: true,
                AnyTurnSettings: true,
                MemManagedMarkerPresent: true
            })
        {
            return new RuntimeStackTurnDisconnectPlan(
                ConfigurationChangeRequired: true,
                RestartRequired: true,
                Detail: "MEM can remove the verified MEM-managed TURN settings and restart Matrix safely.");
        }

        var (code, detail) = inspection.State switch
        {
            RuntimeStackTurnStates.External => (
                "turn_disconnect_external_configuration",
                "Synapse contains TURN settings that MEM does not manage. MEM will not remove them automatically."),
            RuntimeStackTurnStates.Drift => (
                "turn_disconnect_drift_requires_attention",
                "The live TURN configuration and recorded stack association disagree. Resolve the drift before disconnecting."),
            RuntimeStackTurnStates.Unknown => (
                "turn_disconnect_state_unavailable",
                "The active Synapse TURN state is unavailable, so MEM will not remove any settings."),
            _ => (
                "turn_disconnect_state_not_supported",
                "The current stack TURN state is not safe for automatic disconnection.")
        };

        throw new RuntimeStackTurnConnectionException(code, detail);
    }
}
