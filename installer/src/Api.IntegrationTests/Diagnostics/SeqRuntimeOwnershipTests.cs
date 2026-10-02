using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Shared.RuntimeImages;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqRuntimeOwnershipTests
{
    [Fact]
    public async Task Lifecycle_refuses_a_name_matching_container_without_a_MEM_runtime_record()
    {
        await using var fixture = await Fixture.CreateAsync(
            new DockerContainerInspection(
                "container-unmanaged",
                "mem-seq",
                "datalust/seq:2026.1.17044",
                "exited",
                false,
                []));

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.StartAsync(CancellationToken.None));

        Assert.Equal("seq_unmanaged_container", exception.Code);
        Assert.Equal(0, fixture.Docker.StartCount);
    }

    [Fact]
    public async Task Removal_refuses_a_replacement_container_with_a_different_identity()
    {
        await using var fixture = await Fixture.CreateAsync(
            new DockerContainerInspection(
                "container-replacement",
                "mem-seq",
                $"sha256:{new string('a', 64)}",
                "running",
                true,
                []));
        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = "seq",
            ContainerName = "mem-seq",
            Image = $"sha256:{new string('a', 64)}",
            ContainerPort = 80,
            PreferredHostPort = 15341,
            SelectedHostPort = 15341,
            HostPath = "/data/seq",
            ContainerId = "container-original",
            Status = "running",
            CreatedAtUtc = DateTime.UtcNow,
            LastObservedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.RemoveAsync(CancellationToken.None));

        Assert.Equal("seq_container_identity_mismatch", exception.Code);
        Assert.Equal(0, fixture.Docker.RemoveCount);
    }

    [Fact]
    public async Task Status_reports_a_different_Control_Plane_owner_before_treating_the_runtime_as_managed()
    {
        var currentInstance = Guid.NewGuid();
        var foreignInstance = Guid.NewGuid();
        var inspection = new DockerContainerInspection(
            "container-current",
            "mem-seq-local",
            $"sha256:{new string('a', 64)}",
            "running",
            true,
            [])
        {
            Labels = new Dictionary<string, string>
            {
                [ManagedContainerLabels.CanonicalManagedKey] = "true",
                [ManagedContainerLabels.CanonicalServiceKey] = ManagedServiceNames.Seq,
                [ManagedContainerLabels.ControlPlaneInstanceKey] = foreignInstance.ToString("D"),
                [ManagedContainerLabels.RuntimeModeKey] = MemRuntimeModes.ContainerizedDevelopment
            }
        };

        await using var fixture = await Fixture.CreateAsync(
            inspection,
            RuntimeContext(currentInstance));
        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = ManagedServiceNames.Seq,
            ContainerName = "mem-seq-local",
            Image = inspection.Image,
            ContainerPort = 80,
            PreferredHostPort = SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort,
            SelectedHostPort = SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort,
            HostPath = "/data/seq-local",
            ContainerId = inspection.Id,
            Status = "running",
            CreatedAtUtc = DateTime.UtcNow,
            LastObservedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var status = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.False(status.Managed);
        Assert.False(status.UsesApprovedRuntime);
        Assert.Equal("control-plane-mismatch", status.OwnershipState);
        Assert.Equal("seq_control_plane_ownership_mismatch", status.WarningCode);
    }

    [Fact]
    public async Task Lifecycle_reports_the_foreign_Control_Plane_owner_before_stale_container_identity()
    {
        var currentInstance = Guid.NewGuid();
        var foreignInstance = Guid.NewGuid();
        var inspection = new DockerContainerInspection(
            "container-replacement",
            "mem-seq-local",
            $"sha256:{new string('a', 64)}",
            "running",
            true,
            [])
        {
            Labels = new Dictionary<string, string>
            {
                [ManagedContainerLabels.CanonicalManagedKey] = "true",
                [ManagedContainerLabels.CanonicalServiceKey] = ManagedServiceNames.Seq,
                [ManagedContainerLabels.ControlPlaneInstanceKey] = foreignInstance.ToString("D"),
                [ManagedContainerLabels.RuntimeModeKey] = MemRuntimeModes.ContainerizedDevelopment
            }
        };

        await using var fixture = await Fixture.CreateAsync(
            inspection,
            RuntimeContext(currentInstance));
        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = ManagedServiceNames.Seq,
            ContainerName = "mem-seq-local",
            Image = inspection.Image,
            ContainerPort = 80,
            PreferredHostPort = SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort,
            SelectedHostPort = SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort,
            HostPath = "/data/seq-local",
            ContainerId = "container-original",
            Status = "running",
            CreatedAtUtc = DateTime.UtcNow,
            LastObservedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.RemoveAsync(CancellationToken.None));

        Assert.Equal("seq_control_plane_ownership_mismatch", exception.Code);
        Assert.Equal(0, fixture.Docker.RemoveCount);
    }

    [Fact]
    public async Task Local_context_does_not_adopt_the_legacy_unscoped_Seq_container()
    {
        var inspection = new DockerContainerInspection(
            "legacy-seq",
            ManagedContainerNames.Seq,
            $"sha256:{new string('a', 64)}",
            "running",
            true,
            [])
        {
            Labels = new Dictionary<string, string>
            {
                [ManagedContainerLabels.CanonicalManagedKey] = "true",
                [ManagedContainerLabels.CanonicalServiceKey] = ManagedServiceNames.Seq,
                [ManagedContainerLabels.ControlPlaneInstanceKey] = Guid.NewGuid().ToString("D"),
                [ManagedContainerLabels.RuntimeModeKey] = MemRuntimeModes.ContainerizedDevelopment
            }
        };

        await using var fixture = await Fixture.CreateAsync(
            inspection,
            RuntimeContext(Guid.NewGuid()));

        var status = await fixture.Service.GetStatusAsync(CancellationToken.None);

        Assert.False(status.Exists);
        Assert.False(status.Managed);
        Assert.Equal("absent", status.OwnershipState);
        Assert.Equal("mem-seq-local", status.ContainerName);
        Assert.Equal("mem-seq-local", fixture.Docker.LastInspectedName);
        Assert.Equal(0, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.Docker.RemoveCount);
    }


    [Fact]
    public async Task Local_context_ignores_an_inactive_context_runtime_record_without_mutating_it()
    {
        var currentInstance = Guid.NewGuid();
        var foreignInstance = Guid.NewGuid();
        var foreign = ForeignDevelopmentSeq(foreignInstance);
        await using var fixture = await Fixture.CreateAsync(
            foreign,
            RuntimeContext(currentInstance));
        var recordId = Guid.NewGuid();
        fixture.Db.RuntimeServices.Add(RuntimeRecord(
            recordId,
            ManagedContainerNames.Seq,
            foreign));
        await fixture.Db.SaveChangesAsync();

        var status = await fixture.Service.GetStatusAsync(CancellationToken.None);
        await fixture.Service.RemoveAsync(CancellationToken.None);

        Assert.False(status.Exists);
        Assert.False(status.Managed);
        Assert.Equal("absent", status.OwnershipState);
        Assert.Equal("mem-seq-local", status.ContainerName);
        var retained = await fixture.Db.RuntimeServices.SingleAsync();
        Assert.Equal(recordId, retained.Id);
        Assert.Equal(ManagedContainerNames.Seq, retained.ContainerName);
        Assert.NotNull(fixture.Docker.GetContainer(ManagedContainerNames.Seq));
        Assert.Equal(0, fixture.Docker.RemoveCount);
        Assert.Equal(0, fixture.Docker.StartCount);
    }

    [Fact]
    public async Task Local_context_deploy_reuses_the_inactive_context_record_after_active_container_is_verified_running()
    {
        var currentInstance = Guid.NewGuid();
        var foreignInstance = Guid.NewGuid();
        var foreign = ForeignDevelopmentSeq(foreignInstance);
        await using var fixture = await Fixture.CreateAsync(
            foreign,
            RuntimeContext(currentInstance));
        var recordId = Guid.NewGuid();
        fixture.Db.RuntimeServices.Add(RuntimeRecord(
            recordId,
            ManagedContainerNames.Seq,
            foreign));
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.DeployAsync(
            new SeqDeployRequest(
                PreferredHostPort: fixture.Options.PreferredHostPort,
                ForcePreferredPort: true),
            CancellationToken.None);

        var record = await fixture.Db.RuntimeServices.SingleAsync();
        var local = fixture.Docker.GetContainer("mem-seq-local");
        var retainedForeign = fixture.Docker.GetContainer(ManagedContainerNames.Seq);
        Assert.Equal(recordId, record.Id);
        Assert.Equal("mem-seq-local", record.ContainerName);
        Assert.NotNull(local);
        Assert.True(local!.Running);
        Assert.NotNull(retainedForeign);
        Assert.Equal(foreign.Id, retainedForeign!.Id);
        Assert.True(retainedForeign.Running);
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.Docker.RemoveCount);
        Assert.Equal("mem-seq-local", fixture.Docker.LastSpec!.Name);
        Assert.Contains("seq-local", fixture.Docker.LastSpec.NetworkAliases);
        Assert.Equal(
            currentInstance.ToString("D"),
            fixture.Docker.LastSpec.Labels[ManagedContainerLabels.ControlPlaneInstanceKey]);
        Assert.Equal(
            MemRuntimeModes.LocalDevelopment,
            fixture.Docker.LastSpec.Labels[ManagedContainerLabels.RuntimeModeKey]);
    }

    [Fact]
    public async Task Non_context_scoped_runtime_does_not_recycle_a_mismatched_persisted_record()
    {
        await using var fixture = await Fixture.CreateAsync(null);
        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = ManagedServiceNames.Seq,
            ContainerName = "other-seq-runtime",
            Image = $"sha256:{new string('a', 64)}",
            ContainerPort = 80,
            PreferredHostPort = 15341,
            SelectedHostPort = 15341,
            HostPath = "/data/seq",
            ContainerId = "other-seq-id",
            Status = "exited",
            CreatedAtUtc = DateTime.UtcNow,
            LastObservedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.DeployAsync(
                new SeqDeployRequest(15341, ForcePreferredPort: true),
                CancellationToken.None));

        Assert.Equal("seq_runtime_record_conflict", exception.Code);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.Equal("other-seq-runtime",
            (await fixture.Db.RuntimeServices.SingleAsync()).ContainerName);
    }

    private static DockerContainerInspection ForeignDevelopmentSeq(Guid owner) =>
        new(
            "foreign-seq",
            ManagedContainerNames.Seq,
            $"sha256:{new string('a', 64)}",
            "running",
            true,
            [new DockerPortBinding(80, 15341, "tcp", "127.0.0.1")])
        {
            Labels = new Dictionary<string, string>
            {
                [ManagedContainerLabels.CanonicalManagedKey] = "true",
                [ManagedContainerLabels.CanonicalServiceKey] = ManagedServiceNames.Seq,
                [ManagedContainerLabels.ControlPlaneInstanceKey] = owner.ToString("D"),
                [ManagedContainerLabels.RuntimeModeKey] = MemRuntimeModes.ContainerizedDevelopment
            }
        };

    private static RuntimeServiceEntity RuntimeRecord(
        Guid id,
        string containerName,
        DockerContainerInspection inspection) => new()
    {
        Id = id,
        ServiceName = ManagedServiceNames.Seq,
        ContainerName = containerName,
        Image = inspection.Image,
        ContainerPort = 80,
        PreferredHostPort = 15341,
        SelectedHostPort = 15341,
        HostPath = "/data/seq",
        ContainerId = inspection.Id,
        Status = inspection.State,
        CreatedAtUtc = DateTime.UtcNow,
        LastObservedAtUtc = DateTime.UtcNow
    };

    private static MemControlPlaneRuntimeContext RuntimeContext(Guid instanceId) => new(
        SchemaVersion: 1,
        RuntimeMode: MemRuntimeModes.LocalDevelopment,
        ControlPlaneInstanceId: instanceId,
        ApiProcessInstanceId: Guid.NewGuid(),
        EnvironmentName: "Development",
        RunningInContainer: false,
        ContentRootPath: "/repo/installer/src/Api",
        ContentRootKind: "repository-source",
        StateRootPath: "/repo/installer/data",
        StateRootKind: "repository-local",
        StateRootProfile: "repository-local",
        UiDeliveryMode: MemUiDeliveryModes.Vite,
        DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
        DockerEndpointKind: "local-unix-socket",
        ConfiguredContainerName: null,
        ApplicationName: "MEM Control Plane",
        Version: "0.2.0",
        Commit: null,
        ValidationState: MemRuntimeValidationStates.Valid,
        MutationsAllowed: true,
        ShowDevelopmentBanner: true,
        Warnings: []);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _root;
        private readonly string _adminPasswordHashVariable;

        private Fixture(
            SqliteConnection connection,
            string root,
            string adminPasswordHashVariable,
            MemDbContext db,
            SeqDiagnosticsOptions options,
            FakeDockerHost docker,
            SeqRuntimeService service)
        {
            _connection = connection;
            _root = root;
            _adminPasswordHashVariable = adminPasswordHashVariable;
            Db = db;
            Options = options;
            Docker = docker;
            Service = service;
        }

        public MemDbContext Db { get; }
        public SeqDiagnosticsOptions Options { get; }
        public FakeDockerHost Docker { get; }
        public SeqRuntimeService Service { get; }

        public static async Task<Fixture> CreateAsync(
            DockerContainerInspection? inspection,
            MemControlPlaneRuntimeContext? runtimeContext = null)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-seq-runtime-ownership-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var adminPasswordHashVariable =
                $"MEM_TEST_SEQ_ADMIN_HASH_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(
                adminPasswordHashVariable,
                "test-seq-administrator-password-hash");

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();
            var docker = new FakeDockerHost(inspection);
            var options = new SeqDiagnosticsOptions
            {
                ManagementEnabled = true,
                EulaAccepted = true,
                AllowOperationalPull = false,
                ApprovedImageReference = "datalust/seq:2026.1.17044",
                ExpectedVersion = "2026.1.17044",
                PreferredHostPort = 25341,
                HostDataPath = Path.Combine(root, "seq"),
                SecretRootPath = Path.Combine(root, "secrets", "seq"),
                ApiKeyFilePath = Path.Combine(
                    root,
                    "secrets",
                    "seq",
                    "ingestion-api-key"),
                AdminPasswordHashEnvironmentVariableName =
                    adminPasswordHashVariable,
                AdminPasswordHashFilePath = Path.Combine(
                    root,
                    "secrets",
                    "seq",
                    "admin-password-hash"),
                BootstrapStatePath = Path.Combine(
                    root,
                    "diagnostics",
                    "seq-bootstrap.json"),
                DeliveryStatePath = Path.Combine(
                    root,
                    "diagnostics",
                    "seq-delivery.json")
            };
            var environment = new TestHostEnvironment(root);
            var secrets = new SeqSecretResolver(environment);
            var imageResolver = new SeqRuntimeImageResolver(
                options,
                new FixedImageInspector());
            var service = new SeqRuntimeService(
                docker,
                new RuntimePortPlanner(new PortCheckService()),
                db,
                options,
                secrets,
                imageResolver,
                environment: environment,
                runtimeContext: runtimeContext);
            return new Fixture(
                connection,
                root,
                adminPasswordHashVariable,
                db,
                options,
                docker,
                service);
        }

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(_adminPasswordHashVariable, null);
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class FakeDockerHost : IDockerHost
    {
        private readonly Dictionary<string, DockerContainerInspection> _containers =
            new(StringComparer.OrdinalIgnoreCase);

        public FakeDockerHost(DockerContainerInspection? inspection)
        {
            if (inspection is not null)
            {
                _containers[inspection.Name] = inspection;
            }
        }

        public int CreateCount { get; private set; }
        public int StartCount { get; private set; }
        public int RemoveCount { get; private set; }
        public string? LastInspectedName { get; private set; }
        public DockerContainerSpec? LastSpec { get; private set; }

        public DockerContainerInspection? GetContainer(string name) =>
            _containers.TryGetValue(name, out var inspection)
                ? inspection
                : null;

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);
        public Task PullImageAsync(string image, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(string namePrefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<DockerContainerInspection?> InspectByNameAsync(
            string containerName,
            CancellationToken ct)
        {
            LastInspectedName = containerName;
            return Task.FromResult(GetContainer(containerName));
        }

        public Task EnsureNetworkAsync(string networkName, CancellationToken ct) =>
            Task.CompletedTask;

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ConnectContainerToNetworkAsync(string containerIdOrName, string networkName, CancellationToken ct) =>
            Task.CompletedTask;

        public Task CopyFileToContainerAsync(string containerIdOrName, string destinationDirectory, string fileName, ReadOnlyMemory<byte> content, UnixFileMode mode, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<DockerExecResult> ExecAsync(string containerIdOrName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken ct) =>
            Task.FromResult(new DockerExecResult(0, string.Empty, string.Empty, false));

        public Task<string> CreateContainerAsync(
            DockerContainerSpec spec,
            CancellationToken ct)
        {
            CreateCount++;
            LastSpec = spec;
            var id = $"created-{Guid.NewGuid():N}";
            var publicPort = spec.PortBindings.TryGetValue("80/tcp", out var portText) &&
                             uint.TryParse(portText, out var parsed)
                ? parsed
                : 0;
            _containers[spec.Name] = new DockerContainerInspection(
                id,
                spec.Name,
                spec.Image,
                "created",
                false,
                publicPort == 0
                    ? []
                    : [new DockerPortBinding(80, publicPort, "tcp", "127.0.0.1")])
            {
                Labels = spec.Labels.ToDictionary(
                    item => item.Key,
                    item => item.Value,
                    StringComparer.Ordinal)
            };
            return Task.FromResult(id);
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct)
        {
            StartCount++;
            UpdateById(containerId, item => item with
            {
                State = "running",
                Running = true
            });
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct)
        {
            UpdateById(containerId, item => item with
            {
                State = "exited",
                Running = false
            });
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(
            string containerId,
            bool force,
            bool removeVolumes,
            CancellationToken ct)
        {
            RemoveCount++;
            var match = _containers
                .FirstOrDefault(item => string.Equals(
                    item.Value.Id,
                    containerId,
                    StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(match.Key))
            {
                _containers.Remove(match.Key);
            }

            return Task.CompletedTask;
        }

        public Task<string> GetLogsAsync(
            string containerId,
            int tail,
            CancellationToken ct) => Task.FromResult(string.Empty);

        private void UpdateById(
            string containerId,
            Func<DockerContainerInspection, DockerContainerInspection> update)
        {
            var match = _containers
                .FirstOrDefault(item => string.Equals(
                    item.Value.Id,
                    containerId,
                    StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(match.Key))
            {
                _containers[match.Key] = update(match.Value);
            }
        }
    }

    private sealed class FixedImageInspector : IRuntimeImageInspector
    {
        private static readonly RuntimeImageInspection Image = new(
            $"sha256:{new string('a', 64)}",
            [$"datalust/seq@sha256:{new string('b', 64)}"],
            []);

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeImageInspection?>(Image);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
