namespace Shared.ControlPlane;

/// <summary>
/// Canonical and compatibility-safe names for the permanent MEM Control Plane
/// runtime. Bootstrap and the permanent API use canonical names while the
/// nested Legacy and Compatibility contracts preserve reviewed upgrade paths.
/// </summary>
public static class MemControlPlaneIdentity
{
    public const string ProductName = "Message Easy Mode";
    public const string ProductShortName = "MEM";
    public const string ProductDisplayName = "MEM Control Plane";

    public const string CanonicalPublisherNamespace = "message-easy-mode";
    public const string CanonicalContainerName = "mem-control-plane";
    public const string DevelopmentContainerName = "mem-control-plane-dev";
    public const string DevelopmentVolumeName = "mem-control-plane-dev-data";
    public const string CanonicalRegistryPublisher = "ghcr.io/" + CanonicalPublisherNamespace;
    public const string CanonicalImageRepository = CanonicalRegistryPublisher + "/mem-control-plane";
    public const string CanonicalStableImage = CanonicalImageRepository + ":stable";
    public const string CanonicalDevelopmentImage = CanonicalImageRepository + ":dev";
    public const string CanonicalLocalImage = "mem-control-plane:local";
    public const string CanonicalNewInstallVolumeName = "mem-control-plane-data";
    public const string CanonicalCertificateFileName = "mem-control-plane.crt";
    public const string CanonicalPrivateKeyFileName = "mem-control-plane.key";
    public const string CanonicalCertificateSubjectCommonName = ProductDisplayName;

    public static class EnvironmentVariables
    {
        public const string SetupToken = "MEM_CONTROL_PLANE_SETUP_TOKEN";
        public const string SetupTokenPath = "MEM_CONTROL_PLANE_SETUP_TOKEN_PATH";
        public const string Channel = "MEM_CONTROL_PLANE_CHANNEL";
        public const string PublicPort = "MEM_CONTROL_PLANE_PUBLIC_PORT";
        public const string HostIpv4 = "MEM_CONTROL_PLANE_HOST_IPV4";
        public const string InstanceId = "MEM_CONTROL_PLANE_INSTANCE_ID";
        public const string RuntimeMode = "MEM_RUNTIME_MODE";
        public const string StateRoot = "MEM_STATE_ROOT";
        public const string ProductVersion = "MEM_PRODUCT_VERSION";
        public const string CommitSha = "MEM_COMMIT_SHA";
        public const string AllowSharedDockerHost = "MEM_DEV_ALLOW_SHARED_DOCKER_HOST";
    }

    /// <summary>
    /// Canonical labels for resources created by the Control Plane. They are
    /// frozen here and first applied by the later ownership/runtime slice.
    /// </summary>
    public static class DockerLabels
    {
        public const string Prefix = "io.message-easy-mode";
        public const string Managed = Prefix + ".managed";
        public const string ControlPlaneInstance = Prefix + ".control-plane-instance";
        public const string RuntimeMode = Prefix + ".runtime-mode";
        public const string Resource = Prefix + ".resource";
    }

    public static class Legacy
    {
        public const string FormerPublisherNamespace = "matrix-easy-mode";
        public const string ContainerName = "mem-installer";
        public const string ImageRepository = "ghcr.io/" + FormerPublisherNamespace + "/mem-installer";
        public const string StableImage = ImageRepository + ":stable";
        public const string DevelopmentImage = ImageRepository + ":dev";
        public const string VolumeName = "mem-installer-data";
        public const string CertificateFileName = "mem-installer.crt";
        public const string PrivateKeyFileName = "mem-installer.key";

        public static class EnvironmentVariables
        {
            public const string SetupToken = "MEM_INSTALLER_SETUP_TOKEN";
            public const string SetupTokenPath = "MEM_INSTALLER_SETUP_TOKEN_PATH";
            public const string Channel = "MEM_INSTALLER_CHANNEL";
            public const string PublicPort = "MEM_INSTALLER_PUBLIC_PORT";
        }
    }

    /// <summary>
    /// Persisted or authority-bearing identifiers that are intentionally frozen
    /// during the runtime rename. Changing these requires a separate migration.
    /// </summary>
    public static class Compatibility
    {
        public const string DataProtectionApplicationName = "matrix-easy-mode.control-plane";
        public const string InstallerAuthCookieName = "mem_installer_auth";
        public const string TransitionalInstallerUnlockedClaim = "mem_installer_unlocked";
        public const string TransitionalInstallerOperatorSubject = "mem-installer-operator";
    }

    public static IReadOnlyList<string> RecognizedContainerNames { get; } =
        [CanonicalContainerName, Legacy.ContainerName];

    public static bool IsRecognizedContainerName(
        string? name,
        string runtimeMode)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = name.Trim();
        if (RecognizedContainerNames.Contains(normalized, StringComparer.Ordinal))
        {
            return true;
        }

        return string.Equals(
                   runtimeMode,
                   Runtime.MemRuntimeModes.ContainerizedDevelopment,
                   StringComparison.Ordinal) &&
               (string.Equals(
                    normalized,
                    DevelopmentContainerName,
                    StringComparison.Ordinal) ||
                normalized.StartsWith(
                    DevelopmentContainerName + "-",
                    StringComparison.Ordinal));
    }

    public static IReadOnlyList<string> RecognizedPersistentVolumeNames { get; } =
        [CanonicalNewInstallVolumeName, Legacy.VolumeName];
}
