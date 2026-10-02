using HostAgent.Matrix.Runtime;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Tests.Matrix;

public sealed class SynapseRuntimeIdentityPolicyTests
{
    [Theory]
    [InlineData(MemRuntimeModes.ContainerizedProduction, 0u, 0u)]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment, 1000u, 1000u)]
    public void Containerized_control_plane_uses_approved_synapse_image_identity(
        string runtimeMode,
        uint effectiveUid,
        uint effectiveGid)
    {
        var result = SynapseRuntimeIdentityPolicy.ResolveForRuntime(
            runtimeMode,
            runningInContainer: true,
            isLinux: true,
            effectiveUid,
            effectiveGid);

        Assert.Null(result.ContainerUser);
        Assert.Equal("synapse-image-default", result.IdentitySource);
        Assert.False(result.Detail.Contains("host user", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Local_development_non_root_process_uses_effective_process_identity()
    {
        var result = SynapseRuntimeIdentityPolicy.ResolveForRuntime(
            MemRuntimeModes.LocalDevelopment,
            runningInContainer: false,
            isLinux: true,
            effectiveUid: 1000,
            effectiveGid: 1001);

        Assert.Equal("1000:1001", result.ContainerUser);
        Assert.Equal("control-plane-process", result.IdentitySource);
        Assert.True(result.Detail.Contains("effective process identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Local_root_process_does_not_propagate_root_as_synapse_container_user()
    {
        var result = SynapseRuntimeIdentityPolicy.ResolveForRuntime(
            MemRuntimeModes.LocalDevelopment,
            runningInContainer: false,
            isLinux: true,
            effectiveUid: 0,
            effectiveGid: 0);

        Assert.Null(result.ContainerUser);
        Assert.Equal("synapse-image-default", result.IdentitySource);
    }

    [Fact]
    public void Running_in_container_wins_even_when_runtime_mode_is_test_owned()
    {
        var result = SynapseRuntimeIdentityPolicy.ResolveForRuntime(
            MemRuntimeModes.AutomatedTest,
            runningInContainer: true,
            isLinux: true,
            effectiveUid: 1234,
            effectiveGid: 5678);

        Assert.Null(result.ContainerUser);
        Assert.Equal("synapse-image-default", result.IdentitySource);
    }

    [Fact]
    public void Non_linux_runtime_does_not_invent_a_unix_identity()
    {
        var result = SynapseRuntimeIdentityPolicy.ResolveForRuntime(
            MemRuntimeModes.LocalDevelopment,
            runningInContainer: false,
            isLinux: false,
            effectiveUid: 1000,
            effectiveGid: 1000);

        Assert.Null(result.ContainerUser);
        Assert.Equal("synapse-image-default", result.IdentitySource);
    }

    [Fact]
    public void Approved_synapse_default_identity_matches_restore_ownership_contract()
    {
        Assert.Equal((uint)991, SynapseRuntimeIdentityPolicy.DefaultSynapseUid);
        Assert.Equal((uint)991, SynapseRuntimeIdentityPolicy.DefaultSynapseGid);
    }
}
