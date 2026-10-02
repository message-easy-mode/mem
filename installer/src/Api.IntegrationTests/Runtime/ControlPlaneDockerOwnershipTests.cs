using Infrastructure.Docker.Models;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;
using Shared.Exceptions;

namespace Api.IntegrationTests.Runtime;

public sealed class ControlPlaneDockerOwnershipTests
{
    [Fact]
    public async Task Current_control_plane_is_exclusive_when_no_competitor_is_running()
    {
        using var root = new TemporaryDirectory();
        var context = ContainerContext(root.Path);
        var service = new ControlPlaneDockerOwnershipService(
            new FakeInventory(Container("mem-control-plane", "running")),
            context);

        var projection = await service.InspectAsync(CancellationToken.None);

        Assert.Equal("exclusive", projection.State);
        Assert.True(projection.MutationsAllowed);
        Assert.Empty(projection.CompetingContainers);
    }

    [Fact]
    public async Task Running_legacy_control_plane_blocks_mutation()
    {
        using var root = new TemporaryDirectory();
        var context = ContainerContext(root.Path);
        var service = new ControlPlaneDockerOwnershipService(
            new FakeInventory(
                Container("mem-control-plane", "running"),
                Container("mem-installer", "running")),
            context);

        var projection = await service.InspectAsync(CancellationToken.None);

        Assert.Equal("conflict", projection.State);
        Assert.False(projection.MutationsAllowed);
        Assert.Equal(["mem-installer"], projection.CompetingContainers);
        var error = await Assert.ThrowsAsync<MemProblemException>(() =>
            service.EnsureMutationAllowedAsync(CancellationToken.None));
        Assert.Equal("control_plane_competing_controller_detected", error.Code);
    }

    [Fact]
    public async Task Explicit_shared_host_override_is_development_only()
    {
        using var devRoot = new TemporaryDirectory();
        var development = TestRuntimeContext.Create(
            devRoot.Path,
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite,
            allowSharedDockerHost: true);
        var devService = new ControlPlaneDockerOwnershipService(
            new FakeInventory(Container("mem-control-plane", "running")),
            development);

        var devProjection = await devService.InspectAsync(CancellationToken.None);
        Assert.Equal("shared-development-override", devProjection.State);
        Assert.True(devProjection.MutationsAllowed);
        Assert.True(devProjection.DevelopmentOverrideActive);

        using var productionRoot = new TemporaryDirectory();
        var error = Assert.Throws<MemRuntimeContextValidationException>(() =>
            TestRuntimeContext.Create(
                productionRoot.Path,
                mode: MemRuntimeModes.ContainerizedProduction,
                runningInContainer: true,
                containerName: "mem-control-plane",
                uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
                allowSharedDockerHost: true));
        Assert.Equal("runtime_shared_docker_host_override_forbidden", error.Code);
    }

    [Fact]
    public async Task Inventory_failure_fails_closed()
    {
        using var root = new TemporaryDirectory();
        var service = new ControlPlaneDockerOwnershipService(
            new FailingInventory(),
            ContainerContext(root.Path));

        var projection = await service.InspectAsync(CancellationToken.None);

        Assert.Equal("unavailable", projection.State);
        Assert.False(projection.MutationsAllowed);
        Assert.Equal("control_plane_docker_ownership_unavailable", projection.WarningCode);
    }

    private static MemControlPlaneRuntimeContext ContainerContext(string root) =>
        TestRuntimeContext.Create(
            root,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);

    private static DockerContainerSummary Container(string name, string state) => new(
        Id: Guid.NewGuid().ToString("N"),
        Name: name,
        Image: "mem-control-plane:test",
        State: state,
        Status: state,
        Ports: []);

    private sealed class FakeInventory : IControlPlaneContainerInventory
    {
        private readonly DockerContainerSummary[] _containers;

        public FakeInventory(params DockerContainerSummary[] containers)
        {
            _containers = containers;
        }

        public Task<IReadOnlyList<DockerContainerSummary>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>(_containers);
    }

    private sealed class FailingInventory : IControlPlaneContainerInventory
    {
        public Task<IReadOnlyList<DockerContainerSummary>> ListAsync(
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Docker unavailable");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mem-docker-ownership-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
