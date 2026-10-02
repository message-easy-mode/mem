namespace Shared.ControlPlane.Runtime;

public static class MemRestartKinds
{
    public const string DeveloperProcess = "developer-process";
    public const string Container = "container";
    public const string TestHarness = "test-harness";
    public const string Unsupported = "unsupported";
}

public sealed record MemRestartContract(
    string Kind,
    string GuidanceCode,
    string? Command,
    bool CommandAvailable);

public static class MemRestartContractFactory
{
    public static MemRestartContract Create(
        MemControlPlaneRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);

        if (string.Equals(
                runtimeContext.RuntimeMode,
                MemRuntimeModes.LocalDevelopment,
                StringComparison.Ordinal))
        {
            return new MemRestartContract(
                MemRestartKinds.DeveloperProcess,
                "restart_local_api_process",
                Command: null,
                CommandAvailable: false);
        }

        if (MemRuntimeModes.IsContainerized(runtimeContext.RuntimeMode) &&
            !string.IsNullOrWhiteSpace(runtimeContext.ConfiguredContainerName))
        {
            var name = runtimeContext.ConfiguredContainerName.Trim();
            return new MemRestartContract(
                MemRestartKinds.Container,
                "restart_control_plane_container",
                $"sudo docker restart {name}",
                CommandAvailable: true);
        }

        if (string.Equals(
                runtimeContext.RuntimeMode,
                MemRuntimeModes.AutomatedTest,
                StringComparison.Ordinal))
        {
            return new MemRestartContract(
                MemRestartKinds.TestHarness,
                "restart_test_harness",
                Command: null,
                CommandAvailable: false);
        }

        return new MemRestartContract(
            MemRestartKinds.Unsupported,
            "restart_supervisor_unknown",
            Command: null,
            CommandAvailable: false);
    }
}

public sealed record MemDockerOwnershipProjection(
    string State,
    bool MutationsAllowed,
    bool DevelopmentOverrideActive,
    IReadOnlyList<string> CompetingContainers,
    string? WarningCode)
{
    public static MemDockerOwnershipProjection Unchecked { get; } = new(
        "unchecked",
        MutationsAllowed: true,
        DevelopmentOverrideActive: false,
        CompetingContainers: [],
        WarningCode: null);
}
