using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks;
using Modules.Setup.HostChecks.Checks;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallRuns;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class RuntimeAwareSetupHostChecksTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01C_host_capacity_uses_Docker_daemon_evidence_not_container_tools()
    {
        var probe = FixtureProbe.Create();

        var results = new[]
        {
            await new UbuntuVersionCheck(probe).RunAsync(CancellationToken.None),
            await new ArchitectureCheck(probe).RunAsync(CancellationToken.None),
            await new CpuCheck(probe).RunAsync(CancellationToken.None),
            await new MemoryCheck(probe).RunAsync(CancellationToken.None)
        };

        Assert.All(results, result => Assert.Equal(HostCheckStatus.Pass, result.Status));
        Assert.All(
            results,
            result => Assert.Contains(
                result.Evidence,
                evidence => evidence.Kind == "Docker API"));
        Assert.DoesNotContain(
            results.SelectMany(result => result.Evidence),
            evidence => evidence.Kind == "Command");
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01C_container_runtime_does_not_present_container_df_or_ss_as_host_evidence()
    {
        var context = RuntimeContext(MemRuntimeModes.ContainerizedDevelopment);
        var probe = FixtureProbe.Create(
            containers:
            [
                new SetupDockerContainer(
                    "cp",
                    "mem-control-plane-dev",
                    "mem-control-plane:local",
                    "running",
                    "Up",
                    new Dictionary<string, string>
                    {
                        [MemDockerOwnershipLabels.ControlPlaneInstanceKey] = context.ControlPlaneInstanceId.ToString("D")
                    },
                    [new SetupDockerPortBinding(8443, 8443, "tcp", "127.0.0.1")])
            ]);

        var disk = await new DiskSpaceCheck(probe, context)
            .RunAsync(CancellationToken.None);
        var ports = await new PortAvailabilityCheck(probe, context)
            .RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Unavailable, disk.Status);
        Assert.Contains("container", disk.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HostCheckStatus.Unavailable, ports.Status);
        Assert.Contains("Non-Docker", ports.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ss", ports.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01C_Docker_published_port_conflict_is_visible_without_host_ss()
    {
        var context = RuntimeContext(MemRuntimeModes.ContainerizedDevelopment);
        var probe = FixtureProbe.Create(
            containers:
            [
                new SetupDockerContainer(
                    "other",
                    "other-proxy",
                    "nginx:latest",
                    "running",
                    "Up",
                    new Dictionary<string, string>(),
                    [new SetupDockerPortBinding(80, 80, "tcp", "0.0.0.0")])
            ]);

        var result = await new PortAvailabilityCheck(probe, context)
            .RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Warning, result.Status);
        Assert.Contains("80", result.Summary, StringComparison.Ordinal);
        Assert.Contains("other-proxy", result.Details, StringComparison.Ordinal);
        Assert.Contains(result.Evidence, evidence => evidence.Kind == "Docker API");
    }

    [Fact]
    public async Task Shared_platform_TURN_UDP_conflicts_are_visible_in_Docker_preflight()
    {
        var context = RuntimeContext(MemRuntimeModes.ContainerizedDevelopment);
        var probe = FixtureProbe.Create(
            containers:
            [
                new SetupDockerContainer(
                    "other-turn",
                    "other-turn",
                    "coturn:other",
                    "running",
                    "Up",
                    new Dictionary<string, string>(),
                    [
                        new SetupDockerPortBinding(3478, 3478, "udp", "0.0.0.0"),
                        new SetupDockerPortBinding(49160, 49160, "udp", "0.0.0.0")
                    ])
            ]);

        var result = await new PortAvailabilityCheck(probe, context)
            .RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Warning, result.Status);
        Assert.Contains("3478/udp", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("49160/udp", result.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("other-turn", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_MEM_owned_shared_Coturn_bindings_are_reusable_not_reported_as_conflicts()
    {
        var context = RuntimeContext(MemRuntimeModes.ContainerizedDevelopment);
        var probe = FixtureProbe.Create(
            containers:
            [
                new SetupDockerContainer(
                    "coturn",
                    "mem-coturn",
                    "sha256:approved",
                    "running",
                    "Up",
                    new Dictionary<string, string>
                    {
                        ["mem.component"] = "platform",
                        ["mem.service"] = "coturn"
                    },
                    [
                        new SetupDockerPortBinding(3478, 3478, "tcp", "0.0.0.0"),
                        new SetupDockerPortBinding(3478, 3478, "udp", "0.0.0.0"),
                        new SetupDockerPortBinding(49160, 49160, "udp", "0.0.0.0")
                    ])
            ]);

        var result = await new PortAvailabilityCheck(probe, context)
            .RunAsync(CancellationToken.None);

        Assert.Equal(HostCheckStatus.Unavailable, result.Status);
        Assert.DoesNotContain("mem-coturn", result.Details ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MemRuntimeModes.LocalDevelopment, "127.0.0.1", 8181, MemManagedServiceRouteKinds.HostPublishedLoopback)]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment, "mem-npm", 81, MemManagedServiceRouteKinds.DockerNetwork)]
    public async Task STARTUP_INSTALL_REL_01C_NPM_admin_probe_uses_runtime_managed_service_authority(
        string runtimeMode,
        string expectedHost,
        int expectedPort,
        string expectedRouteKind)
    {
        var context = RuntimeContext(runtimeMode);
        var handler = new RecordingHandler();
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var resolver = new MemManagedServiceAuthorityResolver(context);
        var probe = new InstallNpmAdminProbe(client, resolver);

        var result = await probe.ProbeAsync(
            "mem-npm",
            publishedAdminPort: 8181,
            CancellationToken.None);

        Assert.True(result.Reachable);
        Assert.Equal(expectedRouteKind, result.RouteKind);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Equal(expectedHost, handler.LastRequestUri!.Host);
        Assert.Equal(expectedPort, handler.LastRequestUri.Port);
    }

    private static MemControlPlaneRuntimeContext RuntimeContext(string runtimeMode) =>
        new(
            SchemaVersion: 1,
            RuntimeMode: runtimeMode,
            ControlPlaneInstanceId: Guid.NewGuid(),
            ApiProcessInstanceId: Guid.NewGuid(),
            EnvironmentName: "Development",
            RunningInContainer: MemRuntimeModes.IsContainerized(runtimeMode),
            ContentRootPath: "/app",
            ContentRootKind: "test",
            StateRootPath: "/data",
            StateRootKind: "test",
            StateRootProfile: "test",
            UiDeliveryMode: MemRuntimeModes.IsContainerized(runtimeMode)
                ? MemUiDeliveryModes.EmbeddedSpa
                : MemUiDeliveryModes.Vite,
            DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
            DockerEndpointKind: "unix-socket",
            ConfiguredContainerName: runtimeMode == MemRuntimeModes.ContainerizedDevelopment
                ? "mem-control-plane-dev"
                : null,
            ApplicationName: "mem-control-plane",
            Version: "0.2.0",
            Commit: "test",
            ValidationState: "valid",
            MutationsAllowed: true,
            ShowDevelopmentBanner: true,
            Warnings: []);

    private sealed class FixtureProbe(
        SetupDockerSystemInfo info,
        IReadOnlyList<SetupDockerContainer> containers) : ISetupDockerRuntimeProbe
    {
        public static FixtureProbe Create(
            IReadOnlyList<SetupDockerContainer>? containers = null) =>
            new(
                new SetupDockerSystemInfo(
                    ServerVersion: "29.4.2",
                    DockerRootDir: "/var/lib/docker",
                    OperatingSystem: "Ubuntu 24.04.3 LTS",
                    Architecture: "x86_64",
                    MemoryBytes: 8L * 1024 * 1024 * 1024,
                    CpuCount: 8,
                    ContainerCount: containers?.Count ?? 0,
                    ImageCount: 10),
                containers ?? []);

        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(info);

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(containers);

        public Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerVolume>>([]);

        public Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerNetwork>>([]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
