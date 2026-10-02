using Modules.Setup.HostChecks;
using Modules.Setup.HostChecks.Checks;
using Modules.Setup.HostChecks.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class ControlPlaneRuntimeNamingContractTests
{
    [Fact]
    public async Task Existing_container_check_recognises_canonical_and_legacy_control_plane_names()
    {
        var check = new ExistingMemContainersCheck(
            new StaticDockerProbe(
                containers:
                [
                    DockerContainer(
                        "mem-control-plane",
                        "mem-control-plane:local",
                        "running"),
                    DockerContainer(
                        "mem-installer",
                        "legacy",
                        "exited")
                ]));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Pass, result.Status);
        Assert.Contains("2 control plane runtime", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mem-control-plane", result.Details);
        Assert.Contains("mem-installer", result.Details);
        Assert.DoesNotContain("Installer runtime", result.Details);
    }

    [Fact]
    public async Task Existing_volume_check_recognises_canonical_and_legacy_control_plane_volumes()
    {
        var check = new ExistingDockerVolumesCheck(
            new StaticDockerProbe(
                volumes:
                [
                    new SetupDockerVolume("mem-control-plane-data", "local"),
                    new SetupDockerVolume("mem-installer-data", "local")
                ]));

        var result = await check.RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Pass, result.Status);
        Assert.Contains("2 control plane runtime", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mem-control-plane-data", result.Details);
        Assert.Contains("mem-installer-data", result.Details);
    }

    private static SetupDockerContainer DockerContainer(
        string name,
        string image,
        string state) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Image: image,
            State: state,
            Status: state == "running" ? "Up 1 minute" : "Exited (0)",
            Labels: new Dictionary<string, string>());

    private sealed class StaticDockerProbe(
        IReadOnlyList<SetupDockerContainer>? containers = null,
        IReadOnlyList<SetupDockerVolume>? volumes = null)
        : ISetupDockerRuntimeProbe
    {
        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SetupDockerSystemInfo(
                ServerVersion: "28.0.2",
                DockerRootDir: "/var/lib/docker",
                OperatingSystem: "Ubuntu",
                Architecture: "x86_64",
                MemoryBytes: 8L * 1024 * 1024 * 1024,
                CpuCount: 4,
                ContainerCount: containers?.Count ?? 0,
                ImageCount: 1));
        }

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SetupDockerContainer>>(
                containers ?? []);
        }

        public Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SetupDockerVolume>>(
                volumes ?? []);
        }

        public Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SetupDockerNetwork>>([]);
        }
    }
}
