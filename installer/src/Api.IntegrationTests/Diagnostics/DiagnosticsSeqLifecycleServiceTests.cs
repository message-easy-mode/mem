using System.Security.Claims;
using Core.Runtime;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.RuntimeImages;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqLifecycleServiceTests
{
    [Fact]
    public async Task Reviewed_deploy_reuses_the_hardened_runtime_and_records_safe_evidence()
    {
        await using var fixture = await Fixture.CreateAsync(runtimePresent: false);

        var result = await fixture.Service.DeployAsync(
            Principal(),
            CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.Equal("seq.deploy", result.Operation);
        Assert.True(result.DataRetained);
        Assert.True(result.Overview.Runtime.Managed);
        Assert.True(result.Overview.Runtime.Running);
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(1, fixture.HealthVerifier.CallCount);
        Assert.StartsWith("sha256:", fixture.Docker.LastSpec!.Image);
        Assert.False(result.Overview.Runtime.PublishesPublicIngress);

        var operation = Assert.Single(fixture.Db.RuntimeOperations);
        Assert.Equal("seq.deploy", operation.Operation);
        Assert.Equal("succeeded", operation.Status);
        Assert.DoesNotContain(
            fixture.ApiKey,
            operation.ResultJson + operation.EvidenceJson + operation.InputJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            fixture.AdminPasswordHash,
            operation.ResultJson + operation.EvidenceJson + operation.InputJson,
            StringComparison.Ordinal);

        Assert.Equal(2, fixture.Writer.Requests.Count);
        Assert.All(fixture.Writer.Requests, request => Assert.False(request.CreateIncident));
        Assert.Equal(
            ["diagnostics.seq_operation_started", "diagnostics.seq_operation_completed"],
            fixture.Writer.Requests.Select(request => request.EventCode).ToArray());
    }

    [Fact]
    public async Task Start_and_restart_verify_the_owned_runtime_before_reporting_success()
    {
        await using var startFixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: false);

        var started = await startFixture.Service.StartAsync(
            Principal(),
            CancellationToken.None);

        Assert.Equal("succeeded", started.Status);
        Assert.True(started.Overview.Runtime.Running);
        Assert.Equal(1, startFixture.Docker.StartCount);
        Assert.Equal(1, startFixture.HealthVerifier.CallCount);

        await using var restartFixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: true);

        var restarted = await restartFixture.Service.RestartAsync(
            Principal(),
            CancellationToken.None);

        Assert.Equal("succeeded", restarted.Status);
        Assert.True(restarted.Overview.Runtime.Running);
        Assert.Equal(1, restartFixture.Docker.StopCount);
        Assert.Equal(1, restartFixture.Docker.StartCount);
        Assert.Equal(1, restartFixture.HealthVerifier.CallCount);
    }

    [Fact]
    public async Task Start_health_failure_is_recorded_as_a_failed_operation_and_safe_incident()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: false,
            healthPass: false);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.StartAsync(Principal(), CancellationToken.None));

        Assert.Equal("diagnostics.seq_health_failed", exception.Code);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.Equal(1, fixture.HealthVerifier.CallCount);
        var operation = Assert.Single(fixture.Db.RuntimeOperations);
        Assert.Equal("failed", operation.Status);
        Assert.Equal("diagnostics.seq_health_failed", operation.LastError);
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_operation_failed" &&
                       request.CreateIncident);
    }

    [Fact]
    public async Task Stop_is_confirmed_by_Docker_and_marks_the_state_intentional()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: true);

        var result = await fixture.Service.StopAsync(
            Principal(),
            CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.False(result.Overview.Runtime.Running);
        Assert.Equal(1, fixture.Docker.StopCount);
        Assert.Equal(0, fixture.HealthVerifier.CallCount);
        Assert.Equal("stopped-intentionally", fixture.HealthState.GetHealth().Status);
    }

    [Fact]
    public async Task Delivery_disable_is_staged_truthfully_until_the_API_restarts()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: true,
            sinkEnabled: true);

        var result = await fixture.Service.ChangeDeliveryAsync(
            Principal(),
            enabled: false,
            CancellationToken.None);

        Assert.True(result.Overview.Delivery.Enabled);
        Assert.False(result.Overview.Delivery.DesiredEnabled);
        Assert.True(result.Overview.Delivery.RestartRequired);
        Assert.Equal("disable-pending-restart", result.Overview.Delivery.ConfigurationState);
        Assert.Equal(0, fixture.Docker.StartCount);
        Assert.Equal(0, fixture.Docker.StopCount);
        Assert.Equal(0, fixture.Docker.RemoveCount);
        Assert.Equal(false, fixture.DeliveryStore.LastDesiredValue);
    }

    [Fact]
    public async Task Delivery_enable_requires_a_running_healthy_managed_runtime()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: true,
            sinkEnabled: false);

        var result = await fixture.Service.ChangeDeliveryAsync(
            Principal(),
            enabled: true,
            CancellationToken.None);

        Assert.False(result.Overview.Delivery.Enabled);
        Assert.True(result.Overview.Delivery.DesiredEnabled);
        Assert.True(result.Overview.Delivery.RestartRequired);
        Assert.Equal("enable-pending-restart", result.Overview.Delivery.ConfigurationState);
        Assert.Equal(1, fixture.HealthVerifier.CallCount);
        Assert.Equal(true, fixture.DeliveryStore.LastDesiredValue);
    }

    [Fact]
    public async Task Delivery_enable_is_blocked_until_the_dedicated_connection_is_verified()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: true,
            sinkEnabled: false,
            connectionVerified: false);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.ChangeDeliveryAsync(
                Principal(),
                enabled: true,
                CancellationToken.None));

        Assert.Equal("seq_connection_not_verified", exception.Code);
        Assert.Null(fixture.DeliveryStore.LastDesiredValue);
    }

    [Fact]
    public async Task Removal_is_blocked_until_effective_and_desired_delivery_are_disabled()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: false,
            sinkEnabled: true);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.RemoveAsync(Principal(), CancellationToken.None));

        Assert.Equal("seq_delivery_must_be_disabled", exception.Code);
        Assert.Equal(0, fixture.Docker.RemoveCount);
        Assert.True(await fixture.Db.RuntimeServices.AnyAsync());
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_operation_failed" &&
                       request.CreateIncident);
    }

    [Fact]
    public async Task Removal_deletes_only_the_managed_container_record_and_retains_data()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: false,
            sinkEnabled: false);
        var marker = Path.Combine(fixture.Options.HostDataPath, "retained.marker");
        Directory.CreateDirectory(fixture.Options.HostDataPath);
        await File.WriteAllTextAsync(marker, "retain");

        var result = await fixture.Service.RemoveAsync(
            Principal(),
            CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.True(result.DataRetained);
        Assert.Equal(1, fixture.Docker.RemoveCount);
        Assert.False(fixture.Docker.LastRemoveVolumes);
        Assert.False(await fixture.Db.RuntimeServices.AnyAsync());
        Assert.True(File.Exists(marker));
        Assert.Equal("runtime-absent", fixture.HealthState.GetHealth().Status);
    }

    [Fact]
    public async Task Concurrent_requests_are_rejected_before_a_second_operation_starts()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimePresent: true,
            runtimeRunning: true,
            sinkEnabled: false,
            blockingHealth: true);

        var first = fixture.Service.CheckHealthAsync(
            Principal(),
            CancellationToken.None);
        await fixture.BlockingHealthVerifier!.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.CheckHealthAsync(Principal(), CancellationToken.None));
        Assert.Equal("seq_operation_in_progress", exception.Code);

        fixture.BlockingHealthVerifier.Release.TrySetResult(true);
        await first;
        Assert.Single(fixture.Db.RuntimeOperations);
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Name, "platform-owner"),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        "test"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _root;
        private readonly string _apiKeyVariable;
        private readonly string _adminHashVariable;

        private Fixture(
            SqliteConnection connection,
            string root,
            string apiKeyVariable,
            string adminHashVariable,
            MemDbContext db,
            SeqDiagnosticsOptions options,
            FakeDockerHost docker,
            RecordingImageInspector imageInspector,
            RecordingHealthVerifier healthVerifier,
            BlockingHealthVerifier? blockingHealthVerifier,
            RecordingDeliveryStateStore deliveryStore,
            SeqHealthState healthState,
            RecordingDiagnosticWriter writer,
            DiagnosticsSeqLifecycleService service,
            string apiKey,
            string adminPasswordHash)
        {
            _connection = connection;
            _root = root;
            _apiKeyVariable = apiKeyVariable;
            _adminHashVariable = adminHashVariable;
            Db = db;
            Options = options;
            Docker = docker;
            ImageInspector = imageInspector;
            HealthVerifier = healthVerifier;
            BlockingHealthVerifier = blockingHealthVerifier;
            DeliveryStore = deliveryStore;
            HealthState = healthState;
            Writer = writer;
            Service = service;
            ApiKey = apiKey;
            AdminPasswordHash = adminPasswordHash;
        }

        public MemDbContext Db { get; }
        public SeqDiagnosticsOptions Options { get; }
        public FakeDockerHost Docker { get; }
        public RecordingImageInspector ImageInspector { get; }
        public RecordingHealthVerifier HealthVerifier { get; }
        public BlockingHealthVerifier? BlockingHealthVerifier { get; }
        public RecordingDeliveryStateStore DeliveryStore { get; }
        public SeqHealthState HealthState { get; }
        public RecordingDiagnosticWriter Writer { get; }
        public DiagnosticsSeqLifecycleService Service { get; }
        public string ApiKey { get; }
        public string AdminPasswordHash { get; }

        public static async Task<Fixture> CreateAsync(
            bool runtimePresent,
            bool runtimeRunning = false,
            bool sinkEnabled = false,
            bool blockingHealth = false,
            bool healthPass = true,
            bool connectionVerified = true)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-seq-lifecycle-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var apiKeyVariable = $"MEM_SEQ_API_{Guid.NewGuid():N}";
            var adminHashVariable = $"MEM_SEQ_ADMIN_{Guid.NewGuid():N}";
            const string apiKey = "test-seq-api-key";
            const string adminHash = "test-seq-admin-password-hash";
            Environment.SetEnvironmentVariable(apiKeyVariable, apiKey);
            Environment.SetEnvironmentVariable(adminHashVariable, adminHash);

            var options = new SeqDiagnosticsOptions
            {
                SinkEnabled = sinkEnabled,
                ManagementEnabled = true,
                EulaAccepted = true,
                IngestionUrl = "http://seq:5341",
                HealthUrl = "http://seq:80",
                UiUrl = "https://seq.example.test/",
                ApiKeyEnvironmentVariableName = apiKeyVariable,
                AdminPasswordHashEnvironmentVariableName = adminHashVariable,
                AdminPasswordHashFilePath = Path.Combine(
                    root,
                    "seq-secrets",
                    "admin-password-hash"),
                HostDataPath = Path.Combine(root, "seq-data"),
                DeliveryStatePath = Path.Combine(root, "seq-delivery.json"),
                BootstrapStatePath = Path.Combine(root, "seq-bootstrap.json"),
                SecretRootPath = Path.Combine(root, "seq-secrets"),
                ApiKeyFilePath = Path.Combine(root, "seq-secrets", "ingestion-api-key"),
                PreferredHostPort = 25341
            };
            var environment = new TestHostEnvironment(root);
            var secrets = new SeqSecretResolver(environment);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();

            var imageInspector = new RecordingImageInspector();
            var docker = new FakeDockerHost(
                runtimePresent
                    ? new DockerContainerInspection(
                        "container-seq",
                        "mem-seq",
                        imageInspector.ImageId,
                        runtimeRunning ? "running" : "exited",
                        runtimeRunning,
                        [new DockerPortBinding(80, 25341, "tcp", "127.0.0.1")])
                    : null);
            if (runtimePresent)
            {
                db.RuntimeServices.Add(new RuntimeServiceEntity
                {
                    Id = Guid.NewGuid(),
                    ServiceName = "seq",
                    ContainerName = "mem-seq",
                    Image = imageInspector.ImageId,
                    ContainerPort = 80,
                    PreferredHostPort = 25341,
                    SelectedHostPort = 25341,
                    HostPath = options.HostDataPath,
                    ContainerId = "container-seq",
                    Status = runtimeRunning ? "running" : "exited",
                    CreatedAtUtc = DateTime.UtcNow,
                    LastObservedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            var runtimeService = new SeqRuntimeService(
                docker,
                new RuntimePortPlanner(new PortCheckService()),
                db,
                options,
                secrets,
                new SeqRuntimeImageResolver(options, imageInspector));
            var bootstrapStateStore = new SeqBootstrapStateStore(options, environment);
            await bootstrapStateStore.WriteAsync(
                new SeqBootstrapState(
                    ManagementEnabled: true,
                    EulaAccepted: true,
                    SelectedHostPort: 25341,
                    SetupStage: connectionVerified ? "connected" : "runtime-verified",
                    RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow,
                    IngestionCredentialState: connectionVerified ? "available" : "absent",
                    IngestionCredentialId: connectionVerified ? "api-key-test" : null,
                    DeliveryVerifiedAtUtc: connectionVerified ? DateTimeOffset.UtcNow : null,
                    LastDeliveryVerificationId: connectionVerified ? "verification-test" : null,
                    LastDeliveryVerificationEventId: connectionVerified ? "event-test" : null),
                CancellationToken.None);
            var effectiveProvider = new SeqEffectiveConfigurationProvider(options, bootstrapStateStore);
            var deliveryStore = new RecordingDeliveryStateStore(sinkEnabled);
            var healthState = new SeqHealthState(options, secrets);
            var normalVerifier = new RecordingHealthVerifier(healthPass);
            BlockingHealthVerifier? blockingVerifier = blockingHealth
                ? new BlockingHealthVerifier()
                : null;
            ISeqRuntimeHealthVerifier verifier = blockingVerifier is not null
                ? blockingVerifier
                : normalVerifier;
            var diagnosticsService = new DiagnosticsSeqService(
                options,
                secrets,
                healthState,
                runtimeService,
                imageInspector,
                TimeProvider.System,
                deliveryStore,
                effectiveProvider,
                environment,
                bootstrapStateStore);
            var writer = new RecordingDiagnosticWriter();
            var service = new DiagnosticsSeqLifecycleService(
                runtimeService,
                diagnosticsService,
                verifier,
                deliveryStore,
                options,
                secrets,
                healthState,
                db,
                writer,
                TimeProvider.System,
                effectiveProvider,
                bootstrapStateStore);

            return new Fixture(
                connection,
                root,
                apiKeyVariable,
                adminHashVariable,
                db,
                options,
                docker,
                imageInspector,
                normalVerifier,
                blockingVerifier,
                deliveryStore,
                healthState,
                writer,
                service,
                apiKey,
                adminHash);
        }

        public async ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(_apiKeyVariable, null);
            Environment.SetEnvironmentVariable(_adminHashVariable, null);
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private sealed class RecordingDeliveryStateStore(bool effectiveEnabled)
        : ISeqDeliveryStateStore
    {
        private bool _desiredEnabled = effectiveEnabled;

        public bool? LastDesiredValue { get; private set; }

        public SeqDeliveryState GetState() => new(
            EffectiveEnabled: effectiveEnabled,
            DesiredEnabled: _desiredEnabled,
            RestartRequired: effectiveEnabled != _desiredEnabled,
            UpdatedAtUtc: LastDesiredValue.HasValue ? DateTimeOffset.UtcNow : null,
            WarningCode: null);

        public Task<SeqDeliveryState> SetDesiredAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            LastDesiredValue = enabled;
            _desiredEnabled = enabled;
            return Task.FromResult(GetState());
        }
    }

    private sealed class RecordingHealthVerifier(bool passed) : ISeqRuntimeHealthVerifier
    {
        public int CallCount { get; private set; }

        public Task<SeqRuntimeHealthVerification> VerifyAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new SeqRuntimeHealthVerification(
                Passed: passed,
                Status: passed ? "healthy" : "failed",
                ObservedAtUtc: DateTimeOffset.UtcNow,
                WarningCode: passed ? null : "diagnostics.seq_health_failed"));
        }
    }

    private sealed class BlockingHealthVerifier : ISeqRuntimeHealthVerifier
    {
        public TaskCompletionSource<bool> Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<SeqRuntimeHealthVerification> VerifyAsync(
            CancellationToken cancellationToken)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new SeqRuntimeHealthVerification(
                Passed: true,
                Status: "healthy",
                ObservedAtUtc: DateTimeOffset.UtcNow,
                WarningCode: null);
        }
    }

    private sealed class RecordingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_{Guid.NewGuid():N}",
                IncidentId: request.CreateIncident ? $"inc_{Guid.NewGuid():N}" : null,
                WarningCode: null));
        }
    }

    private sealed class RecordingImageInspector : IRuntimeImageInspector
    {
        public string ImageId { get; } = $"sha256:{new string('a', 64)}";
        public int PullCount { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeImageInspection?>(new RuntimeImageInspection(
                ImageId,
                [$"datalust/seq@sha256:{new string('b', 64)}"],
                []));

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PullCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDockerHost(DockerContainerInspection? inspection)
        : IDockerHost
    {
        private DockerContainerInspection? _inspection = inspection;

        public int CreateCount { get; private set; }
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool LastRemoveVolumes { get; private set; }
        public DockerContainerSpec? LastSpec { get; private set; }

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);
        public Task PullImageAsync(string image, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(string namePrefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<DockerContainerInspection?> InspectByNameAsync(string containerName, CancellationToken ct) => Task.FromResult(_inspection);
        public Task EnsureNetworkAsync(string networkName, CancellationToken ct) => Task.CompletedTask;

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) => Task.CompletedTask;
        public Task ConnectContainerToNetworkAsync(string containerIdOrName, string networkName, CancellationToken ct) => Task.CompletedTask;
        public Task CopyFileToContainerAsync(string containerIdOrName, string destinationDirectory, string fileName, ReadOnlyMemory<byte> content, UnixFileMode mode, CancellationToken ct) => Task.CompletedTask;
        public Task<DockerExecResult> ExecAsync(string containerIdOrName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken ct) => Task.FromResult(new DockerExecResult(0, string.Empty, string.Empty, false));

        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct)
        {
            CreateCount++;
            LastSpec = spec;
            _inspection = new DockerContainerInspection(
                "container-seq",
                spec.Name,
                spec.Image,
                "created",
                false,
                [new DockerPortBinding(80, 25341, "tcp", "127.0.0.1")]);
            return Task.FromResult("container-seq");
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct)
        {
            StartCount++;
            if (_inspection is not null)
            {
                _inspection = _inspection with { State = "running", Running = true };
            }
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct)
        {
            StopCount++;
            if (_inspection is not null)
            {
                _inspection = _inspection with { State = "exited", Running = false };
            }
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(
            string containerId,
            bool force,
            bool removeVolumes,
            CancellationToken ct)
        {
            RemoveCount++;
            LastRemoveVolumes = removeVolumes;
            _inspection = null;
            return Task.CompletedTask;
        }

        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
