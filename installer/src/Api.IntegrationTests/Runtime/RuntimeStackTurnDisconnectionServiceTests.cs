using System.Text;
using System.Text.Json;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Turn;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackTurnDisconnectionServiceTests
{
    [Fact]
    public async Task Disconnect_removes_only_mem_turn_settings_restarts_and_records_disconnected_state()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(RuntimeStackTurnDisconnectStatuses.Ready, review!.Status);
        Assert.True(review.ConfigurationChangeRequired);
        Assert.True(review.RestartRequired);

        var result = await fixture.Service.DisconnectAsync(
            "tester",
            Request(review.ReviewHash, "turn-disconnect-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(RuntimeStackTurnDisconnectStatuses.Succeeded, result!.Status);
        Assert.True(result.ConfigurationChanged);
        Assert.True(result.MatrixRestarted);
        Assert.Equal(RuntimeStackTurnStates.NotConnected, result.StateAfter);
        Assert.Equal(1, fixture.Candidate.ValidationCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);

        var text = await File.ReadAllTextAsync(fixture.ConfigPath);
        Assert.Contains("server_name: matrix-e01affa9.matrixeasyhost.com", text, StringComparison.Ordinal);
        Assert.Contains("# unrelated top-level comment", text, StringComparison.Ordinal);
        Assert.Contains("report_stats: false", text, StringComparison.Ordinal);
        Assert.DoesNotContain("MEM-managed TURN", text, StringComparison.Ordinal);
        Assert.DoesNotContain("turn_uris:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("turn_shared_secret", text, StringComparison.Ordinal);

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.NotNull(manifest);
        Assert.Equal("false", manifest!.Matrix.RuntimeMetadata["turnConfigured"]);
        Assert.Equal("operator-disconnected", manifest.Matrix.RuntimeMetadata["turnConfigurationSource"]);
        Assert.Equal(RuntimeStackTurnManagementKinds.None, manifest.Matrix.RuntimeMetadata["turnManagement"]);
        Assert.Equal("disconnect", manifest.Matrix.RuntimeMetadata["turnLastOperationMode"]);
        Assert.False(manifest.Matrix.RuntimeMetadata.ContainsKey("turnUris"));
        Assert.DoesNotContain("platform-secret", fixture.Operations.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("platform-secret", fixture.Operations.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Already_disconnected_review_returns_no_change()
    {
        await using var fixture = await Fixture.CreateAsync(connected: false);

        var review = await fixture.Service.ReviewAsync(
            "tester",
            CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(RuntimeStackTurnDisconnectStatuses.NoChange, review!.Status);
        Assert.False(review.ConfigurationChangeRequired);
        Assert.False(review.RestartRequired);
        Assert.Equal(
            RuntimeStackTurnConfigTransaction.Hash(
                await File.ReadAllBytesAsync(fixture.ConfigPath)),
            review.CurrentConfigurationSha256);
    }

    [Fact]
    public async Task Candidate_rejection_does_not_mutate_config_or_restart_matrix()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Candidate.Valid = false;
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.DisconnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-disconnect-rejected"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnDisconnectStatuses.CandidateRejected, result!.Status);
        Assert.False(result.ConfigurationChanged);
        Assert.False(result.RollbackAttempted);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));
    }

    [Fact]
    public async Task Restart_failure_restores_exact_config_and_metadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Lifecycle.ThrowOnRestartCalls.Add(1);
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var beforeManifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        var beforeMetadata = beforeManifest!.Matrix.RuntimeMetadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.DisconnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-disconnect-rollback"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnDisconnectStatuses.RolledBack, result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal("turn_disconnect_restart_failed", result.ErrorCode);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));

        var restored = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.Equal(beforeMetadata.Count, restored!.Matrix.RuntimeMetadata.Count);
        foreach (var pair in beforeMetadata)
        {
            Assert.True(restored.Matrix.RuntimeMetadata.TryGetValue(pair.Key, out var value));
            Assert.Equal(pair.Value, value);
        }
    }

    [Fact]
    public async Task Completed_idempotent_result_replays_without_second_mutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);
        var request = Request(review!.ReviewHash, "turn-disconnect-replay");

        var first = await fixture.Service.DisconnectAsync(
            "tester", request, "owner", Guid.NewGuid(), CancellationToken.None);
        var replay = await fixture.Service.DisconnectAsync(
            "tester", request, "owner", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(first!.OperationId, replay!.OperationId);
        Assert.Equal(RuntimeStackTurnDisconnectStatuses.Succeeded, replay.Status);
        Assert.Equal(1, fixture.Operations.StartCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Rollback_restart_failure_returns_unresolved_outcome()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Lifecycle.ThrowOnRestartCalls.Add(1);
        fixture.Lifecycle.ThrowOnRestartCalls.Add(2);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.DisconnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-disconnect-unresolved"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnDisconnectStatuses.Unresolved, result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.False(result.RollbackSucceeded);
        Assert.Equal("turn_disconnect_rollback_failed", result.ErrorCode);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
    }

    private static RuntimeStackTurnDisconnectRequest Request(
        string reviewHash,
        string idempotencyKey) =>
        new(reviewHash, idempotencyKey, ConfirmDisconnectFromPlatformTurn: true);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly MemDbContext _db;

        private Fixture(
            string root,
            string configPath,
            MemDbContext db,
            RuntimeStackManifestStore manifestStore,
            RuntimeStackTurnDisconnectionService service,
            FakeOperations operations,
            FakeLifecycle lifecycle,
            FakeCandidate candidate)
        {
            _root = root;
            ConfigPath = configPath;
            _db = db;
            ManifestStore = manifestStore;
            Service = service;
            Operations = operations;
            Lifecycle = lifecycle;
            Candidate = candidate;
        }

        public string ConfigPath { get; }
        public RuntimeStackManifestStore ManifestStore { get; }
        public RuntimeStackTurnDisconnectionService Service { get; }
        public FakeOperations Operations { get; }
        public FakeLifecycle Lifecycle { get; }
        public FakeCandidate Candidate { get; }

        public static async Task<Fixture> CreateAsync(bool connected = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-turn-disconnect-{Guid.NewGuid():N}");
            var matrixData = Path.Combine(root, "instances", "tester", "matrix");
            var manifests = Path.Combine(root, "control-plane", "runtime-stacks");
            Directory.CreateDirectory(matrixData);
            Directory.CreateDirectory(manifests);

            var editor = new SynapseTurnConfigEditor();
            var baseBytes = Encoding.UTF8.GetBytes(
                "server_name: matrix-e01affa9.matrixeasyhost.com\n" +
                "# unrelated top-level comment\n" +
                "report_stats: false\n");
            var configBytes = connected
                ? editor.RenderConnect(baseBytes, Platform())
                : baseBytes;
            var configPath = Path.Combine(matrixData, "homeserver.yaml");
            await File.WriteAllBytesAsync(configPath, configBytes);

            var stackId = Guid.Parse("47dad438-56b0-4f47-a4b5-1fd4e92dc228");
            var manifest = new RuntimeStackManifest(
                Source: "control-plane",
                StackId: stackId,
                Slug: "tester",
                LastVerifiedStatus: "ready",
                LastVerifiedAtUtc: DateTimeOffset.Parse("2026-07-26T04:56:00Z"),
                Matrix: new RuntimeStackServiceManifest(
                    InstanceId: Guid.Parse("0749399a-7178-457b-9e5b-91a7dc29fec5"),
                    ServiceKey: ServiceKeys.Matrix,
                    ContainerId: "matrix-container-id",
                    ContainerName: "mem-matrix-tester",
                    HostPort: 0,
                    DataPath: matrixData,
                    ServerName: "matrix-e01affa9.matrixeasyhost.com",
                    PublicHost: "matrix-e01affa9.matrixeasyhost.com",
                    PublicBaseUrl: "https://matrix-e01affa9.matrixeasyhost.com",
                    InternalHost: "mem-matrix-tester",
                    InternalBaseUrl: "http://mem-matrix-tester:8008",
                    PublicRouteId: "97",
                    InternalRouteId: null,
                    NpmCertificateId: 3,
                    RuntimeMetadata: connected
                        ? ConnectedMetadata(configBytes)
                        : DisconnectedMetadata(configBytes)),
                Element: null,
                Warnings: [],
                Metadata: new Dictionary<string, string?>());
            var manifestPath = Path.Combine(manifests, $"{stackId:N}.json");
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));

            var dbPath = Path.Combine(root, "control-plane.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = root
                })
                .Build();
            var manifestStore = new RuntimeStackManifestStore(configuration, db);
            var operations = new FakeOperations();
            var lifecycle = new FakeLifecycle();
            var candidate = new FakeCandidate();
            var inspection = connected
                ? new FakeInspection(
                    () => Inspection(RuntimeStackTurnStates.Connected, configPath),
                    () => Inspection(RuntimeStackTurnStates.Connected, configPath),
                    () => Inspection(RuntimeStackTurnStates.NotConnected, configPath))
                : new FakeInspection(
                    () => Inspection(RuntimeStackTurnStates.NotConnected, configPath));
            var reader = new SynapseTurnConfigReader();
            var service = new RuntimeStackTurnDisconnectionService(
                manifestStore,
                inspection,
                reader,
                new RuntimeStackTurnConfigTransaction(reader, editor),
                operations,
                new RuntimeStackTurnMutationLock(),
                lifecycle,
                candidate,
                new FakeAudit(),
                NullLogger<RuntimeStackTurnDisconnectionService>.Instance);

            return new Fixture(
                root,
                configPath,
                db,
                manifestStore,
                service,
                operations,
                lifecycle,
                candidate);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static IReadOnlyDictionary<string, string?> ConnectedMetadata(byte[] configBytes) =>
            new Dictionary<string, string?>
            {
                ["runtimeNetworkName"] = "mem-runtime-tester",
                ["matrixImage"] = "matrixdotorg/synapse@sha256:approved",
                ["publicForwardPort"] = "8008",
                ["turnConfigured"] = "true",
                ["turnPublicHost"] = "turn.deltabox.dev",
                ["turnRealm"] = "deltabox.dev",
                ["turnUris"] = string.Join(", ", Platform().TurnUris),
                ["turnRelayPortsPublished"] = "true",
                ["turnSharedSecretPresent"] = "true",
                ["turnUserLifetime"] = "1h",
                ["turnAllowGuests"] = "true",
                ["turnConfigurationSource"] = "platform-coturn",
                ["turnManagement"] = RuntimeStackTurnManagementKinds.MemManaged,
                ["turnConfigurationSha256"] = RuntimeStackTurnConfigTransaction.Hash(configBytes)
            };

        private static IReadOnlyDictionary<string, string?> DisconnectedMetadata(byte[] configBytes) =>
            new Dictionary<string, string?>
            {
                ["runtimeNetworkName"] = "mem-runtime-tester",
                ["matrixImage"] = "matrixdotorg/synapse@sha256:approved",
                ["publicForwardPort"] = "8008",
                ["turnConfigured"] = "false",
                ["turnConfigurationSource"] = "operator-disconnected",
                ["turnManagement"] = RuntimeStackTurnManagementKinds.None,
                ["turnConfigurationSha256"] = RuntimeStackTurnConfigTransaction.Hash(configBytes)
            };

        private static RuntimeStackTurnInspectionResponse Inspection(
            string state,
            string configPath)
        {
            var connected = state == RuntimeStackTurnStates.Connected;
            var hash = RuntimeStackTurnConfigTransaction.Hash(File.ReadAllBytes(configPath));
            return new RuntimeStackTurnInspectionResponse(
                Source: "control-plane",
                Status: "ok",
                RuntimeStackId: Guid.Parse("47dad438-56b0-4f47-a4b5-1fd4e92dc228"),
                Slug: "tester",
                InspectedAtUtc: DateTimeOffset.UtcNow,
                State: state,
                Management: connected
                    ? RuntimeStackTurnManagementKinds.MemManaged
                    : RuntimeStackTurnManagementKinds.None,
                LiveConfiguration: new RuntimeStackTurnLiveConfigurationResponse(
                    Supported: true,
                    AnyTurnSettings: connected,
                    MemManagedMarkerPresent: connected,
                    TurnUris: connected ? Platform().TurnUris : [],
                    CredentialMechanism: connected ? "inline-shared-secret" : "none",
                    SharedSecretPresent: connected,
                    SharedSecretMatchesPlatform: connected ? true : (bool?)null,
                    UserLifetime: connected ? "1h" : null,
                    AllowGuests: connected ? true : (bool?)null,
                    PublicHost: connected ? "turn.deltabox.dev" : null,
                    Realm: connected ? "deltabox.dev" : null,
                    FileSha256: hash,
                    ProblemCode: null,
                    Detail: null),
                PersistedMetadata: new RuntimeStackTurnPersistedMetadataResponse(
                    Recorded: true,
                    Configured: connected,
                    TurnUris: connected ? Platform().TurnUris : [],
                    PublicHost: connected ? "turn.deltabox.dev" : null,
                    Realm: connected ? "deltabox.dev" : null,
                    ConfigurationSource: connected ? "platform-coturn" : "operator-disconnected",
                    RelayPortsPublished: connected ? true : (bool?)null,
                    SharedSecretPresent: connected ? true : (bool?)null,
                    UserLifetime: connected ? "1h" : null,
                    AllowGuests: connected ? true : (bool?)null,
                    MatchesLiveConfiguration: true),
                Platform: new RuntimeStackTurnPlatformResponse(
                    Status: "ok",
                    Readiness: "ready",
                    Running: true,
                    OwnershipVerified: true,
                    ImageApproved: true,
                    PublicHost: "turn.deltabox.dev",
                    TurnUris: Platform().TurnUris,
                    SecretPresent: true,
                    RelayPortsPublished: true,
                    SecurityPolicyApplied: true,
                    Detail: null),
                MatrixRuntime: new RuntimeStackTurnMatrixRuntimeResponse(
                    Exists: true,
                    Running: true,
                    IdentityMatches: true,
                    ProblemCode: null,
                    Detail: null),
                Diagnostics: [],
                Warnings: [],
                Detail: connected ? "Connected." : "Not connected.");
        }

        private static HostAgent.Runtime.Coturn.CoturnSynapseConfig Platform() =>
            new(
                PublicHost: "turn.deltabox.dev",
                Realm: "deltabox.dev",
                TurnUris:
                [
                    "turn:turn.deltabox.dev:3478?transport=udp",
                    "turn:turn.deltabox.dev:3478?transport=tcp"
                ],
                SharedSecret: "platform-secret",
                UserLifetime: "1h",
                AllowGuests: true,
                RelayPortsPublished: true,
                ExpectedBaseDomain: "deltabox.dev");
    }

    private sealed class FakeInspection : IRuntimeStackTurnInspectionService
    {
        private readonly Queue<Func<RuntimeStackTurnInspectionResponse>> _results;
        private Func<RuntimeStackTurnInspectionResponse>? _last;

        public FakeInspection(params Func<RuntimeStackTurnInspectionResponse>[] results)
        {
            _results = new Queue<Func<RuntimeStackTurnInspectionResponse>>(results);
        }

        public Task<RuntimeStackTurnInspectionResponse?> InspectAsync(
            string slugOrId,
            CancellationToken ct)
        {
            if (_results.TryDequeue(out var result))
            {
                _last = result;
            }

            return Task.FromResult(_last?.Invoke());
        }
    }

    private sealed class FakeLifecycle : IRuntimeMatrixContainerLifecycleService
    {
        public int RestartCount { get; private set; }
        public HashSet<int> ThrowOnRestartCalls { get; } = [];

        public Task<RuntimeMatrixContainerObservation> InspectAsync(
            RuntimeStackServiceManifest matrix,
            CancellationToken ct) =>
            Task.FromResult(Observation());

        public Task<RuntimeMatrixContainerObservation> RestartAsync(
            RuntimeStackServiceManifest matrix,
            CancellationToken ct)
        {
            RestartCount++;
            if (ThrowOnRestartCalls.Contains(RestartCount))
            {
                throw new InvalidOperationException("restart failed");
            }

            return Task.FromResult(Observation());
        }

        public Task<string> GetBoundedLogsAsync(
            string containerId,
            int tail,
            CancellationToken ct) =>
            Task.FromResult(string.Empty);

        private static RuntimeMatrixContainerObservation Observation() =>
            new(
                ContainerId: "matrix-container-id",
                ContainerName: "mem-matrix-tester",
                Image: "matrixdotorg/synapse@sha256:approved",
                User: null,
                Running: true,
                IdentityMatches: true,
                DataBindMatches: true,
                ExpectedNetworkAttached: true,
                DirectHostPortExposed: false);
    }

    private sealed class FakeCandidate : ISynapseFederationConfigCandidateValidator
    {
        public bool Valid { get; set; } = true;
        public int ValidationCount { get; private set; }

        public Task<SynapseFederationCandidateValidationResult> ValidateAsync(
            RuntimeStackManifest manifest,
            RuntimeMatrixContainerObservation container,
            string candidatePath,
            CancellationToken ct)
        {
            ValidationCount++;
            return Task.FromResult(new SynapseFederationCandidateValidationResult(
                Valid,
                Valid ? 0 : 1,
                container.Image,
                ImagePinned: true,
                LogTail: null,
                ErrorCode: Valid ? null : "turn_candidate_rejected",
                Detail: Valid ? "accepted" : "rejected"));
        }
    }

    private sealed class FakeOperations : IRuntimeOperationStore
    {
        private Guid? _id;
        private string? _idempotencyKey;
        private Guid? _stackId;
        private string? _operation;

        public int StartCount { get; private set; }
        public string? InputJson { get; private set; }
        public string? ResultJson { get; private set; }
        public string? Status { get; private set; }
        public RuntimeOperationSummary? ActiveConflict { get; set; }

        public Task<Guid> StartAsync(
            Guid? runtimeStackId,
            string operation,
            string? idempotencyKey,
            string requestedBy,
            string hostMutationLevel,
            object? input,
            CancellationToken ct,
            Guid? restoreAttemptId = null)
        {
            StartCount++;
            _id = Guid.NewGuid();
            _idempotencyKey = idempotencyKey;
            _stackId = runtimeStackId;
            _operation = operation;
            Status = "running";
            InputJson = input is null ? null : JsonSerializer.Serialize(input, JsonOptions());
            return Task.FromResult(_id.Value);
        }

        public Task UpdateStepAsync(Guid operationId, string currentStep, CancellationToken ct) =>
            Task.CompletedTask;

        public Task CompleteAsync(
            Guid operationId,
            string status,
            string? currentStep,
            object? result,
            object? evidence,
            CancellationToken ct)
        {
            Status = status;
            ResultJson = result is null ? null : JsonSerializer.Serialize(result, JsonOptions());
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid operationId,
            string? currentStep,
            string error,
            object? evidence,
            CancellationToken ct) =>
            FailAsync(operationId, currentStep, error, null, evidence, ct);

        public Task FailAsync(
            Guid operationId,
            string? currentStep,
            string error,
            object? result,
            object? evidence,
            CancellationToken ct)
        {
            Status = "failed";
            ResultJson = result is null ? null : JsonSerializer.Serialize(result, JsonOptions());
            return Task.CompletedTask;
        }

        public Task<RuntimeOperationDetail?> FindByIdempotencyKeyAsync(
            Guid runtimeStackId,
            string operation,
            string idempotencyKey,
            CancellationToken ct)
        {
            if (_id is null ||
                _stackId != runtimeStackId ||
                _idempotencyKey != idempotencyKey ||
                !string.Equals(_operation, operation, StringComparison.Ordinal))
            {
                return Task.FromResult<RuntimeOperationDetail?>(null);
            }

            return Task.FromResult<RuntimeOperationDetail?>(new RuntimeOperationDetail(
                _id.Value,
                runtimeStackId,
                operation,
                Status ?? "running",
                idempotencyKey,
                "owner",
                "filesystem,docker",
                "completed",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                Status == "running" ? null : DateTimeOffset.UtcNow,
                Status == "failed" ? "failed" : null,
                InputJson,
                ResultJson,
                null,
                null));
        }

        public Task<RuntimeOperationSummary?> FindActiveMutatingOperationForStackAsync(
            Guid runtimeStackId,
            CancellationToken ct) =>
            Task.FromResult(ActiveConflict);

        public Task<IReadOnlyList<RuntimeOperationSummary>> ListForStackAsync(
            Guid runtimeStackId,
            int limit,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RuntimeOperationSummary>>([]);

        private static JsonSerializerOptions JsonOptions() =>
            new(JsonSerializerDefaults.Web);
    }

    private sealed class FakeAudit : IMemOperatorAuditService
    {
        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
