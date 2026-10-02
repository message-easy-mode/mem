using System.Text.RegularExpressions;
using Modules.Setup.HostChecks;
using Modules.Setup.HostChecks.Checks;
using Modules.Setup.HostChecks.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class SetupDockerApiHostChecksTests
{
    [Fact]
    public async Task Docker_reachability_uses_the_Docker_API_probe()
    {
        var probe = FixtureProbe.Create();
        var check = new DockerReachableCheck(probe);

        var result = await check.RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Pass, result.Status);
        Assert.True(result.Blocking);
        Assert.Contains("Docker API", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Evidence, evidence => evidence.Kind == "Docker API");
        Assert.Equal(1, probe.SystemInfoCalls);
    }

    [Fact]
    public async Task Existing_resource_checks_use_Docker_API_inventory()
    {
        var probe = FixtureProbe.Create(
            containers:
            [
                new SetupDockerContainer(
                    "cp-dev",
                    "mem-control-plane-dev",
                    "mem-control-plane:local",
                    "running",
                    "Up 1 minute",
                    new Dictionary<string, string>()),
                new SetupDockerContainer(
                    "seq",
                    "mem-seq",
                    "datalust/seq:2026.1.17044",
                    "running",
                    "Up 1 minute",
                    new Dictionary<string, string>())
            ],
            volumes:
            [
                new SetupDockerVolume("mem-control-plane-dev-data", "local")
            ],
            networks:
            [
                new SetupDockerNetwork("mem-control-plane-dev", "bridge")
            ]);

        var containers = await new ExistingMemContainersCheck(probe)
            .RunAsync(CancellationToken.None);
        var volumes = await new ExistingDockerVolumesCheck(probe)
            .RunAsync(CancellationToken.None);
        var networks = await new ExistingDockerNetworksCheck(probe)
            .RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Pass, containers.Status);
        Assert.Contains("control plane runtime", containers.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("support tool", containers.Summary, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HostCheckStatus.Pass, volumes.Status);
        Assert.Contains("control plane runtime", volumes.Summary, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HostCheckStatus.Pass, networks.Status);
        Assert.Contains("development compose", networks.Summary, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(1, probe.ContainerCalls);
        Assert.Equal(1, probe.VolumeCalls);
        Assert.Equal(1, probe.NetworkCalls);
    }

    [Fact]
    public async Task Docker_data_root_comes_from_daemon_info_while_cli_only_checks_are_skipped()
    {
        var probe = FixtureProbe.Create();

        var dataRoot = await new DockerDataRootCheck(probe)
            .RunAsync(CancellationToken.None);
        var diskUsage = await new DockerDiskUsageCheck(probe)
            .RunAsync(CancellationToken.None);
        var compose = await new DockerComposePluginCheck()
            .RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Pass, dataRoot.Status);
        Assert.Contains("/var/lib/docker", dataRoot.Summary, StringComparison.Ordinal);

        Assert.Equal(HostCheckStatus.Skipped, diskUsage.Status);
        Assert.Contains("Byte-level", diskUsage.Summary, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HostCheckStatus.Skipped, compose.Status);
        Assert.Contains("not required", compose.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Setup_start_and_host_Docker_checks_do_not_shell_out_to_the_Docker_CLI()
    {
        var repositoryRoot = FindRepositoryRoot();
        var roots = new[]
        {
            Path.Combine(
                repositoryRoot,
                "installer",
                "src",
                "Modules",
                "Modules",
                "Setup",
                "Start"),
            Path.Combine(
                repositoryRoot,
                "installer",
                "src",
                "Modules",
                "Modules",
                "Setup",
                "HostChecks")
        };

        var violations = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*.cs",
                SearchOption.AllDirectories))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return Regex.IsMatch(
                    source,
                    "RunAsync\\s*\\(\\s*\\\"docker\\\"",
                    RegexOptions.CultureInvariant);
            })
            .Select(file => Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private sealed class FixtureProbe(
        SetupDockerSystemInfo systemInfo,
        IReadOnlyList<SetupDockerContainer> containers,
        IReadOnlyList<SetupDockerVolume> volumes,
        IReadOnlyList<SetupDockerNetwork> networks)
        : ISetupDockerRuntimeProbe
    {
        public int SystemInfoCalls { get; private set; }
        public int ContainerCalls { get; private set; }
        public int VolumeCalls { get; private set; }
        public int NetworkCalls { get; private set; }

        public static FixtureProbe Create(
            IReadOnlyList<SetupDockerContainer>? containers = null,
            IReadOnlyList<SetupDockerVolume>? volumes = null,
            IReadOnlyList<SetupDockerNetwork>? networks = null) =>
            new(
                new SetupDockerSystemInfo(
                    ServerVersion: "28.0.2",
                    DockerRootDir: "/var/lib/docker",
                    OperatingSystem: "Ubuntu 24.04 LTS",
                    Architecture: "x86_64",
                    MemoryBytes: 8L * 1024 * 1024 * 1024,
                    CpuCount: 4,
                    ContainerCount: containers?.Count ?? 0,
                    ImageCount: 12),
                containers ?? [],
                volumes ?? [],
                networks ?? []);

        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemInfoCalls++;
            return Task.FromResult(systemInfo);
        }

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContainerCalls++;
            return Task.FromResult(containers);
        }

        public Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            VolumeCalls++;
            return Task.FromResult(volumes);
        }

        public Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            NetworkCalls++;
            return Task.FromResult(networks);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "installer", "src", "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }
}
