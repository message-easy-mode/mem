using Shared.ControlPlane;

namespace Api.IntegrationTests.Runtime;

public sealed class MemControlPlaneIdentityTests
{
    [Fact]
    public void Canonical_product_and_runtime_identity_is_frozen()
    {
        Assert.Equal("Message Easy Mode", MemControlPlaneIdentity.ProductName);
        Assert.Equal("MEM", MemControlPlaneIdentity.ProductShortName);
        Assert.Equal("MEM Control Plane", MemControlPlaneIdentity.ProductDisplayName);
        Assert.Equal("message-easy-mode", MemControlPlaneIdentity.CanonicalPublisherNamespace);
        Assert.Equal("mem-control-plane", MemControlPlaneIdentity.CanonicalContainerName);
        Assert.Equal("mem-control-plane-dev", MemControlPlaneIdentity.DevelopmentContainerName);
        Assert.Equal("mem-control-plane-dev-data", MemControlPlaneIdentity.DevelopmentVolumeName);
        Assert.Equal(
            "ghcr.io/message-easy-mode",
            MemControlPlaneIdentity.CanonicalRegistryPublisher);
        Assert.Equal(
            "ghcr.io/message-easy-mode/mem-control-plane",
            MemControlPlaneIdentity.CanonicalImageRepository);
        Assert.Equal(
            "ghcr.io/message-easy-mode/mem-control-plane:stable",
            MemControlPlaneIdentity.CanonicalStableImage);
        Assert.Equal(
            "ghcr.io/message-easy-mode/mem-control-plane:dev",
            MemControlPlaneIdentity.CanonicalDevelopmentImage);
        Assert.Equal("mem-control-plane:local", MemControlPlaneIdentity.CanonicalLocalImage);
        Assert.Equal("mem-control-plane-data", MemControlPlaneIdentity.CanonicalNewInstallVolumeName);
        Assert.Equal("mem-control-plane.crt", MemControlPlaneIdentity.CanonicalCertificateFileName);
        Assert.Equal("mem-control-plane.key", MemControlPlaneIdentity.CanonicalPrivateKeyFileName);
        Assert.Equal("MEM Control Plane", MemControlPlaneIdentity.CanonicalCertificateSubjectCommonName);
    }

    [Fact]
    public void Canonical_and_legacy_environment_names_are_explicit()
    {
        Assert.Equal(
            "MEM_CONTROL_PLANE_SETUP_TOKEN",
            MemControlPlaneIdentity.EnvironmentVariables.SetupToken);
        Assert.Equal(
            "MEM_CONTROL_PLANE_SETUP_TOKEN_PATH",
            MemControlPlaneIdentity.EnvironmentVariables.SetupTokenPath);
        Assert.Equal(
            "MEM_CONTROL_PLANE_CHANNEL",
            MemControlPlaneIdentity.EnvironmentVariables.Channel);
        Assert.Equal(
            "MEM_CONTROL_PLANE_PUBLIC_PORT",
            MemControlPlaneIdentity.EnvironmentVariables.PublicPort);
        Assert.Equal(
            "MEM_CONTROL_PLANE_INSTANCE_ID",
            MemControlPlaneIdentity.EnvironmentVariables.InstanceId);
        Assert.Equal("MEM_RUNTIME_MODE", MemControlPlaneIdentity.EnvironmentVariables.RuntimeMode);
        Assert.Equal("MEM_STATE_ROOT", MemControlPlaneIdentity.EnvironmentVariables.StateRoot);
        Assert.Equal(
            "MEM_PRODUCT_VERSION",
            MemControlPlaneIdentity.EnvironmentVariables.ProductVersion);
        Assert.Equal(
            "MEM_COMMIT_SHA",
            MemControlPlaneIdentity.EnvironmentVariables.CommitSha);

        Assert.Equal(
            "MEM_INSTALLER_SETUP_TOKEN",
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupToken);
        Assert.Equal(
            "MEM_INSTALLER_SETUP_TOKEN_PATH",
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupTokenPath);
        Assert.Equal(
            "MEM_INSTALLER_CHANNEL",
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.Channel);
        Assert.Equal(
            "MEM_INSTALLER_PUBLIC_PORT",
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.PublicPort);
    }

    [Fact]
    public void Canonical_resource_label_namespace_uses_message_easy_mode()
    {
        Assert.Equal("io.message-easy-mode", MemControlPlaneIdentity.DockerLabels.Prefix);
        Assert.Equal("io.message-easy-mode.managed", MemControlPlaneIdentity.DockerLabels.Managed);
        Assert.Equal(
            "io.message-easy-mode.control-plane-instance",
            MemControlPlaneIdentity.DockerLabels.ControlPlaneInstance);
        Assert.Equal(
            "io.message-easy-mode.runtime-mode",
            MemControlPlaneIdentity.DockerLabels.RuntimeMode);
        Assert.Equal("io.message-easy-mode.resource", MemControlPlaneIdentity.DockerLabels.Resource);
    }

    [Fact]
    public void Legacy_runtime_aliases_remain_recognized_without_becoming_canonical()
    {
        Assert.Equal("matrix-easy-mode", MemControlPlaneIdentity.Legacy.FormerPublisherNamespace);
        Assert.Equal("mem-installer", MemControlPlaneIdentity.Legacy.ContainerName);
        Assert.Equal("mem-installer-data", MemControlPlaneIdentity.Legacy.VolumeName);
        Assert.Equal(
            "ghcr.io/matrix-easy-mode/mem-installer",
            MemControlPlaneIdentity.Legacy.ImageRepository);

        Assert.Equal(
            new[] { "mem-control-plane", "mem-installer" },
            MemControlPlaneIdentity.RecognizedContainerNames);
        Assert.Equal(
            new[] { "mem-control-plane-data", "mem-installer-data" },
            MemControlPlaneIdentity.RecognizedPersistentVolumeNames);
    }

    [Fact]
    public void Development_container_identity_is_bounded_to_containerized_development()
    {
        Assert.True(MemControlPlaneIdentity.IsRecognizedContainerName(
            MemControlPlaneIdentity.DevelopmentContainerName,
            Shared.ControlPlane.Runtime.MemRuntimeModes.ContainerizedDevelopment));
        Assert.True(MemControlPlaneIdentity.IsRecognizedContainerName(
            "mem-control-plane-dev-e2e",
            Shared.ControlPlane.Runtime.MemRuntimeModes.ContainerizedDevelopment));
        Assert.False(MemControlPlaneIdentity.IsRecognizedContainerName(
            MemControlPlaneIdentity.DevelopmentContainerName,
            Shared.ControlPlane.Runtime.MemRuntimeModes.ContainerizedProduction));
        Assert.False(MemControlPlaneIdentity.IsRecognizedContainerName(
            "mem-control-plane-unknown",
            Shared.ControlPlane.Runtime.MemRuntimeModes.ContainerizedDevelopment));
    }

    [Fact]
    public void Authority_bearing_compatibility_names_are_frozen()
    {
        Assert.Equal(
            "matrix-easy-mode.control-plane",
            MemControlPlaneIdentity.Compatibility.DataProtectionApplicationName);
        Assert.Equal(
            "mem_installer_auth",
            MemControlPlaneIdentity.Compatibility.InstallerAuthCookieName);
        Assert.Equal(
            "mem_installer_unlocked",
            MemControlPlaneIdentity.Compatibility.TransitionalInstallerUnlockedClaim);
        Assert.Equal(
            "mem-installer-operator",
            MemControlPlaneIdentity.Compatibility.TransitionalInstallerOperatorSubject);
    }
}
