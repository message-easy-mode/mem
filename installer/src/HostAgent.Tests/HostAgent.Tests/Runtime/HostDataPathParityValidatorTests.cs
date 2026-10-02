using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Options;
using HostAgent.Runtime.Filesystem;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Tests.Runtime;

public sealed class HostDataPathParityValidatorTests
{
    [Fact]
    public async Task Local_development_does_not_require_container_mount_parity()
    {
        var docker = new RecordingDockerHost();
        var validator = CreateValidator(
            docker,
            CreateRuntime(MemRuntimeModes.LocalDevelopment, runningInContainer: false, containerName: null),
            memDataRoot: "relative-dev-data",
            instanceRoot: "relative-dev-instances",
            coturnRoot: "relative-dev-coturn",
            seqRoot: "relative-dev-seq");

        await validator.ValidateConfiguredRootsAsync(CancellationToken.None);

        Assert.Equal(0, docker.InspectCount);
    }

    [Fact]
    public async Task Containerized_production_accepts_identity_mounted_canonical_host_data_root()
    {
        const string root = "/var/lib/message-easy-mode";
        var docker = new RecordingDockerHost
        {
            Inspection = Inspection(
                new DockerBindMountInspection(
                    root,
                    root,
                    ReadOnly: false),
                new DockerBindMountInspection(
                    "/var/lib/docker/volumes/mem-control-plane-data/_data",
                    "/data",
                    ReadOnly: false))
        };
        var validator = CreateValidator(
            docker,
            CreateRuntime(MemRuntimeModes.ContainerizedProduction, runningInContainer: true, containerName: "mem-control-plane"),
            memDataRoot: root + "/mem-data",
            instanceRoot: root + "/instances",
            coturnRoot: root + "/platform/coturn",
            seqRoot: root + "/seq");

        await validator.ValidateConfiguredRootsAsync(CancellationToken.None);

        Assert.Equal(1, docker.InspectCount);
        Assert.Equal("mem-control-plane", docker.LastInspectedContainer);
    }

    [Fact]
    public async Task Containerized_development_accepts_separate_identity_mounted_mem_and_host_roots()
    {
        const string memDataRoot = "/workspace/dev/.state/interactive/mem-data";
        const string hostDataRoot = "/workspace/dev/.state/container/host-data";
        var docker = new RecordingDockerHost
        {
            Inspection = Inspection(
                new DockerBindMountInspection(
                    memDataRoot,
                    memDataRoot,
                    ReadOnly: false),
                new DockerBindMountInspection(
                    hostDataRoot,
                    hostDataRoot,
                    ReadOnly: false),
                new DockerBindMountInspection(
                    "/workspace/dev/.state/interactive/data",
                    "/data",
                    ReadOnly: false))
        };
        var validator = CreateValidator(
            docker,
            CreateRuntime(MemRuntimeModes.ContainerizedDevelopment, runningInContainer: true, containerName: "mem-control-plane-dev"),
            memDataRoot: memDataRoot,
            instanceRoot: hostDataRoot + "/instances",
            coturnRoot: hostDataRoot + "/platform/coturn",
            seqRoot: hostDataRoot + "/seq");

        await validator.ValidateConfiguredRootsAsync(CancellationToken.None);

        Assert.Equal(1, docker.InspectCount);
        Assert.Equal("mem-control-plane-dev", docker.LastInspectedContainer);
    }

    [Fact]
    public void Rc4_style_unmounted_developer_home_instance_root_is_rejected_by_mount_contract()
    {
        var mounts = new[]
        {
            new DockerBindMountInspection(
                "/var/lib/message-easy-mode",
                "/var/lib/message-easy-mode",
                ReadOnly: false),
            new DockerBindMountInspection(
                "/var/lib/docker/volumes/mem-control-plane-data/_data",
                "/data",
                ReadOnly: false)
        };

        var exception = Assert.Throws<HostDataPathParityException>(() =>
            HostDataPathParityValidator.ValidatePathAgainstMounts(
                "instances",
                "/workspace/mem-data/instances",
                mounts,
                requireWritable: true));

        Assert.Equal("host_data_path_parity.mount_missing", exception.Code);
        Assert.Equal("instances", exception.LogicalRoot);
    }

    [Fact]
    public async Task Container_path_mapped_to_different_host_path_is_rejected()
    {
        var docker = new RecordingDockerHost
        {
            Inspection = Inspection(
                new DockerBindMountInspection(
                    "/srv/mem-host-data",
                    "/var/lib/message-easy-mode",
                    ReadOnly: false))
        };
        var validator = CreateValidator(
            docker,
            CreateRuntime(MemRuntimeModes.ContainerizedProduction, runningInContainer: true, containerName: "mem-control-plane"),
            memDataRoot: "/var/lib/message-easy-mode/mem-data",
            instanceRoot: "/var/lib/message-easy-mode/instances",
            coturnRoot: "/var/lib/message-easy-mode/platform/coturn",
            seqRoot: "/var/lib/message-easy-mode/seq");

        var exception = await Assert.ThrowsAsync<HostDataPathParityException>(() =>
            validator.ValidateConfiguredRootsAsync(CancellationToken.None));

        Assert.Equal("host_data_path_parity.mapping_mismatch", exception.Code);
        Assert.Equal("mem-data", exception.LogicalRoot);
    }

    [Fact]
    public async Task Read_only_identity_mount_is_rejected_for_mutable_host_data()
    {
        const string root = "/var/lib/message-easy-mode";
        var docker = new RecordingDockerHost
        {
            Inspection = Inspection(
                new DockerBindMountInspection(
                    root,
                    root,
                    ReadOnly: true))
        };
        var validator = CreateValidator(
            docker,
            CreateRuntime(MemRuntimeModes.ContainerizedProduction, runningInContainer: true, containerName: "mem-control-plane"),
            memDataRoot: root + "/mem-data",
            instanceRoot: root + "/instances",
            coturnRoot: root + "/platform/coturn",
            seqRoot: root + "/seq");

        var exception = await Assert.ThrowsAsync<HostDataPathParityException>(() =>
            validator.ValidateConfiguredRootsAsync(CancellationToken.None));

        Assert.Equal("host_data_path_parity.mount_read_only", exception.Code);
    }

    [Fact]
    public async Task Containerized_runtime_requires_declared_control_plane_container_identity()
    {
        var docker = new RecordingDockerHost();
        var validator = CreateValidator(
            docker,
            CreateRuntime(MemRuntimeModes.ContainerizedProduction, runningInContainer: true, containerName: null),
            memDataRoot: "/var/lib/message-easy-mode/mem-data",
            instanceRoot: "/var/lib/message-easy-mode/instances",
            coturnRoot: "/var/lib/message-easy-mode/platform/coturn",
            seqRoot: "/var/lib/message-easy-mode/seq");

        var exception = await Assert.ThrowsAsync<HostDataPathParityException>(() =>
            validator.ValidateConfiguredRootsAsync(CancellationToken.None));

        Assert.Equal("host_data_path_parity.container_identity_missing", exception.Code);
        Assert.Equal(0, docker.InspectCount);
    }

    private static HostDataPathParityValidator CreateValidator(
        IDockerHost docker,
        MemControlPlaneRuntimeContext runtimeContext,
        string memDataRoot,
        string instanceRoot,
        string coturnRoot,
        string seqRoot)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MEM_RUNTIME_MODE"] = runtimeContext.RuntimeMode,
                ["MEM_DATA_ROOT"] = memDataRoot,
                ["Coturn:StorageRootPath"] = coturnRoot,
                ["Diagnostics:Seq:HostDataPath"] = seqRoot
            })
            .Build();

        return new HostDataPathParityValidator(
            docker,
            runtimeContext,
            configuration,
            Microsoft.Extensions.Options.Options.Create(new InstanceStorageOptions
            {
                InstanceDataRoot = instanceRoot
            }),
            NullLogger<HostDataPathParityValidator>.Instance);
    }

    private static MemControlPlaneRuntimeContext CreateRuntime(
        string mode,
        bool runningInContainer,
        string? containerName) =>
        new(
            SchemaVersion: 1,
            RuntimeMode: mode,
            ControlPlaneInstanceId: Guid.NewGuid(),
            ApiProcessInstanceId: Guid.NewGuid(),
            EnvironmentName: string.Equals(mode, MemRuntimeModes.ContainerizedProduction, StringComparison.Ordinal)
                ? "Production"
                : "Development",
            RunningInContainer: runningInContainer,
            ContentRootPath: "/app",
            ContentRootKind: runningInContainer ? "published-container" : "source-tree",
            StateRootPath: "/data",
            StateRootKind: runningInContainer ? "persistent-volume" : "filesystem",
            StateRootProfile: MemStateRootProfiles.Default,
            UiDeliveryMode: runningInContainer ? MemUiDeliveryModes.EmbeddedSpa : MemUiDeliveryModes.Vite,
            DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
            DockerEndpointKind: "local-unix-socket",
            ConfiguredContainerName: containerName,
            ApplicationName: "mem-control-plane",
            Version: "0.2.0-test",
            Commit: "test-commit",
            ValidationState: MemRuntimeValidationStates.Valid,
            MutationsAllowed: true,
            ShowDevelopmentBanner: false,
            Warnings: []);

    private static DockerContainerInspection Inspection(
        params DockerBindMountInspection[] mounts) =>
        new(
            Id: "control-plane-container-id",
            Name: "mem-control-plane",
            Running: true,
            Status: "running",
            HealthStatus: "healthy")
        {
            BindMounts = mounts
        };

    private sealed class RecordingDockerHost : IDockerHost
    {
        public DockerContainerInspection Inspection { get; set; } =
            HostDataPathParityValidatorTests.Inspection();

        public int InspectCount { get; private set; }

        public string? LastInspectedContainer { get; private set; }

        public Task<DockerContainerInspection> InspectAsync(
            string containerId,
            CancellationToken ct)
        {
            InspectCount++;
            LastInspectedContainer = containerId;
            return Task.FromResult(Inspection);
        }

        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task StartContainerAsync(string containerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task StopContainerAsync(string containerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RemoveContainerAsync(string containerId, bool force, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
            string labelKey,
            string labelValue,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<int> WaitForExitAsync(string containerId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task EnsureNetworkConnectedAsync(
            string containerId,
            string networkName,
            string? alias,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
