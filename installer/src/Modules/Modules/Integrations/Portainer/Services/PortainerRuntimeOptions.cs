namespace Modules.Integrations.Portainer.Services;

/// <summary>
/// Server-owned Portainer runtime policy. The setup plan decides whether the
/// optional support tool is installed; this contract fixes the image, runtime
/// identity, storage, and direct-listener policy used when it is installed.
/// </summary>
public sealed class PortainerRuntimeOptions
{
    public const string SectionName = "Diagnostics:Portainer";
    public const string ApprovedVersion = "2.39.5";
    public const string ApprovedImage = "portainer/portainer-ce:2.39.5";

    public string ApprovedImageReference { get; set; } = ApprovedImage;

    public string ExpectedVersion { get; set; } = ApprovedVersion;

    public string ContainerName { get; set; } = "portainer";

    public string DataVolumeName { get; set; } = "portainer_data";

    public int PreferredHttpsHostPort { get; set; } = 9443;

    /// <summary>
    /// Operational start/status requests must never pull. An exact image may be
    /// pulled only by the explicit setup preparation path.
    /// </summary>
    public bool AllowOperationalPull { get; set; }

    /// <summary>
    /// Optional authoritative operator URL used for normal Portainer handoff.
    /// MEM never derives this from browser input. Host-native local development
    /// may separately project a bounded localhost URL from the live published
    /// Portainer HTTPS port so first-time setup and manual recovery remain
    /// reachable before the private handoff configuration is complete.
    /// </summary>
    public string? UiUrl { get; set; }

    /// <summary>
    /// Optional Portainer environment identifier used by the later handoff
    /// slice. It is configuration, not browser input.
    /// </summary>
    public int? EnvironmentId { get; set; }

    public string[] SupportedArchitectures { get; set; } = ["amd64", "arm64"];
}
