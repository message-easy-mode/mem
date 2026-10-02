using Core.Runtime;
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
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.RuntimeImages;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqBootstrapOperationProcessorTests
{
    [Fact]
    public async Task Clean_setup_deploys_connects_verifies_and_prepares_event_delivery()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: true);
        var workItem = await fixture.CreateWorkItemAsync();
        const string plaintextPassword = "Correct-Horse-Battery-42";

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        var state = fixture.StateStore.Read().State;
        var secret = await File.ReadAllTextAsync(fixture.Options.AdminPasswordHashFilePath!);

        Assert.Equal("succeeded", operation.Status);
        Assert.Equal("completed", operation.CurrentStep);
        Assert.All(
            DiagnosticsSeqBootstrapExecutionService.ReadProgress(operation.EvidenceJson).Checks,
            check => Assert.Equal("passed", check.Status));
        Assert.Equal(1, fixture.ImageInspector.PullCount);
        Assert.Equal(1, fixture.CommandRunner.CallCount);
        Assert.Equal(plaintextPassword, fixture.CommandRunner.StandardInput);
        Assert.DoesNotContain(plaintextPassword, fixture.CommandRunner.Arguments);
        Assert.DoesNotContain(plaintextPassword, operation.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(plaintextPassword, operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal("generated-seq-password-hash", secret);
        Assert.True(Directory.Exists(fixture.Options.HostDataPath));
        Assert.NotNull(state);
        Assert.True(state!.ManagementEnabled);
        Assert.True(state.EulaAccepted);
        Assert.Equal("completed", state.SetupStage);
        Assert.NotNull(state.RuntimeVerifiedAtUtc);
        Assert.Equal(workItem.OperationId, state.LastOperationId);
        Assert.Equal("available", state.IngestionCredentialState);
        Assert.NotNull(state.DeliveryVerifiedAtUtc);
        Assert.Equal(1, fixture.ConnectionProvisioner.CallCount);
        Assert.Equal(plaintextPassword, fixture.ConnectionProvisioner.LastPassword);
        Assert.True(fixture.DeliveryStore.DesiredEnabled);
        Assert.Equal(1, fixture.DeliveryStore.SetCount);
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.NotNull(fixture.Docker.LastSpec);
        Assert.StartsWith("sha256:", fixture.Docker.LastSpec!.Image, StringComparison.Ordinal);
        Assert.Equal("Y", fixture.Docker.LastSpec.Environment["ACCEPT_EULA"]);
        Assert.Equal(
            "generated-seq-password-hash",
            fixture.Docker.LastSpec.Environment["SEQ_FIRSTRUN_ADMINPASSWORDHASH"]);
        Assert.Equal(
            "true",
            fixture.Docker.LastSpec.Environment["SEQ_API_INGESTIONREQUIREAUTHENTICATION"]);
        Assert.DoesNotContain(plaintextPassword, fixture.Docker.LastSpec.Environment.Values);
        Assert.Single(await fixture.Db.RuntimeServices.ToListAsync());
        Assert.Equal(1, fixture.HealthVerifier.CallCount);
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_bootstrap_completed" &&
                       !request.CreateIncident);
    }

    [Fact]
    public async Task Relative_development_storage_path_is_resolved_consistently_for_Docker()
    {
        await using var fixture = await Fixture.CreateAsync(
            healthPass: true,
            useRelativeHostDataPath: true);
        var workItem = await fixture.CreateWorkItemAsync();

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var mount = Assert.Single(fixture.Docker.LastSpec!.BindMounts);
        var runtime = await fixture.Db.RuntimeServices.SingleAsync();
        Assert.Equal(fixture.ResolvedHostDataPath, mount.HostPath);
        Assert.Equal(fixture.ResolvedHostDataPath, runtime.HostPath);
        Assert.True(Directory.Exists(fixture.ResolvedHostDataPath));
        Assert.Equal("succeeded", (await fixture.Db.RuntimeOperations.SingleAsync()).Status);
    }

    [Fact]
    public async Task Unavailable_storage_path_reports_the_storage_stage_and_safe_problem_code()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: true);
        var blockedParent = Path.Combine(
            Path.GetDirectoryName(fixture.Options.HostDataPath)!,
            "blocked-parent");
        await File.WriteAllTextAsync(blockedParent, "not-a-directory");
        fixture.Options.HostDataPath = Path.Combine(blockedParent, "seq-data");
        var workItem = await fixture.CreateWorkItemAsync();

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            operation.EvidenceJson);

        Assert.Equal("failed", operation.Status);
        Assert.Equal("seq_data_path_unavailable", operation.LastError);
        Assert.Contains(
            progress.Checks,
            check => check.Code == "storage" &&
                     check.Status == "failed" &&
                     check.WarningCode == "seq_data_path_unavailable");
        Assert.Equal(0, fixture.CommandRunner.CallCount);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_bootstrap_failed" &&
                       request.CreateIncident &&
                       request.Details!["warningCode"] == "seq_data_path_unavailable");
    }

    [Fact]
    public async Task Initialized_data_without_its_existing_secret_fails_before_hashing_or_secret_write()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: true);
        Directory.CreateDirectory(fixture.Options.HostDataPath);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Options.HostDataPath, "seq.json"),
            "initialized");
        await fixture.StateStore.WriteAsync(
            new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SetupStage: "failed:verifying-health"),
            CancellationToken.None);
        var workItem = await fixture.CreateWorkItemAsync();

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.Equal("failed", operation.Status);
        Assert.Equal(
            "seq_initialized_data_requires_existing_administrator_secret",
            operation.LastError);
        Assert.Equal(0, fixture.CommandRunner.CallCount);
        Assert.False(File.Exists(fixture.Options.AdminPasswordHashFilePath!));
        Assert.Equal(0, fixture.Docker.CreateCount);
    }

    [Fact]
    public async Task Retry_uses_the_existing_administrator_secret_without_rehashing_or_overwriting_it()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: true);
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.Options.AdminPasswordHashFilePath!)!);
        await File.WriteAllTextAsync(
            fixture.Options.AdminPasswordHashFilePath!,
            "existing-seq-password-hash");
        var workItem = await fixture.CreateWorkItemAsync(
            password: string.Empty,
            administratorPasswordRequired: false,
            connectionPassword: "Current-Seq-Password");

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var secret = await File.ReadAllTextAsync(fixture.Options.AdminPasswordHashFilePath!);
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            (await fixture.Db.RuntimeOperations.SingleAsync()).EvidenceJson);
        Assert.Equal("existing-seq-password-hash", secret);
        Assert.Equal(0, fixture.CommandRunner.CallCount);
        Assert.Contains(progress.Checks, check => check.Code == "password_hash" && check.Status == "passed");
        Assert.Contains(progress.Checks, check => check.Code == "administrator_secret" && check.Status == "passed");
        Assert.Equal(
            "existing-seq-password-hash",
            fixture.Docker.LastSpec!.Environment["SEQ_FIRSTRUN_ADMINPASSWORDHASH"]);
        Assert.Equal("Current-Seq-Password", fixture.ConnectionProvisioner.LastPassword);
    }

    [Fact]
    public async Task Changed_server_policy_after_review_fails_before_any_host_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: true);
        var workItem = await fixture.CreateWorkItemAsync();
        fixture.Options.ExpectedVersion = "2026.1.99999";

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.Equal("failed", operation.Status);
        Assert.Equal("seq_bootstrap_review_stale", operation.LastError);
        Assert.Equal(0, fixture.ImageInspector.PullCount);
        Assert.Equal(0, fixture.CommandRunner.CallCount);
        Assert.Equal(0, fixture.Docker.CreateCount);
        Assert.False(Directory.Exists(fixture.Options.HostDataPath));
    }

    [Fact]
    public async Task Startup_health_is_retried_within_the_configured_bounded_attempts()
    {
        await using var fixture = await Fixture.CreateAsync(
            healthPass: true,
            bootstrapHealthAttemptCount: 3,
            healthPassAfterCall: 2,
            bootstrapHealthPollIntervalSeconds: 1);
        var workItem = await fixture.CreateWorkItemAsync();

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        Assert.Equal("succeeded", (await fixture.Db.RuntimeOperations.SingleAsync()).Status);
        Assert.Equal(2, fixture.HealthVerifier.CallCount);
    }

    [Fact]
    public async Task Health_failure_leaves_the_managed_runtime_and_data_recoverable_but_does_not_claim_success()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: false);
        var workItem = await fixture.CreateWorkItemAsync();
        const string markerName = "retain.marker";

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.Options.HostDataPath, markerName),
            "retain");

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        var state = fixture.StateStore.Read().State;
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            operation.EvidenceJson);

        Assert.Equal("failed", operation.Status);
        Assert.Equal("diagnostics.seq_health_failed", operation.LastError);
        Assert.Equal(0, fixture.ConnectionProvisioner.CallCount);
        Assert.Equal(0, fixture.DeliveryStore.SetCount);
        Assert.Contains(
            progress.Checks,
            check => check.Code == "seq_health" &&
                     check.Status == "failed" &&
                     check.WarningCode == "diagnostics.seq_health_failed");
        Assert.NotNull(state);
        Assert.StartsWith("failed:verifying-health", state!.SetupStage, StringComparison.Ordinal);
        Assert.Null(state.RuntimeVerifiedAtUtc);
        Assert.True(File.Exists(Path.Combine(fixture.Options.HostDataPath, markerName)));
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(0, fixture.Docker.RemoveCount);
        Assert.Single(await fixture.Db.RuntimeServices.ToListAsync());
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_bootstrap_failed" &&
                       request.CreateIncident);
    }

    [Fact]
    public async Task Operator_can_leave_event_delivery_disabled_after_verified_connection()
    {
        await using var fixture = await Fixture.CreateAsync(healthPass: true);
        var workItem = await fixture.CreateWorkItemAsync(enableEventDelivery: false);

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            operation.EvidenceJson);
        var state = fixture.StateStore.Read().State;

        Assert.Equal("succeeded", operation.Status);
        Assert.Equal(1, fixture.ConnectionProvisioner.CallCount);
        Assert.Equal(0, fixture.DeliveryStore.SetCount);
        Assert.Contains(
            progress.Checks,
            check => check.Code == "ingestion_connection" && check.Status == "passed");
        Assert.Contains(
            progress.Checks,
            check => check.Code == "event_delivery" && check.Status == "skipped");
        Assert.Equal("available", state!.IngestionCredentialState);
        Assert.NotNull(state.DeliveryVerifiedAtUtc);
    }

    [Fact]
    public async Task Connection_failure_after_health_preserves_the_runtime_and_reports_attention()
    {
        await using var fixture = await Fixture.CreateAsync(
            healthPass: true,
            connectionPass: false);
        var workItem = await fixture.CreateWorkItemAsync();

        await fixture.Processor.ProcessAsync(workItem, CancellationToken.None);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        var progress = DiagnosticsSeqBootstrapExecutionService.ReadProgress(
            operation.EvidenceJson);
        var state = fixture.StateStore.Read().State;

        Assert.Equal("attention", operation.Status);
        Assert.Equal("seq_connection_verification_failed", operation.LastError);
        Assert.Equal(1, fixture.Docker.CreateCount);
        Assert.Equal(1, fixture.Docker.StartCount);
        Assert.Equal(1, fixture.ConnectionProvisioner.CallCount);
        Assert.Equal(0, fixture.DeliveryStore.SetCount);
        Assert.NotNull(state!.RuntimeVerifiedAtUtc);
        Assert.StartsWith("attention:", state.SetupStage, StringComparison.Ordinal);
        Assert.Contains(
            progress.Checks,
            check => check.Code == "seq_health" && check.Status == "passed");
        Assert.Contains(
            progress.Checks,
            check => check.Code == "ingestion_connection" &&
                     check.Status == "failed" &&
                     check.WarningCode == "seq_connection_verification_failed");
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_bootstrap_needs_attention" &&
                       request.CreateIncident);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _root;

        private Fixture(
            SqliteConnection connection,
            string root,
            MemDbContext db,
            SeqDiagnosticsOptions options,
            SeqBootstrapStateStore stateStore,
            RecordingImageInspector imageInspector,
            RecordingDockerRunner commandRunner,
            FakeDockerHost docker,
            RecordingHealthVerifier healthVerifier,
            RecordingConnectionProvisioner connectionProvisioner,
            RecordingDeliveryStateStore deliveryStore,
            RecordingDiagnosticWriter writer,
            DiagnosticsSeqBootstrapOperationProcessor processor,
            string resolvedHostDataPath)
        {
            _connection = connection;
            _root = root;
            Db = db;
            Options = options;
            StateStore = stateStore;
            ImageInspector = imageInspector;
            CommandRunner = commandRunner;
            Docker = docker;
            HealthVerifier = healthVerifier;
            ConnectionProvisioner = connectionProvisioner;
            DeliveryStore = deliveryStore;
            Writer = writer;
            Processor = processor;
            ResolvedHostDataPath = resolvedHostDataPath;
        }

        public MemDbContext Db { get; }
        public SeqDiagnosticsOptions Options { get; }
        public SeqBootstrapStateStore StateStore { get; }
        public RecordingImageInspector ImageInspector { get; }
        public RecordingDockerRunner CommandRunner { get; }
        public FakeDockerHost Docker { get; }
        public RecordingHealthVerifier HealthVerifier { get; }
        public RecordingConnectionProvisioner ConnectionProvisioner { get; }
        public RecordingDeliveryStateStore DeliveryStore { get; }
        public RecordingDiagnosticWriter Writer { get; }
        public DiagnosticsSeqBootstrapOperationProcessor Processor { get; }
        public string ResolvedHostDataPath { get; }

        public static async Task<Fixture> CreateAsync(
            bool healthPass,
            int bootstrapHealthAttemptCount = 1,
            int healthPassAfterCall = 1,
            int bootstrapHealthPollIntervalSeconds = 1,
            bool useRelativeHostDataPath = false,
            bool connectionPass = true)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-seq-bootstrap-operation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var contentRoot = useRelativeHostDataPath
                ? Path.Combine(root, "installer", "src", "Api")
                : root;
            Directory.CreateDirectory(contentRoot);
            var configuredHostDataPath = useRelativeHostDataPath
                ? "../../data/seq"
                : Path.Combine(root, "seq-data");
            var resolvedHostDataPath = Path.GetFullPath(
                Path.Combine(contentRoot, configuredHostDataPath));
            var options = new SeqDiagnosticsOptions
            {
                SinkEnabled = false,
                ManagementEnabled = false,
                EulaAccepted = false,
                AllowSetupPull = true,
                AllowOperationalPull = false,
                IngestionUrl = "http://seq:5341",
                HealthUrl = "http://seq:80",
                UiUrl = null,
                ApiKeyEnvironmentVariableName = string.Empty,
                ApiKeyFilePath = Path.Combine(root, "secrets", "ingestion-api-key"),
                AdminPasswordHashEnvironmentVariableName = string.Empty,
                AdminPasswordHashFilePath = Path.Combine(root, "secrets", "admin-password-hash"),
                ApprovedImageReference = "datalust/seq:2026.1.17044",
                ExpectedVersion = "2026.1.17044",
                HostDataPath = configuredHostDataPath,
                PreferredHostPort = 25341,
                ProbeTimeoutSeconds = 5,
                ProbeIntervalSeconds = 30,
                PasswordHashTimeoutSeconds = 30,
                BootstrapHealthAttemptCount = bootstrapHealthAttemptCount,
                BootstrapHealthPollIntervalSeconds = bootstrapHealthPollIntervalSeconds,
                SecretRootPath = Path.Combine(root, "secrets"),
                BootstrapStatePath = Path.Combine(root, "diagnostics", "seq-bootstrap.json"),
                DeliveryStatePath = Path.Combine(root, "diagnostics", "seq-delivery.json")
            };
            var environment = new TestHostEnvironment(contentRoot);
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options);
            await db.Database.EnsureCreatedAsync();

            var stateStore = new SeqBootstrapStateStore(options, environment);
            var imageInspector = new RecordingImageInspector();
            var imageResolver = new SeqRuntimeImageResolver(options, imageInspector);
            var commandRunner = new RecordingDockerRunner();
            var passwordHashService = new SeqPasswordHashService(
                options,
                imageResolver,
                commandRunner);
            var secretWriter = new SeqSecretFileWriter(options, environment);
            var storageInspector = new SeqBootstrapStorageInspector(
                options,
                stateStore,
                environment);
            var docker = new FakeDockerHost();
            var secretResolver = new SeqSecretResolver(environment);
            var runtimeService = new SeqRuntimeService(
                docker,
                new RuntimePortPlanner(new PortCheckService()),
                db,
                options,
                secretResolver,
                imageResolver,
                new SeqEffectiveConfigurationProvider(options, stateStore),
                environment);
            var healthVerifier = new RecordingHealthVerifier(
                healthPass,
                healthPassAfterCall);
            var connectionProvisioner = new RecordingConnectionProvisioner(
                stateStore,
                connectionPass);
            var deliveryStore = new RecordingDeliveryStateStore();
            var writer = new RecordingDiagnosticWriter();
            var processor = new DiagnosticsSeqBootstrapOperationProcessor(
                db,
                imageResolver,
                passwordHashService,
                secretResolver,
                secretWriter,
                options,
                storageInspector,
                stateStore,
                runtimeService,
                healthVerifier,
                connectionProvisioner,
                deliveryStore,
                writer,
                TimeProvider.System);

            return new Fixture(
                connection,
                root,
                db,
                options,
                stateStore,
                imageInspector,
                commandRunner,
                docker,
                healthVerifier,
                connectionProvisioner,
                deliveryStore,
                writer,
                processor,
                resolvedHostDataPath);
        }

        public async Task<SeqBootstrapWorkItem> CreateWorkItemAsync(
            string password = "Correct-Horse-Battery-42",
            bool administratorPasswordRequired = true,
            string connectionPassword = "",
            bool enableEventDelivery = true)
        {
            var operation = new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                Operation = "seq.bootstrap",
                Status = "queued",
                RequestedBy = "platform-owner",
                RequestedByUserId = Guid.NewGuid(),
                RequestedAtUtc = DateTime.UtcNow,
                CurrentStep = "reviewed",
                AttemptCount = 1,
                LockedUntilUtc = DateTime.UtcNow.AddMinutes(30),
                EvidenceJson = System.Text.Json.JsonSerializer.Serialize(
                    DiagnosticsSeqBootstrapExecutionService.InitialProgress()),
                HostMutationLevel = "container-create",
                RequiresConfirmation = true,
                ConfirmedAtUtc = DateTime.UtcNow
            };
            Db.RuntimeOperations.Add(operation);
            await Db.SaveChangesAsync();
            var passwordCharacters = password.ToCharArray();
            return new SeqBootstrapWorkItem(
                operation.Id,
                new SeqBootstrapReviewSnapshot(
                    $"seq_bootstrap_review_{Guid.NewGuid():N}",
                    TimeProvider.System.GetUtcNow(),
                    TimeProvider.System.GetUtcNow().AddMinutes(10),
                    AcceptEula: true,
                    PrivateUiUrl: null,
                    SelectedHostPort: Options.PreferredHostPort,
                    ApprovedImageReference: Options.ApprovedImageReference,
                    ExpectedVersion: Options.ExpectedVersion,
                    StorageState: "ready-to-create",
                    RuntimeOwnershipState: "absent",
                    RuntimeExists: false,
                    RuntimeManaged: false,
                    AdministratorPasswordRequired: administratorPasswordRequired,
                    EnableEventDelivery: enableEventDelivery,
                    CurrentAdministratorPasswordRequired: !administratorPasswordRequired),
                passwordCharacters,
                operation.RequestedByUserId,
                operation.RequestedBy!,
                connectionPassword.ToCharArray());
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }

    private sealed class RecordingImageInspector : IRuntimeImageInspector
    {
        private RuntimeImageInspection? _image;

        public string ImageId { get; } = $"sha256:{new string('a', 64)}";
        public int PullCount { get; private set; }

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.FromResult(_image);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            PullCount++;
            _image = new RuntimeImageInspection(
                ImageId,
                [$"datalust/seq@sha256:{new string('b', 64)}"],
                []);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDockerRunner : IDockerIsolatedStandardInputRunner
    {
        public int CallCount { get; private set; }
        public string StandardInput { get; private set; } = string.Empty;
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<DockerIsolatedStandardInputResult> RunAsync(
            string image,
            string containerName,
            IReadOnlyList<string> command,
            ReadOnlyMemory<char> standardInput,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            CallCount++;
            StandardInput = standardInput.ToString();
            Arguments = command.ToArray();
            return Task.FromResult(new DockerIsolatedStandardInputResult(
                0,
                "generated-seq-password-hash",
                string.Empty,
                TimedOut: false));
        }
    }

    private sealed class RecordingHealthVerifier(
        bool eventuallyPasses,
        int passAfterCall) : ISeqRuntimeHealthVerifier
    {
        public int CallCount { get; private set; }

        public Task<SeqRuntimeHealthVerification> VerifyAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            var passed = eventuallyPasses && CallCount >= passAfterCall;
            return Task.FromResult(new SeqRuntimeHealthVerification(
                Passed: passed,
                Status: passed ? "healthy" : "failed",
                ObservedAtUtc: TimeProvider.System.GetUtcNow(),
                WarningCode: passed ? null : "diagnostics.seq_health_failed"));
        }
    }

    private sealed class RecordingConnectionProvisioner(
        ISeqBootstrapStateStore stateStore,
        bool passes) : ISeqConnectionProvisioner
    {
        public int CallCount { get; private set; }
        public string? LastPassword { get; private set; }

        public async Task<SeqConnectionProvisioningResult> ProvisionAndVerifyAsync(
            string administratorPassword,
            Guid operationId,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastPassword = administratorPassword;
            if (!passes)
            {
                throw new SeqOperationException(
                    "seq_connection_verification_failed",
                    Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                    "The connection verification failed.");
            }

            var verifiedAt = TimeProvider.System.GetUtcNow();
            var previous = stateStore.Read().State ?? new SeqBootstrapState();
            await stateStore.WriteAsync(
                previous with
                {
                    SetupStage = "connected",
                    IngestionCredentialState = "available",
                    IngestionCredentialId = "api-key-1",
                    DeliveryVerifiedAtUtc = verifiedAt,
                    LastDeliveryVerificationId = "seq-connect-test",
                    LastDeliveryVerificationEventId = "event-test",
                    LastOperationId = operationId
                },
                cancellationToken);
            return new SeqConnectionProvisioningResult(
                "api-key-1",
                "seq-connect-test",
                "event-test",
                verifiedAt,
                ReusedCredential: false);
        }
    }

    private sealed class RecordingDeliveryStateStore : ISeqDeliveryStateStore
    {
        public int SetCount { get; private set; }
        public bool DesiredEnabled { get; private set; }

        public SeqDeliveryState GetState() => new(
            EffectiveEnabled: false,
            DesiredEnabled,
            RestartRequired: DesiredEnabled,
            UpdatedAtUtc: null,
            WarningCode: null);

        public Task<SeqDeliveryState> SetDesiredAsync(
            bool enabled,
            CancellationToken cancellationToken)
        {
            SetCount++;
            DesiredEnabled = enabled;
            return Task.FromResult(new SeqDeliveryState(
                EffectiveEnabled: false,
                DesiredEnabled: enabled,
                RestartRequired: enabled,
                UpdatedAtUtc: TimeProvider.System.GetUtcNow(),
                WarningCode: null));
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

    private sealed class FakeDockerHost : IDockerHost
    {
        private DockerContainerInspection? _inspection;

        public int CreateCount { get; private set; }
        public int StartCount { get; private set; }
        public int RemoveCount { get; private set; }
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
            _inspection = null;
            return Task.CompletedTask;
        }

        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
