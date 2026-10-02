using Shared.ControlPlane;

namespace Shared.ControlPlane.Runtime;

public sealed record MemControlPlaneRuntimeContext(
    int SchemaVersion,
    string RuntimeMode,
    Guid ControlPlaneInstanceId,
    Guid ApiProcessInstanceId,
    string EnvironmentName,
    bool RunningInContainer,
    string ContentRootPath,
    string ContentRootKind,
    string StateRootPath,
    string StateRootKind,
    string StateRootProfile,
    string UiDeliveryMode,
    Uri DockerEndpoint,
    string DockerEndpointKind,
    string? ConfiguredContainerName,
    string ApplicationName,
    string Version,
    string? Commit,
    string ValidationState,
    bool MutationsAllowed,
    bool ShowDevelopmentBanner,
    IReadOnlyList<string> Warnings)
{
    public bool AllowSharedDockerHost { get; init; }

    /// <summary>
    /// Server-side host IPv4 supplied by the bootstrap/runtime boundary. This
    /// is intentionally omitted from the generic safe runtime projection; a
    /// feature may expose only a validated purpose-specific browser authority.
    /// </summary>
    public string? HostAccessIpv4 { get; init; }

    public MemControlPlaneRuntimeContextProjection ToSafeProjection() =>
        new(
            SchemaVersion,
            MemControlPlaneIdentity.ProductDisplayName,
            ApplicationName,
            RuntimeMode,
            ControlPlaneInstanceId,
            ApiProcessInstanceId,
            EnvironmentName,
            RunningInContainer,
            ContentRootKind,
            StateRootKind,
            StateRootProfile,
            UiDeliveryMode,
            DockerEndpointKind,
            ConfiguredContainerName,
            Version,
            Commit,
            ValidationState,
            MutationsAllowed,
            ShowDevelopmentBanner,
            Warnings)
        {
            Restart = MemRestartContractFactory.Create(this)
        };
}

public sealed record MemControlPlaneRuntimePreflightProjection(
    int SchemaVersion,
    string ProductDisplayName,
    string RuntimeMode,
    Guid ControlPlaneInstanceId,
    Guid ApiProcessInstanceId,
    string UiDeliveryMode,
    string Version,
    string? Commit,
    string ValidationState,
    bool ShowDevelopmentBanner);

/// <summary>
/// Browser- and support-report-safe runtime projection. Exact content-root,
/// state-root and Docker endpoint values remain server-side.
/// </summary>
public sealed record MemControlPlaneRuntimeContextProjection(
    int SchemaVersion,
    string ProductDisplayName,
    string ApplicationName,
    string RuntimeMode,
    Guid ControlPlaneInstanceId,
    Guid ApiProcessInstanceId,
    string EnvironmentName,
    bool RunningInContainer,
    string ContentRootKind,
    string StateRootKind,
    string StateRootProfile,
    string UiDeliveryMode,
    string DockerEndpointKind,
    string? ConfiguredContainerName,
    string Version,
    string? Commit,
    string ValidationState,
    bool MutationsAllowed,
    bool ShowDevelopmentBanner,
    IReadOnlyList<string> Warnings)
{
    public MemRestartContract Restart { get; init; } = new(
        MemRestartKinds.Unsupported,
        "restart_supervisor_unknown",
        Command: null,
        CommandAvailable: false);

    public MemDockerOwnershipProjection DockerOwnership { get; init; } =
        MemDockerOwnershipProjection.Unchecked;

    public MemControlPlaneExposureProjection ControlPlaneExposure { get; init; } =
        MemControlPlaneExposureProjection.Unchecked;
}

public sealed record MemControlPlaneExposureProjection(
    string State,
    string AccessMode,
    string? HostAddress,
    int? HostPort,
    int BindingCount,
    string? WarningCode)
{
    public bool IsPrivate => string.Equals(
        State,
        MemControlPlaneExposureStates.Private,
        StringComparison.Ordinal);

    public static MemControlPlaneExposureProjection Unchecked { get; } = new(
        MemControlPlaneExposureStates.Unchecked,
        MemControlPlaneAccessModes.Unknown,
        HostAddress: null,
        HostPort: null,
        BindingCount: 0,
        WarningCode: null);

    public static MemControlPlaneExposureProjection NotApplicable { get; } = new(
        MemControlPlaneExposureStates.NotApplicable,
        MemControlPlaneAccessModes.LocalDevelopment,
        HostAddress: null,
        HostPort: null,
        BindingCount: 0,
        WarningCode: null);
}

public static class MemControlPlaneExposureStates
{
    public const string Private = "private";
    public const string NeedsAttention = "needs-attention";
    public const string Unavailable = "unavailable";
    public const string NotApplicable = "not-applicable";
    public const string Unchecked = "unchecked";
}

public static class MemControlPlaneAccessModes
{
    public const string SshTunnel = "ssh-tunnel";
    public const string TrustedLan = "trusted-lan";
    public const string Unsupported = "unsupported";
    public const string LocalDevelopment = "local-development";
    public const string Unknown = "unknown";
}
