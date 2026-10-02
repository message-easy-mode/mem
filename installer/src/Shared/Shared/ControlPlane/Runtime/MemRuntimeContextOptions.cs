namespace Shared.ControlPlane.Runtime;

public sealed class MemRuntimeContextOptions
{
    public const string SectionName = "MemRuntime";

    public string Mode { get; set; } = string.Empty;

    public string StateRoot { get; set; } = string.Empty;

    public string DockerEndpoint { get; set; } = "unix:///var/run/docker.sock";

    public string? ContainerName { get; set; }

    /// <summary>
    /// Host IPv4 discovered outside the Control Plane container. It is used
    /// only as a server-owned candidate for private browser-facing managed
    /// service authorities and is never inferred from container networking.
    /// </summary>
    public string? HostAccessIpv4 { get; set; }

    public string? UiDeliveryMode { get; set; }

    public string InstanceIdentityFileName { get; set; } =
        "control-plane/runtime-instance.json";

    public string? ExpectedInstanceId { get; set; }

    public bool AllowSharedDockerHost { get; set; }
}

public sealed record MemRuntimeEnvironmentSnapshot(
    string EnvironmentName,
    string ApplicationName,
    string ContentRootPath,
    string? WebRootPath,
    bool RunningInContainer,
    bool DevelopmentSetupTokenConfigured,
    string Version,
    string? Commit);

public sealed class MemRuntimeContextValidationException(
    string code,
    string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
