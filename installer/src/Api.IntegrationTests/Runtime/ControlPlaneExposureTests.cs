using Infrastructure.Docker.Models;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

public sealed class ControlPlaneExposureTests
{
    [Fact]
    public async Task Local_source_runtime_does_not_invent_a_Docker_host_binding()
    {
        using var root = new TemporaryDirectory();
        var service = new ControlPlaneExposureService(
            new FailingInspector(),
            TestRuntimeContext.Create(
                root.Path,
                mode: MemRuntimeModes.LocalDevelopment,
                uiDeliveryMode: MemUiDeliveryModes.Vite));

        var projection = await service.InspectAsync(CancellationToken.None);

        Assert.Equal(MemControlPlaneExposureStates.NotApplicable, projection.State);
        Assert.Equal(MemControlPlaneAccessModes.LocalDevelopment, projection.AccessMode);
        Assert.Null(projection.HostAddress);
        Assert.Null(projection.WarningCode);
    }

    [Fact]
    public async Task Loopback_only_publication_is_private_SSH_administration()
    {
        using var root = new TemporaryDirectory();
        var projection = await Service(root.Path, Binding("127.0.0.1", 8443))
            .InspectAsync(CancellationToken.None);

        Assert.Equal(MemControlPlaneExposureStates.Private, projection.State);
        Assert.Equal(MemControlPlaneAccessModes.SshTunnel, projection.AccessMode);
        Assert.True(projection.IsPrivate);
        Assert.Equal("127.0.0.1", projection.HostAddress);
        Assert.Equal(8443, projection.HostPort);
        Assert.Null(projection.WarningCode);
    }

    [Theory]
    [InlineData("10.10.0.193")]
    [InlineData("172.16.4.20")]
    [InlineData("172.31.255.250")]
    [InlineData("192.168.10.20")]
    public async Task Rfc1918_publication_is_private_Trusted_LAN_administration(string address)
    {
        using var root = new TemporaryDirectory();
        var projection = await Service(root.Path, Binding(address, 9443))
            .InspectAsync(CancellationToken.None);

        Assert.Equal(MemControlPlaneExposureStates.Private, projection.State);
        Assert.Equal(MemControlPlaneAccessModes.TrustedLan, projection.AccessMode);
        Assert.Equal(address, projection.HostAddress);
        Assert.Equal(9443, projection.HostPort);
        Assert.Null(projection.WarningCode);
    }

    [Theory]
    [InlineData("0.0.0.0", "control_plane_exposure_wildcard_binding")]
    [InlineData("", "control_plane_exposure_wildcard_binding")]
    [InlineData("::", "control_plane_exposure_wildcard_binding")]
    [InlineData("::1", "control_plane_exposure_noncanonical_loopback")]
    [InlineData("8.8.8.8", "control_plane_exposure_public_or_nonprivate_binding")]
    [InlineData("172.15.0.10", "control_plane_exposure_public_or_nonprivate_binding")]
    [InlineData("192.0.2.20", "control_plane_exposure_public_or_nonprivate_binding")]
    public async Task Unsupported_host_publication_requires_attention(
        string address,
        string warningCode)
    {
        using var root = new TemporaryDirectory();
        var projection = await Service(root.Path, Binding(address, 8443))
            .InspectAsync(CancellationToken.None);

        Assert.Equal(MemControlPlaneExposureStates.NeedsAttention, projection.State);
        Assert.Equal(MemControlPlaneAccessModes.Unsupported, projection.AccessMode);
        Assert.False(projection.IsPrivate);
        Assert.Equal(warningCode, projection.WarningCode);
    }

    [Fact]
    public async Task Multiple_HTTPS_host_bindings_are_ambiguous_and_require_attention()
    {
        using var root = new TemporaryDirectory();
        var projection = await Service(
                root.Path,
                Binding("127.0.0.1", 8443),
                Binding("192.168.10.20", 8443))
            .InspectAsync(CancellationToken.None);

        Assert.Equal(MemControlPlaneExposureStates.NeedsAttention, projection.State);
        Assert.Equal(MemControlPlaneAccessModes.Unsupported, projection.AccessMode);
        Assert.Equal(2, projection.BindingCount);
        Assert.Null(projection.HostAddress);
        Assert.Equal("control_plane_exposure_multiple_bindings", projection.WarningCode);
    }

    [Fact]
    public async Task Docker_inspection_failure_does_not_claim_the_Control_Plane_is_private()
    {
        using var root = new TemporaryDirectory();
        var service = new ControlPlaneExposureService(
            new FailingInspector(),
            ContainerContext(root.Path));

        var projection = await service.InspectAsync(CancellationToken.None);

        Assert.Equal(MemControlPlaneExposureStates.Unavailable, projection.State);
        Assert.Equal(MemControlPlaneAccessModes.Unknown, projection.AccessMode);
        Assert.False(projection.IsPrivate);
        Assert.Equal("control_plane_exposure_inspection_failed", projection.WarningCode);
    }

    private static ControlPlaneExposureService Service(
        string root,
        params DockerPortBinding[] ports) =>
        new(
            new FakeInspector(Container(ports)),
            ContainerContext(root));

    private static MemControlPlaneRuntimeContext ContainerContext(string root) =>
        TestRuntimeContext.Create(
            root,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);

    private static DockerPortBinding Binding(string ip, uint publicPort) =>
        new(
            PrivatePort: 8443,
            PublicPort: publicPort,
            Type: "tcp",
            Ip: ip);

    private static DockerContainerInspection Container(params DockerPortBinding[] ports) =>
        new(
            Id: "container-1",
            Name: "mem-control-plane",
            Image: "mem-control-plane:test",
            State: "running",
            Running: true,
            Ports: ports);

    private sealed class FakeInspector(DockerContainerInspection? container)
        : IControlPlaneContainerInspector
    {
        public Task<DockerContainerInspection?> InspectAsync(
            string containerName,
            CancellationToken cancellationToken) =>
            Task.FromResult(container);
    }

    private sealed class FailingInspector : IControlPlaneContainerInspector
    {
        public Task<DockerContainerInspection?> InspectAsync(
            string containerName,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Docker unavailable");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mem-control-plane-exposure-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
