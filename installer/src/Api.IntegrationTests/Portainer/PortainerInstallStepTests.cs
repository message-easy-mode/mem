using System.Runtime.InteropServices;
using System.Text.Json;
using Core.Runtime;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Integrations.Portainer.Services;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Portainer;

public sealed class PortainerInstallStepTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public void New_install_plans_select_the_pinned_Portainer_runtime_by_default()
    {
        var plan = InstallPlanFactory.CreateDefault();

        Assert.True(plan.SupportTools.Portainer.Enabled);
        Assert.True(plan.SupportTools.Portainer.UseExistingIfDetected);
        Assert.Equal("portainer", plan.SupportTools.Portainer.ContainerName);
        Assert.Equal(9443, plan.SupportTools.Portainer.HostPort);
    }

    [Fact]
    public async Task Persistent_volume_step_does_not_mutate_Portainer_before_existing_runtime_detection()
    {
        var runner = new RecordingCommandRunner();
        var dockerHost = new RecordingDockerHost(() => { });
        var executor = CreateExecutor(
            runner,
            portainerRuntimeService: null!,
            dockerHost: dockerHost);
        var plan = InstallPlanFactory.CreateDefault();

        var result = await executor.ExecuteAsync(
            Context(
                InstallStepNames.CreatePersistentVolumes,
                JsonSerializer.Serialize(plan, JsonOptions)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(runner.Calls);
        Assert.DoesNotContain("portainer_data", dockerHost.EnsuredVolumes);
        Assert.Contains(plan.Platform.Postgres.VolumeName, dockerHost.EnsuredVolumes);
        Assert.Contains("mem_npm_data", dockerHost.EnsuredVolumes);
        Assert.Contains("mem_npm_letsencrypt", dockerHost.EnsuredVolumes);
    }

    [Fact]
    public async Task Selected_support_tools_step_uses_the_explicit_installation_preparation_path()
    {
        await using var fixture = await RuntimeFixture.CreateAsync();
        var executor = CreateExecutor(
            new RecordingCommandRunner(),
            fixture.Service);
        var plan = InstallPlanFactory.CreateDefault();

        var result = await executor.ExecuteAsync(
            Context(
                InstallStepNames.StartSelectedSupportTools,
                JsonSerializer.Serialize(plan, JsonOptions)),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("Portainer 2.39.5", result.Message, StringComparison.Ordinal);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(1, fixture.Docker.PullCount);
        Assert.StartsWith("sha256:", fixture.Docker.LastSpec!.Image);
    }

    private static InstallStepExecutor CreateExecutor(
        ICommandRunner runner,
        PortainerRuntimeService portainerRuntimeService,
        IDockerHost? dockerHost = null) =>
        new(
            runner,
            dockerHost: dockerHost ?? new RecordingDockerHost(() => { }),
            dockerRuntimeProbe: null!,
            npmAdminProbe: null!,
            db: null!,
            certificateStorage: null!,
            certificateValidation: null!,
            npmCertificateProbe: null!,
            npmReadiness: null!,
            npmProxyHostService: null!,
            npmOptions: null!,
            memCliHostCommandInstaller: null!,
            approvedPostgresRuntimeProvider: null!,
            portainerRuntimeService: portainerRuntimeService,
            installationSecretStore: null!,
            platformCertificateProvisioner: null!,
            npmInitialAdminBootstrap: null!,
            logger: NullLogger<InstallStepExecutor>.Instance);

    private static InstallStepContext Context(
        string stepName,
        string configJson) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            stepName,
            Sequence: 1,
            ConfigJson: configJson);

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public List<CommandCall> Calls { get; } = [];

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls.Add(new CommandCall(fileName, arguments.ToArray()));
            var isInspect = arguments.Count >= 2 &&
                            arguments[0] == "volume" &&
                            arguments[1] == "inspect";
            return Task.FromResult(new CommandResult(
                ExitCode: isInspect ? 1 : 0,
                StandardOutput: string.Empty,
                StandardError: isInspect ? "not found" : string.Empty));
        }
    }

    private sealed record CommandCall(
        string FileName,
        IReadOnlyList<string> Arguments);

    private sealed class RuntimeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private RuntimeFixture(
            SqliteConnection connection,
            MemDbContext db,
            RecordingDockerHost docker,
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
        public RecordingDockerHost Docker { get; }
        public RecordingImageInspector ImageInspector { get; }
        public PortainerRuntimeService Service { get; }

        public static async Task<RuntimeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(
                new DbContextOptionsBuilder<MemDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await db.Database.EnsureCreatedAsync();

            var options = new PortainerRuntimeOptions();
            var inspector = new RecordingImageInspector();
            var docker = new RecordingDockerHost(inspector.MakeAvailable);
            var provider = new PortainerRuntimeImageProvider(
                options,
                inspector,
                docker,
                new FixedArchitectureReader());
            var service = new PortainerRuntimeService(
                docker,
                new RuntimePortPlanner(new PortCheckService()),
                db,
                options,
                provider);
            return new RuntimeFixture(
                connection,
                db,
                docker,
                inspector,
                service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedArchitectureReader : IPortainerHostArchitectureReader
    {
        public Architecture Current => Architecture.X64;
    }

    private sealed class RecordingImageInspector : IRuntimeImageInspector
    {
        private bool _available;

        public string ImageId { get; } =
            $"sha256:{new string('a', 64)}";
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
            PullCount++;
            _available = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDockerHost(Action onPull) : IDockerHost
    {
        private DockerContainerInspection? _inspection;

        public int PullCount { get; private set; }
        public int CreateCount { get; private set; }
        public DockerContainerSpec? LastSpec { get; private set; }
        public List<string> EnsuredVolumes { get; } = [];

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

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct)
        {
            EnsuredVolumes.Add(volumeName);
            return Task.CompletedTask;
        }

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
            var port = uint.Parse(spec.PortBindings["9443/tcp"]);
            _inspection = new DockerContainerInspection(
                "portainer-created",
                spec.Name,
                spec.Image,
                "created",
                false,
                [new DockerPortBinding(9443, port, "tcp", "127.0.0.1")]);
            return Task.FromResult("portainer-created");
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct)
        {
            _inspection = _inspection! with
            {
                State = "running",
                Running = true
            };
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct) =>
            Task.CompletedTask;

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
