using System.Runtime.InteropServices;
using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Portainer.Contracts;
using Modules.Integrations.Portainer.Services;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Portainer;

public sealed class PortainerRuntimeServiceTests
{
    [Fact]
    public async Task Explicit_setup_pulls_the_exact_patch_then_deploys_from_the_immutable_local_identity()
    {
        await using var fixture = await Fixture.CreateAsync(imageAvailable: false);

        var result = await fixture.Service.EnsureInstalledForSetupAsync(
            new PortainerInstallRequest(
                PreferredUiHostPort: 19443,
                ForcePreferredPort: true,
                UseExistingIfDetected: true),
            CancellationToken.None);

        Assert.Equal("installed", result.Status);
        Assert.True(result.PulledImage);
        Assert.True(result.CreatedContainer);
        Assert.False(result.ReusedExisting);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(1, fixture.Docker.PullCount);
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(1, fixture.Docker.StartCount);

        var spec = Assert.IsType<DockerContainerSpec>(fixture.Docker.LastSpec);
        Assert.Equal(fixture.ImageInspector.ImageId, spec.Image);
        Assert.Equal("19443", spec.PortBindings["9443/tcp"]);
        Assert.DoesNotContain("8000/tcp", spec.PortBindings.Keys);
        Assert.Equal(
            new DockerBindMount(
                "/var/run/docker.sock",
                "/var/run/docker.sock"),
            Assert.Single(spec.BindMounts));
        Assert.Equal(
            new DockerVolumeMount("portainer_data", "/data"),
            Assert.Single(spec.VolumeMounts));
        Assert.Equal(DockerRestartPolicyName.Always, spec.RestartPolicy.Name);
        Assert.Equal("true", spec.Labels[ManagedContainerLabels.ManagedKey]);
        Assert.Equal(
            ManagedServiceNames.Portainer,
            spec.Labels[ManagedContainerLabels.ServiceKey]);
        Assert.Null(spec.NetworkName);

        var record = Assert.Single(fixture.Db.RuntimeServices);
        Assert.Equal(fixture.ImageInspector.ImageId, record.Image);
        Assert.Equal(9443, record.ContainerPort);
        Assert.Equal(19443, record.SelectedHostPort);
        Assert.True(result.Runtime.Ready);
        Assert.True(result.Runtime.Managed);
        Assert.True(result.Runtime.UsesApprovedRuntime);
        Assert.True(result.Runtime.DataRetained);
        Assert.False(result.Runtime.PublishesPublicIngress);
    }

    [Fact]
    public async Task Operational_start_never_pulls_and_verifies_the_owned_runtime()
    {
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: true,
            container: Container(
                id: "portainer-owned",
                image: ApprovedImageId,
                running: false,
                privatePort: 9443,
                publicPort: 19443),
            record: Record("portainer-owned", ApprovedImageId, "exited"));

        var status = await fixture.Service.StartManagedAsync(CancellationToken.None);

        Assert.True(status.Running);
        Assert.True(status.Ready);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(0, fixture.Docker.PullCount);
        Assert.Equal(0, fixture.Docker.CreateCount);
    }

    [Fact]
    public async Task Managed_runtime_starts_without_a_pull_when_the_approved_tag_is_not_local()
    {
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: false,
            container: Container(
                id: "portainer-owned-without-tag",
                image: ApprovedImageId,
                running: false,
                privatePort: 9443,
                publicPort: 19443),
            record: Record(
                "portainer-owned-without-tag",
                ApprovedImageId,
                "exited"));

        var status = await fixture.Service.StartManagedAsync(CancellationToken.None);

        Assert.True(status.Running);
        Assert.Contains("portainer_approved_image_missing", status.Warnings);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(0, fixture.Docker.PullCount);
    }

    [Fact]
    public async Task Working_2_39_1_container_is_observed_but_never_adopted_or_upgraded()
    {
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: false,
            container: Container(
                id: "legacy-portainer",
                image: "portainer/portainer-ce:2.39.1",
                running: true,
                privatePort: 9000,
                publicPort: 9000));

        var result = await fixture.Service.EnsureInstalledForSetupAsync(
            new PortainerInstallRequest(
                PreferredUiHostPort: 9443,
                ForcePreferredPort: false,
                UseExistingIfDetected: true),
            CancellationToken.None);

        Assert.Equal("observed-unmanaged", result.Status);
        Assert.True(result.ReusedExisting);
        Assert.False(result.CreatedContainer);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.Equal(0, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(0, fixture.Docker.PullCount);
        Assert.False(result.Runtime.Managed);
        Assert.Equal("unmanaged", result.Runtime.OwnershipState);
        Assert.Equal("2.39.1", result.Runtime.ObservedVersion);
        Assert.True(result.Runtime.UpgradeAvailable);
        Assert.True(result.Runtime.Ready);
        Assert.Equal(9000, result.Runtime.UiHostPort);
        Assert.False(result.Runtime.DataRetained);
        Assert.Empty(fixture.Db.RuntimeServices);
    }

    [Fact]
    public async Task Current_unmanaged_patch_is_left_read_only_without_a_false_upgrade_claim()
    {
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: true,
            container: Container(
                id: "current-unmanaged-portainer",
                image: PortainerRuntimeOptions.ApprovedImage,
                running: true,
                privatePort: 9443,
                publicPort: 9443));

        var result = await fixture.Service.EnsureInstalledForSetupAsync(
            new PortainerInstallRequest(),
            CancellationToken.None);

        Assert.Equal("observed-unmanaged", result.Status);
        Assert.False(result.Runtime.Managed);
        Assert.False(result.Runtime.UpgradeAvailable);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(0, fixture.Docker.PullCount);
    }

    [Fact]
    public async Task Unmanaged_same_name_conflict_fails_closed_when_reuse_is_not_allowed()
    {
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: true,
            container: Container(
                id: "foreign-portainer",
                image: "portainer/portainer-ce:2.39.1",
                running: true,
                privatePort: 9000,
                publicPort: 9000));

        var exception = await Assert.ThrowsAsync<PortainerOperationException>(() =>
            fixture.Service.EnsureInstalledForSetupAsync(
                new PortainerInstallRequest(
                    UseExistingIfDetected: false),
                CancellationToken.None));

        Assert.Equal("portainer_unmanaged_container", exception.Code);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
    }

    [Fact]
    public async Task Replacement_identity_mismatch_blocks_operational_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: true,
            container: Container(
                id: "replacement-container",
                image: ApprovedImageId,
                running: false,
                privatePort: 9443,
                publicPort: 19443),
            record: Record("expected-container", ApprovedImageId, "exited"));

        var exception = await Assert.ThrowsAsync<PortainerOperationException>(() =>
            fixture.Service.StartManagedAsync(CancellationToken.None));

        Assert.Equal("portainer_container_identity_mismatch", exception.Code);
        Assert.Equal(0, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
    }

    [Fact]
    public async Task Existing_managed_older_runtime_is_retained_for_an_explicit_future_upgrade()
    {
        var previousImageId = $"sha256:{new string('c', 64)}";
        await using var fixture = await Fixture.CreateAsync(
            imageAvailable: true,
            container: Container(
                id: "managed-previous",
                image: previousImageId,
                running: true,
                privatePort: 9443,
                publicPort: 19443),
            record: Record("managed-previous", previousImageId, "running"));

        var result = await fixture.Service.EnsureInstalledForSetupAsync(
            new PortainerInstallRequest(),
            CancellationToken.None);

        Assert.Equal("existing-managed", result.Status);
        Assert.False(result.Runtime.UsesApprovedRuntime);
        Assert.True(result.Runtime.UpgradeAvailable);
        Assert.Contains(
            "explicit upgrade workflow",
            result.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.Equal(0, fixture.Docker.StopCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
    }

    private const string ApprovedImageId =
        "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static DockerContainerInspection Container(
        string id,
        string image,
        bool running,
        uint privatePort,
        uint publicPort) =>
        new(
            id,
            "portainer",
            image,
            running ? "running" : "exited",
            running,
            [new DockerPortBinding(privatePort, publicPort, "tcp", "127.0.0.1")]);

    private static RuntimeServiceEntity Record(
        string containerId,
        string image,
        string status) =>
        new()
        {
            Id = Guid.NewGuid(),
            ServiceName = ManagedServiceNames.Portainer,
            ContainerName = ManagedContainerNames.Portainer,
            Image = image,
            ContainerPort = 9443,
            PreferredHostPort = 19443,
            SelectedHostPort = 19443,
            ContainerId = containerId,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            LastObservedAtUtc = DateTime.UtcNow
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            FakeDockerHost docker,
            RecordingImageInspector imageInspector,
            PortainerRuntimeService service)
        {
            _connection = connection;
            Db = db;
            Docker = docker;
            ImageInspector = imageInspector;
            Service = service;
        }

        public MemDbContext Db { get; }
        public FakeDockerHost Docker { get; }
        public RecordingImageInspector ImageInspector { get; }
        public PortainerRuntimeService Service { get; }

        public static async Task<Fixture> CreateAsync(
            bool imageAvailable,
            DockerContainerInspection? container = null,
            RuntimeServiceEntity? record = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(
                new DbContextOptionsBuilder<MemDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();
            if (record is not null)
            {
                db.RuntimeServices.Add(record);
                await db.SaveChangesAsync();
            }

            var options = new PortainerRuntimeOptions();
            var inspector = new RecordingImageInspector(imageAvailable);
            var docker = new FakeDockerHost(container, inspector.MakeAvailable);
            var provider = new PortainerRuntimeImageProvider(
                options,
                inspector,
                docker,
                new FixedArchitectureReader(Architecture.X64));
            var service = new PortainerRuntimeService(
                docker,
                new RuntimePortPlanner(new PortCheckService()),
                db,
                options,
                provider);

            return new Fixture(connection, db, docker, inspector, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedArchitectureReader(Architecture architecture)
        : IPortainerHostArchitectureReader
    {
        public Architecture Current { get; } = architecture;
    }

    private sealed class RecordingImageInspector(bool available)
        : IRuntimeImageInspector
    {
        private bool _available = available;

        public string ImageId { get; } = ApprovedImageId;
        public int PullCount { get; private set; }

        public void MakeAvailable() => _available = true;

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeImageInspection?>(_available
                ? new RuntimeImageInspection(
                    ImageId,
                    [$"portainer/portainer-ce@sha256:{new string('b', 64)}"],
                    [])
                : null);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            Assert.Equal("portainer/portainer-ce:2.39.5", immutableReference);
            PullCount++;
            _available = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDockerHost(
        DockerContainerInspection? inspection,
        Action onPull) : IDockerHost
    {
        private DockerContainerInspection? _inspection = inspection;

        public int PullCount { get; private set; }
        public int CreateCount { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public DockerContainerSpec? LastSpec { get; private set; }

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);

        public Task PullImageAsync(string image, CancellationToken ct)
        {
            Assert.Equal(PortainerRuntimeOptions.ApprovedImage, image);
            PullCount++;
            onPull();
            return Task.CompletedTask;
        }

        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(
            string namePrefix,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
            string labelKey,
            string labelValue,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<DockerContainerInspection?> InspectByNameAsync(
            string containerName,
            CancellationToken ct) =>
            Task.FromResult(_inspection);

        public Task EnsureNetworkAsync(string networkName, CancellationToken ct) =>
            Task.CompletedTask;

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) => Task.CompletedTask;
        public Task ConnectContainerToNetworkAsync(string containerIdOrName, string networkName, CancellationToken ct) => Task.CompletedTask;
        public Task CopyFileToContainerAsync(string containerIdOrName, string destinationDirectory, string fileName, ReadOnlyMemory<byte> content, UnixFileMode mode, CancellationToken ct) => Task.CompletedTask;
        public Task<DockerExecResult> ExecAsync(string containerIdOrName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken ct) => Task.FromResult(new DockerExecResult(0, string.Empty, string.Empty, false));

        public Task<string> CreateContainerAsync(
            DockerContainerSpec spec,
            CancellationToken ct)
        {
            CreateCount++;
            LastSpec = spec;
            var port = uint.Parse(spec.PortBindings["9443/tcp"]);
            _inspection = new DockerContainerInspection(
                "managed-portainer",
                spec.Name,
                spec.Image,
                "created",
                false,
                [new DockerPortBinding(9443, port, "tcp", "127.0.0.1")]);
            return Task.FromResult("managed-portainer");
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct)
        {
            StartCount++;
            if (_inspection is not null)
            {
                _inspection = _inspection with
                {
                    Running = true,
                    State = "running"
                };
            }

            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct)
        {
            StopCount++;
            if (_inspection is not null)
            {
                _inspection = _inspection with
                {
                    Running = false,
                    State = "exited"
                };
            }

            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(
            string containerId,
            bool force,
            bool removeVolumes,
            CancellationToken ct)
        {
            _inspection = null;
            return Task.CompletedTask;
        }

        public Task<string> GetLogsAsync(
            string containerId,
            int tail,
            CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }
}
