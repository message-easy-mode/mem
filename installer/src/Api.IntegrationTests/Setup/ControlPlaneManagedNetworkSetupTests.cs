using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Runtime;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class ControlPlaneManagedNetworkSetupTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SETUP_NPM_CLEANROOM_CORR_01_network_step_attaches_containerized_control_plane_to_mem_gateway()
    {
        var root = TemporaryRoot();
        try
        {
            var runtimeContext = TestRuntimeContext.Create(
                root,
                mode: MemRuntimeModes.ContainerizedProduction,
                runningInContainer: true,
                containerName: "mem-control-plane");
            var docker = new RecordingDockerHost();
            var executor = CreateExecutor(
                docker,
                runtimeContext,
                npmAdminProbe: null!,
                bootstrap: new SuccessfulBootstrapService());

            var result = await executor.ExecuteAsync(
                Context(InstallStepNames.CreateOrVerifyDockerNetwork, sequence: 3),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(new[] { "mem-gateway" }, docker.EnsuredNetworks);
            Assert.Contains(
                ("mem-control-plane", "mem-gateway"),
                docker.NetworkConnections);
            Assert.Contains(
                "active Control Plane container is connected",
                result.Message,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SETUP_NPM_CLEANROOM_CORR_01_failed_step_7_retry_repairs_control_plane_network_before_npm_probe()
    {
        var root = TemporaryRoot();
        try
        {
            var runtimeContext = TestRuntimeContext.Create(
                root,
                mode: MemRuntimeModes.ContainerizedProduction,
                runningInContainer: true,
                containerName: "mem-control-plane");
            var events = new List<string>();
            var docker = new RecordingDockerHost(events)
            {
                Inspection = new DockerContainerInspection(
                    Id: "npm-id",
                    Name: "mem-npm",
                    Image: NpmRuntimeRelease.ApprovedImage,
                    State: "running",
                    Running: true,
                    Ports: [])
            };
            using var httpClient = new HttpClient(new RecordingHttpHandler(events));
            var npmAdminProbe = new InstallNpmAdminProbe(
                httpClient,
                new MemManagedServiceAuthorityResolver(runtimeContext));
            var bootstrap = new SuccessfulBootstrapService();
            var executor = CreateExecutor(
                docker,
                runtimeContext,
                npmAdminProbe,
                bootstrap);

            var result = await executor.ExecuteAsync(
                Context(InstallStepNames.StartNpmIngress, sequence: 7),
                CancellationToken.None);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(
                new[]
                {
                    "connect:mem-control-plane:mem-gateway",
                    "connect:mem-npm:mem-gateway",
                    "probe:http://mem-npm:81/"
                },
                events);
            Assert.NotNull(bootstrap.Target);
            Assert.Equal("mem-gateway", bootstrap.Target!.NetworkName);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static InstallStepExecutor CreateExecutor(
        IDockerHost dockerHost,
        MemControlPlaneRuntimeContext runtimeContext,
        InstallNpmAdminProbe npmAdminProbe,
        INpmInitialAdminBootstrapService bootstrap) =>
        new(
            commandRunner: null!,
            dockerHost: dockerHost,
            dockerRuntimeProbe: null!,
            npmAdminProbe: npmAdminProbe,
            db: null!,
            certificateStorage: null!,
            certificateValidation: null!,
            npmCertificateProbe: null!,
            npmReadiness: null!,
            npmProxyHostService: null!,
            npmOptions: Options.Create(new NpmApiOptions()),
            memCliHostCommandInstaller: null!,
            approvedPostgresRuntimeProvider: null!,
            portainerRuntimeService: null!,
            installationSecretStore: null!,
            platformCertificateProvisioner: null!,
            npmInitialAdminBootstrap: bootstrap,
            logger: NullLogger<InstallStepExecutor>.Instance,
            runtimeContext: runtimeContext);

    private static InstallStepContext Context(string stepName, int sequence) =>
        new(
            InstallationId: Guid.NewGuid(),
            StepId: Guid.NewGuid(),
            StepName: stepName,
            Sequence: sequence,
            ConfigJson: JsonSerializer.Serialize(InstallPlanFactory.CreateDefault(), JsonOptions),
            AttemptNumber: 1);

    private static string TemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-setup-network-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup only.
        }
    }

    private sealed class SuccessfulBootstrapService : INpmInitialAdminBootstrapService
    {
        public NpmInitialAdminBootstrapTarget? Target { get; private set; }

        public Task<NpmInitialAdminBootstrapResult> BootstrapAsync(
            Guid installationId,
            NpmInitialAdminBootstrapTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Target = target;
            return Task.FromResult(new NpmInitialAdminBootstrapResult(
                Succeeded: true,
                Status: "Ready",
                Message: "Synthetic NPM administrator bootstrap succeeded.",
                ErrorCode: null,
                InitialLoginVerified: true,
                Recreated: false,
                FinalLoginVerified: true,
                BootstrapEnvironmentRemoved: true));
        }
    }

    private sealed class RecordingHttpHandler(List<string> events) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.Add($"probe:{request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class RecordingDockerHost : IDockerHost
    {
        private readonly List<string>? _events;

        public RecordingDockerHost(List<string>? events = null)
        {
            _events = events;
        }

        public DockerContainerInspection? Inspection { get; init; }
        public List<string> EnsuredNetworks { get; } = [];
        public List<(string Container, string Network)> NetworkConnections { get; } = [];

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);
        public Task PullImageAsync(string image, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(string namePrefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<DockerContainerInspection?> InspectByNameAsync(string containerName, CancellationToken ct) =>
            Task.FromResult(string.Equals(containerName, "mem-npm", StringComparison.Ordinal)
                ? Inspection
                : null);

        public Task EnsureNetworkAsync(string networkName, CancellationToken ct)
        {
            EnsuredNetworks.Add(networkName);
            return Task.CompletedTask;
        }

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) => Task.CompletedTask;

        public Task ConnectContainerToNetworkAsync(
            string containerIdOrName,
            string networkName,
            CancellationToken ct)
        {
            NetworkConnections.Add((containerIdOrName, networkName));
            _events?.Add($"connect:{containerIdOrName}:{networkName}");
            return Task.CompletedTask;
        }

        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct) =>
            Task.FromResult("created-id");
        public Task CopyFileToContainerAsync(
            string containerIdOrName,
            string destinationDirectory,
            string fileName,
            ReadOnlyMemory<byte> content,
            UnixFileMode mode,
            CancellationToken ct) => Task.CompletedTask;
        public Task<DockerExecResult> ExecAsync(
            string containerIdOrName,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken ct) =>
            Task.FromResult(new DockerExecResult(0, string.Empty, string.Empty, false));
        public Task StartContainerAsync(string containerId, CancellationToken ct) => Task.CompletedTask;
        public Task StopContainerAsync(string containerId, CancellationToken ct) => Task.CompletedTask;
        public Task RemoveContainerAsync(string containerId, bool force, bool removeVolumes, CancellationToken ct) => Task.CompletedTask;
        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
