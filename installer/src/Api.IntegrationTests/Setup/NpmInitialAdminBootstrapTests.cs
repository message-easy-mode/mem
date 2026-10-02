using System.Net;
using System.Text;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Integrations.Npm.Services;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class NpmInitialAdminBootstrapTests
{
    private const string Image = NpmRuntimeRelease.ApprovedImage;
    private const string Email = "admin@deltabox.dev";
    private const string Password = "npm-bootstrap-test-secret";

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01B_fresh_npm_uses_bounded_no_log_bootstrap_then_clean_recreation()
    {
        var installationId = Guid.NewGuid();
        var docker = new RecordingDockerHost(Inspection(
            id: "existing",
            running: true,
            environmentNames: ["DB_SQLITE_FILE"]));
        var handler = new NpmBootstrapHttpHandler(
            setupStates: [false, true, true],
            loginStatuses: [HttpStatusCode.OK, HttpStatusCode.OK]);
        var secretStore = SeededSecretStore(installationId);

        await using var db = CreateDb();
        var credentialService = new NpmAdminCredentialService(db, secretStore);
        var service = CreateService(
            docker,
            handler,
            credentialService,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 9, 45, 0, TimeSpan.Zero)));

        var result = await service.BootstrapAsync(
            installationId,
            Target(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Succeeded", result.Status);
        Assert.True(result.InitialLoginVerified);
        Assert.True(result.Recreated);
        Assert.True(result.FinalLoginVerified);
        Assert.True(result.BootstrapEnvironmentRemoved);

        Assert.Equal(2, docker.CreatedSpecs.Count);
        var bootstrap = docker.CreatedSpecs[0];
        var clean = docker.CreatedSpecs[1];

        Assert.Equal(NpmRuntimeRelease.ApprovedImage, bootstrap.Image);
        Assert.Equal(NpmRuntimeRelease.ApprovedImage, clean.Image);

        Assert.True(bootstrap.DisableLogging);
        Assert.Equal("true", bootstrap.Labels[MemDockerOwnershipLabels.ManagedKey]);
        Assert.Equal("npm", bootstrap.Labels[MemDockerOwnershipLabels.ServiceKey]);
        Assert.Equal("platform-service", bootstrap.Labels[MemDockerOwnershipLabels.ResourceKey]);
        Assert.Equal(DockerRestartPolicyName.No, bootstrap.RestartPolicy.Name);
        Assert.Equal(Email, bootstrap.Environment["INITIAL_ADMIN_EMAIL"]);
        Assert.Equal(Password, bootstrap.Environment["INITIAL_ADMIN_PASSWORD"]);
        Assert.Equal("/data/database.sqlite", bootstrap.Environment["DB_SQLITE_FILE"]);

        Assert.False(clean.DisableLogging);
        Assert.Equal("true", clean.Labels[MemDockerOwnershipLabels.ManagedKey]);
        Assert.Equal("npm", clean.Labels[MemDockerOwnershipLabels.ServiceKey]);
        Assert.Equal("platform-service", clean.Labels[MemDockerOwnershipLabels.ResourceKey]);
        Assert.Equal(DockerRestartPolicyName.UnlessStopped, clean.RestartPolicy.Name);
        Assert.DoesNotContain("INITIAL_ADMIN_EMAIL", clean.Environment.Keys);
        Assert.DoesNotContain("INITIAL_ADMIN_PASSWORD", clean.Environment.Keys);
        Assert.Equal("/data/database.sqlite", clean.Environment["DB_SQLITE_FILE"]);

        Assert.Equal(
            bootstrap.VolumeMounts.Select(x => (x.VolumeName, x.ContainerPath)),
            clean.VolumeMounts.Select(x => (x.VolumeName, x.ContainerPath)));
        Assert.Contains(bootstrap.VolumeMounts, x =>
            x.VolumeName == "mem_npm_data" && x.ContainerPath == "/data");
        Assert.Contains(bootstrap.VolumeMounts, x =>
            x.VolumeName == "mem_npm_letsencrypt" && x.ContainerPath == "/etc/letsencrypt");

        Assert.NotEmpty(docker.Removals);
        Assert.All(docker.Removals, removal => Assert.False(removal.RemoveVolumes));
        Assert.Equal(0, docker.GetLogsCalls);

        var final = await docker.InspectByNameAsync("mem-npm", CancellationToken.None);
        Assert.NotNull(final);
        Assert.DoesNotContain("INITIAL_ADMIN_EMAIL", final!.EnvironmentVariableNames);
        Assert.DoesNotContain("INITIAL_ADMIN_PASSWORD", final.EnvironmentVariableNames);

        Assert.Equal(2, handler.LoginCalls);
        Assert.Equal(3, handler.SetupStateCalls);

        var verified = await credentialService.ResolveAsync(
            installationId,
            CancellationToken.None);
        Assert.NotNull(verified?.VerifiedAtUtc);
        Assert.Equal(
            new DateTime(2026, 8, 12, 9, 45, 0, DateTimeKind.Utc),
            verified!.VerifiedAtUtc);
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01C_CORR_02_waits_for_api_backend_after_admin_shell_is_reachable()
    {
        var installationId = Guid.NewGuid();
        var docker = new RecordingDockerHost(Inspection(
            id: "existing",
            running: true,
            environmentNames: ["DB_SQLITE_FILE"]));
        var handler = new NpmBootstrapHttpHandler(
            setupResponses:
            [
                NpmSetupStateResponse.Ready(setup: false),
                NpmSetupStateResponse.Http(HttpStatusCode.BadGateway),
                NpmSetupStateResponse.Ready(setup: true),
                NpmSetupStateResponse.Http(HttpStatusCode.BadGateway),
                NpmSetupStateResponse.Ready(setup: true)
            ],
            loginStatuses: [HttpStatusCode.OK, HttpStatusCode.OK]);
        var secretStore = SeededSecretStore(installationId);

        await using var db = CreateDb();
        var credentialService = new NpmAdminCredentialService(db, secretStore);
        var service = CreateService(
            docker,
            handler,
            credentialService,
            new FixedTimeProvider(new DateTimeOffset(2026, 8, 12, 10, 45, 0, TimeSpan.Zero)));

        var result = await service.BootstrapAsync(
            installationId,
            Target(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Succeeded", result.Status);
        Assert.True(result.InitialLoginVerified);
        Assert.True(result.Recreated);
        Assert.True(result.FinalLoginVerified);
        Assert.True(result.BootstrapEnvironmentRemoved);
        Assert.Equal(5, handler.SetupStateCalls);
        Assert.Equal(2, handler.LoginCalls);
        Assert.Equal(2, docker.CreatedSpecs.Count);

        var final = await docker.InspectByNameAsync("mem-npm", CancellationToken.None);
        Assert.NotNull(final);
        Assert.DoesNotContain("INITIAL_ADMIN_EMAIL", final!.EnvironmentVariableNames);
        Assert.DoesNotContain("INITIAL_ADMIN_PASSWORD", final.EnvironmentVariableNames);
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01B_initialized_npm_rejecting_credential_is_not_rebootstrapped()
    {
        var installationId = Guid.NewGuid();
        var docker = new RecordingDockerHost(Inspection(
            id: "existing",
            running: true,
            environmentNames: ["DB_SQLITE_FILE"]));
        var handler = new NpmBootstrapHttpHandler(
            setupStates: [true],
            loginStatuses: [HttpStatusCode.Unauthorized]);
        var secretStore = SeededSecretStore(installationId);

        await using var db = CreateDb();
        var credentialService = new NpmAdminCredentialService(db, secretStore);
        var service = CreateService(
            docker,
            handler,
            credentialService,
            TimeProvider.System);

        var result = await service.BootstrapAsync(
            installationId,
            Target(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("CredentialsRejected", result.Status);
        Assert.Equal("NpmCredentialsRejected", result.ErrorCode);
        Assert.Empty(docker.CreatedSpecs);
        Assert.Empty(docker.Removals);
        Assert.Equal(1, handler.LoginCalls);

        var verified = await credentialService.ResolveAsync(
            installationId,
            CancellationToken.None);
        Assert.NotNull(verified);
        Assert.Null(verified!.VerifiedAtUtc);
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01B_initialized_npm_with_bootstrap_env_is_sanitized_after_verified_login()
    {
        var installationId = Guid.NewGuid();
        var docker = new RecordingDockerHost(Inspection(
            id: "existing",
            running: true,
            environmentNames:
            [
                "DB_SQLITE_FILE",
                "INITIAL_ADMIN_EMAIL",
                "INITIAL_ADMIN_PASSWORD"
            ]));
        var handler = new NpmBootstrapHttpHandler(
            setupStates: [true, true],
            loginStatuses: [HttpStatusCode.OK, HttpStatusCode.OK]);
        var secretStore = SeededSecretStore(installationId);

        await using var db = CreateDb();
        var credentialService = new NpmAdminCredentialService(db, secretStore);
        var service = CreateService(
            docker,
            handler,
            credentialService,
            TimeProvider.System);

        var result = await service.BootstrapAsync(
            installationId,
            Target(),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("AlreadyInitialized", result.Status);
        Assert.True(result.InitialLoginVerified);
        Assert.True(result.Recreated);
        Assert.True(result.FinalLoginVerified);
        Assert.True(result.BootstrapEnvironmentRemoved);

        var clean = Assert.Single(docker.CreatedSpecs);
        Assert.False(clean.DisableLogging);
        Assert.Equal("true", clean.Labels[MemDockerOwnershipLabels.ManagedKey]);
        Assert.Equal("npm", clean.Labels[MemDockerOwnershipLabels.ServiceKey]);
        Assert.Equal("platform-service", clean.Labels[MemDockerOwnershipLabels.ResourceKey]);
        Assert.DoesNotContain("INITIAL_ADMIN_EMAIL", clean.Environment.Keys);
        Assert.DoesNotContain("INITIAL_ADMIN_PASSWORD", clean.Environment.Keys);
        Assert.Single(docker.Removals);
        Assert.False(docker.Removals[0].RemoveVolumes);
        Assert.Equal(2, handler.LoginCalls);
    }

    private static NpmInitialAdminBootstrapService CreateService(
        RecordingDockerHost docker,
        NpmBootstrapHttpHandler handler,
        NpmAdminCredentialService credentialService,
        TimeProvider timeProvider)
    {
        var resolver = new FixedAuthorityResolver();
        var probe = new InstallNpmAdminProbe(
            new HttpClient(handler, disposeHandler: false),
            resolver);
        var apiClient = new NpmApiClient(
            new HttpClient(handler, disposeHandler: false));

        return new NpmInitialAdminBootstrapService(
            docker,
            probe,
            resolver,
            apiClient,
            credentialService,
            timeProvider,
            NullLogger<NpmInitialAdminBootstrapService>.Instance);
    }

    private static MemDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

    private static RecordingInstallationSecretStore SeededSecretStore(Guid installationId)
    {
        var store = new RecordingInstallationSecretStore();
        store.Seed(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminEmail,
            Email);
        store.Seed(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.NpmAdminPassword,
            Password);
        return store;
    }

    private static NpmInitialAdminBootstrapTarget Target() =>
        new(
            ContainerName: "mem-npm",
            Image: Image,
            HttpPort: 80,
            HttpsPort: 443,
            AdminPort: 81,
            DataVolumeName: "mem_npm_data",
            LetsEncryptVolumeName: "mem_npm_letsencrypt",
            NetworkName: "mem-gateway",
            NetworkAlias: "npm");

    private static DockerContainerInspection Inspection(
        string id,
        bool running,
        IReadOnlyList<string> environmentNames) =>
        new(
            Id: id,
            Name: "mem-npm",
            Image: Image,
            State: running ? "running" : "exited",
            Running: running,
            Ports: [])
        {
            EnvironmentVariableNames = environmentNames
        };

    private sealed class FixedAuthorityResolver : IMemManagedServiceAuthorityResolver
    {
        public MemManagedServiceAuthority Resolve(
            string purpose,
            MemManagedServiceAuthoritySource source) =>
            new(
                ServiceName: source.ServiceName,
                Purpose: purpose,
                Authority: new Uri("http://npm:81/"),
                RouteKind: MemManagedServiceRouteKinds.DockerNetwork);
    }

    private sealed record NpmSetupStateResponse(
        HttpStatusCode StatusCode,
        bool? Setup)
    {
        public static NpmSetupStateResponse Ready(bool setup) =>
            new(HttpStatusCode.OK, setup);

        public static NpmSetupStateResponse Http(HttpStatusCode statusCode) =>
            new(statusCode, null);
    }

    private sealed class NpmBootstrapHttpHandler : HttpMessageHandler
    {
        private readonly Queue<NpmSetupStateResponse> _setupStates;
        private readonly Queue<HttpStatusCode> _loginStatuses;

        public NpmBootstrapHttpHandler(
            IEnumerable<bool> setupStates,
            IEnumerable<HttpStatusCode> loginStatuses)
            : this(
                setupStates.Select(NpmSetupStateResponse.Ready),
                loginStatuses)
        {
        }

        public NpmBootstrapHttpHandler(
            IEnumerable<NpmSetupStateResponse> setupResponses,
            IEnumerable<HttpStatusCode> loginStatuses)
        {
            _setupStates = new Queue<NpmSetupStateResponse>(setupResponses);
            _loginStatuses = new Queue<HttpStatusCode>(loginStatuses);
        }

        public int SetupStateCalls { get; private set; }
        public int LoginCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Get && path == "/")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            if (request.Method == HttpMethod.Get && path == "/api")
            {
                SetupStateCalls++;
                if (_setupStates.Count == 0)
                    throw new InvalidOperationException("No NPM setup-state response remains in the test queue.");

                var response = _setupStates.Dequeue();
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return Task.FromResult(JsonResponse(
                        response.StatusCode,
                        "{\"error\":\"backend not ready\"}"));
                }

                if (!response.Setup.HasValue)
                    throw new InvalidOperationException("Successful setup-state response must include setup=true/false.");

                var setup = response.Setup.Value ? "true" : "false";
                return Task.FromResult(JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"status\":\"OK\",\"setup\":{setup}}}"));
            }

            if (request.Method == HttpMethod.Post && path == "/api/tokens")
            {
                LoginCalls++;
                if (_loginStatuses.Count == 0)
                    throw new InvalidOperationException("No NPM login response remains in the test queue.");

                var status = _loginStatuses.Dequeue();
                return Task.FromResult(status == HttpStatusCode.OK
                    ? JsonResponse(HttpStatusCode.OK, "{\"token\":\"test-token\"}")
                    : JsonResponse(status, "{\"error\":\"invalid credentials\"}"));
            }

            throw new InvalidOperationException(
                $"Unexpected NPM HTTP request: {request.Method} {request.RequestUri}");
        }

        private static HttpResponseMessage JsonResponse(
            HttpStatusCode status,
            string json) =>
            new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed class RecordingDockerHost : IDockerHost
    {
        private DockerContainerInspection? _current;
        private int _nextId = 1;

        public RecordingDockerHost(DockerContainerInspection? initial) =>
            _current = initial;

        public List<DockerContainerSpec> CreatedSpecs { get; } = [];
        public List<(string ContainerId, bool Force, bool RemoveVolumes)> Removals { get; } = [];
        public int GetLogsCalls { get; private set; }

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);
        public Task PullImageAsync(string image, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(string namePrefix, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<DockerContainerInspection?> InspectByNameAsync(
            string containerName,
            CancellationToken ct) =>
            Task.FromResult(_current);

        public Task EnsureNetworkAsync(string networkName, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) => Task.CompletedTask;
        public Task ConnectContainerToNetworkAsync(string containerIdOrName, string networkName, CancellationToken ct) => Task.CompletedTask;

        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct)
        {
            CreatedSpecs.Add(spec);
            var id = $"created-{_nextId++}";
            _current = new DockerContainerInspection(
                Id: id,
                Name: spec.Name,
                Image: spec.Image,
                State: "created",
                Running: false,
                Ports: [])
            {
                EnvironmentVariableNames = spec.Environment.Keys.ToArray()
            };
            return Task.FromResult(id);
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct)
        {
            Assert.NotNull(_current);
            _current = _current! with { State = "running", Running = true };
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct)
        {
            Assert.NotNull(_current);
            _current = _current! with { State = "exited", Running = false };
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(
            string containerId,
            bool force,
            bool removeVolumes,
            CancellationToken ct)
        {
            Removals.Add((containerId, force, removeVolumes));
            _current = null;
            return Task.CompletedTask;
        }

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

        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct)
        {
            GetLogsCalls++;
            return Task.FromResult(string.Empty);
        }
    }

    private sealed class RecordingInstallationSecretStore : IInstallationSecretStore
    {
        private readonly Dictionary<(Guid InstallationId, string Category, string Key), string> _values = [];

        public void Seed(Guid installationId, string category, string key, string value) =>
            _values[(installationId, category, key)] = value;

        public Task SetProtectedAsync(
            Guid installationId,
            string category,
            string key,
            string secret,
            string? description,
            CancellationToken cancellationToken)
        {
            _values[(installationId, category, key)] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ResolveProtectedAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue((installationId, category, key), out var value)
                ? value
                : null);

        public Task<bool> ExistsAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(_values.ContainsKey((installationId, category, key)));

        public Task DeleteAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken)
        {
            _values.Remove((installationId, category, key));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
