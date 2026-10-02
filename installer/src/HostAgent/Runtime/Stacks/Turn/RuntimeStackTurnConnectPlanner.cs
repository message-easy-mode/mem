namespace HostAgent.Runtime.Stacks.Turn;

public static class RuntimeStackTurnConnectPlanner
{
    public static RuntimeStackTurnConnectPlan Plan(
        RuntimeStackTurnInspectionResponse inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        if (inspection.Platform is null ||
            !string.Equals(
                inspection.Platform.Readiness,
                "ready",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_platform_not_ready",
                "The platform TURN service must be Ready before this stack can be connected.");
        }

        if (!inspection.MatrixRuntime.Exists ||
            !inspection.MatrixRuntime.Running ||
            !inspection.MatrixRuntime.IdentityMatches)
        {
            throw new RuntimeStackTurnConnectionException(
                "turn_connect_matrix_runtime_unavailable",
                "The active Matrix runtime could not be verified safely.");
        }

        if (CanAdoptExisting(inspection))
        {
            return new RuntimeStackTurnConnectPlan(
                RuntimeStackTurnConnectModes.AdoptExisting,
                ConfigurationChangeRequired: false,
                RestartRequired: false,
                "The live Synapse TURN settings already match the ready MEM platform. MEM can record the durable association without rewriting or restarting Matrix.");
        }

        if (string.Equals(
                inspection.State,
                RuntimeStackTurnStates.Connected,
                StringComparison.Ordinal) &&
            string.Equals(
                inspection.Management,
                RuntimeStackTurnManagementKinds.MemManaged,
                StringComparison.Ordinal))
        {
            return new RuntimeStackTurnConnectPlan(
                RuntimeStackTurnConnectModes.NoChange,
                ConfigurationChangeRequired: false,
                RestartRequired: false,
                "This stack is already connected to MEM-managed TURN.");
        }

        if (string.Equals(
                inspection.State,
                RuntimeStackTurnStates.NotConnected,
                StringComparison.Ordinal))
        {
            return new RuntimeStackTurnConnectPlan(
                RuntimeStackTurnConnectModes.Configure,
                ConfigurationChangeRequired: true,
                RestartRequired: true,
                "Synapse has no TURN settings. MEM can add the current platform TURN configuration and restart Matrix safely.");
        }

        if (CanReplaceExternal(inspection))
        {
            return new RuntimeStackTurnConnectPlan(
                RuntimeStackTurnConnectModes.ReplaceExternal,
                ConfigurationChangeRequired: true,
                RestartRequired: true,
                "Synapse is using external TURN. MEM can replace those exact reviewed settings with the current platform TURN configuration after explicit confirmation, while retaining a private rollback snapshot.");
        }

        var (code, detail) = inspection.State switch
        {
            RuntimeStackTurnStates.External => (
                "turn_connect_external_configuration",
                "Synapse contains TURN settings that MEM does not manage. MEM will not overwrite them automatically."),
            RuntimeStackTurnStates.Unknown => (
                "turn_connect_state_unavailable",
                "The active Synapse TURN state is unavailable, so MEM will not change it."),
            RuntimeStackTurnStates.Drift => (
                "turn_connect_drift_requires_attention",
                "The live TURN configuration and recorded stack state disagree in a way that cannot be adopted automatically."),
            _ => (
                "turn_connect_state_not_supported",
                "The current stack TURN state is not safe for automatic connection.")
        };

        throw new RuntimeStackTurnConnectionException(code, detail);
    }

    public static bool CanReplaceExternal(
        RuntimeStackTurnInspectionResponse inspection)
    {
        var live = inspection.LiveConfiguration;

        return string.Equals(
                   inspection.State,
                   RuntimeStackTurnStates.External,
                   StringComparison.Ordinal) &&
               string.Equals(
                   inspection.Management,
                   RuntimeStackTurnManagementKinds.ExternalObserved,
                   StringComparison.Ordinal) &&
               live is not null &&
               live.Supported &&
               live.AnyTurnSettings;
    }

    public static bool CanAdoptExisting(
        RuntimeStackTurnInspectionResponse inspection)
    {
        var live = inspection.LiveConfiguration;
        var platform = inspection.Platform;

        var unrecordedMatchingPlatform =
            string.Equals(
                inspection.State,
                RuntimeStackTurnStates.Drift,
                StringComparison.Ordinal) &&
            string.Equals(
                inspection.Management,
                RuntimeStackTurnManagementKinds.MemManaged,
                StringComparison.Ordinal) &&
            !inspection.PersistedMetadata.Recorded;

        var migrationPreservedMatchingPlatform =
            inspection.PersistedMetadata.Recorded &&
            string.Equals(
                inspection.PersistedMetadata.ConfigurationSource,
                "migration-source-preserved",
                StringComparison.OrdinalIgnoreCase);

        return (unrecordedMatchingPlatform || migrationPreservedMatchingPlatform) &&
               live is not null &&
               live.Supported &&
               live.AnyTurnSettings &&
               live.SharedSecretMatchesPlatform == true &&
               platform is not null &&
               SetsEqual(live.TurnUris, platform.TurnUris);
    }

    private static bool SetsEqual(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right) =>
        left.Select(NormalizeUri)
            .OrderBy(value => value, StringComparer.Ordinal)
            .SequenceEqual(
                right.Select(NormalizeUri)
                    .OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal);

    private static string NormalizeUri(string value) =>
        value.Trim().ToLowerInvariant();
}
