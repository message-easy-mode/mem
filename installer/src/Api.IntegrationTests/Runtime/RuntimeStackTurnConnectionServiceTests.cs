using System.Text;
using System.Text.Json;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Stacks.Turn;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Services.Identity;
using HostAgent.Runtime.ServiceRuntime;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackTurnConnectionServiceTests
{
    [Fact]
    public async Task Adopts_the_matching_tester_configuration_without_rewrite_or_restart()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: true);
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(RuntimeStackTurnConnectModes.AdoptExisting, review!.Mode);
        Assert.False(review.ConfigurationChangeRequired);
        Assert.False(review.RestartRequired);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(review.ReviewHash, "turn-connect-adopt-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(RuntimeStackTurnConnectStatuses.Succeeded, result!.Status);
        Assert.Equal(RuntimeStackTurnConnectModes.AdoptExisting, result.Mode);
        Assert.False(result.ConfigurationChanged);
        Assert.False(result.MatrixRestarted);
        Assert.Equal(0, fixture.Candidate.ValidationCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.NotNull(manifest);
        Assert.Equal("true", manifest!.Matrix.RuntimeMetadata["turnConfigured"]);
        Assert.Equal("platform-coturn", manifest.Matrix.RuntimeMetadata["turnConfigurationSource"]);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, manifest.Matrix.RuntimeMetadata["turnManagement"]);
        Assert.Equal("adopt-existing", manifest.Matrix.RuntimeMetadata["turnLastOperationMode"]);
        Assert.DoesNotContain("platform-secret", fixture.Operations.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("platform-secret", fixture.Operations.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configures_validates_restarts_and_records_a_disconnected_stack()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: false);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(RuntimeStackTurnConnectModes.Configure, review!.Mode);
        Assert.True(review.ConfigurationChangeRequired);
        Assert.True(review.RestartRequired);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(review.ReviewHash, "turn-connect-configure-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(RuntimeStackTurnConnectStatuses.Succeeded, result!.Status);
        Assert.True(result.ConfigurationChanged);
        Assert.True(result.MatrixRestarted);
        Assert.Equal(1, fixture.Candidate.ValidationCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);

        var text = await File.ReadAllTextAsync(fixture.ConfigPath);
        Assert.Contains("# MEM-managed TURN configuration for Matrix voice/video calls.", text, StringComparison.Ordinal);
        Assert.Contains("turn:turn.deltabox.dev:3478?transport=udp", text, StringComparison.Ordinal);
        Assert.Contains("turn_shared_secret: \"platform-secret\"", text, StringComparison.Ordinal);
        Assert.Contains("server_name: matrix-e01affa9.matrixeasyhost.com", text, StringComparison.Ordinal);

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.Equal("configure", manifest!.Matrix.RuntimeMetadata["turnLastOperationMode"]);
        Assert.Equal("true", manifest.Matrix.RuntimeMetadata["turnConfigured"]);
    }

    [Fact]
    public async Task Adopts_a_migration_preserved_platform_match_without_rewrite_or_restart()
    {
        await using var fixture = await Fixture.CreateAsync(
            existingMatchingTurn: true,
            migrationPreserved: true);
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(RuntimeStackTurnConnectModes.AdoptExisting, review!.Mode);
        Assert.False(review.ConfigurationChangeRequired);
        Assert.False(review.RestartRequired);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(review.ReviewHash, "turn-connect-migration-adopt-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnConnectStatuses.Succeeded, result!.Status);
        Assert.Equal(RuntimeStackTurnConnectModes.AdoptExisting, result.Mode);
        Assert.False(result.ConfigurationChanged);
        Assert.False(result.MatrixRestarted);
        Assert.Equal(0, fixture.Candidate.ValidationCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.Equal("platform-coturn", manifest!.Matrix.RuntimeMetadata["turnConfigurationSource"]);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, manifest.Matrix.RuntimeMetadata["turnManagement"]);
        Assert.Equal("adopt-existing", manifest.Matrix.RuntimeMetadata["turnLastOperationMode"]);
    }

    [Fact]
    public async Task External_replacement_requires_explicit_replacement_confirmation_before_start()
    {
        await using var fixture = await Fixture.CreateAsync(
            existingMatchingTurn: false,
            externalTurn: true);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        Assert.NotNull(review);
        Assert.Equal(RuntimeStackTurnConnectModes.ReplaceExternal, review!.Mode);
        Assert.True(review.ConfigurationChangeRequired);
        Assert.True(review.RestartRequired);

        var error = await Assert.ThrowsAsync<RuntimeStackTurnConnectionException>(() =>
            fixture.Service.ConnectAsync(
                "tester",
                Request(review.ReviewHash, "turn-connect-external-confirm-1"),
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("turn_connect_external_replacement_confirmation_required", error.Code);
        Assert.Equal(0, fixture.Operations.StartCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Replaces_external_turn_and_records_mem_managed_association()
    {
        await using var fixture = await Fixture.CreateAsync(
            existingMatchingTurn: false,
            externalTurn: true);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(
                review!.ReviewHash,
                "turn-connect-external-replace-1",
                confirmReplaceExternalTurn: true),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnConnectStatuses.Succeeded, result!.Status);
        Assert.Equal(RuntimeStackTurnConnectModes.ReplaceExternal, result.Mode);
        Assert.True(result.ConfigurationChanged);
        Assert.True(result.MatrixRestarted);
        Assert.Equal(1, fixture.Candidate.ValidationCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);

        var text = await File.ReadAllTextAsync(fixture.ConfigPath);
        Assert.DoesNotContain("external-secret", text, StringComparison.Ordinal);
        Assert.Contains("turn_shared_secret: \"platform-secret\"", text, StringComparison.Ordinal);
        Assert.Contains("# MEM-managed TURN configuration for Matrix voice/video calls.", text, StringComparison.Ordinal);

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.Equal("platform-coturn", manifest!.Matrix.RuntimeMetadata["turnConfigurationSource"]);
        Assert.Equal(RuntimeStackTurnManagementKinds.MemManaged, manifest.Matrix.RuntimeMetadata["turnManagement"]);
        Assert.Equal("replace-external", manifest.Matrix.RuntimeMetadata["turnLastOperationMode"]);
        Assert.DoesNotContain("external-secret", fixture.Operations.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("external-secret", fixture.Operations.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_replacement_restart_failure_restores_exact_config_and_external_metadata()
    {
        await using var fixture = await Fixture.CreateAsync(
            existingMatchingTurn: false,
            externalTurn: true);
        fixture.Lifecycle.ThrowOnRestartCalls.Add(1);
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var beforeManifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        var beforeMetadata = beforeManifest!.Matrix.RuntimeMetadata.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(
                review!.ReviewHash,
                "turn-connect-external-rollback-1",
                confirmReplaceExternalTurn: true),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnConnectStatuses.RolledBack, result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal("turn_connect_restart_failed", result.ErrorCode);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.Equal(beforeMetadata.Count, manifest!.Matrix.RuntimeMetadata.Count);
        foreach (var pair in beforeMetadata)
        {
            Assert.True(manifest.Matrix.RuntimeMetadata.TryGetValue(pair.Key, out var value));
            Assert.Equal(pair.Value, value);
        }
        Assert.Equal("migration-source-preserved", manifest.Matrix.RuntimeMetadata["turnConfigurationSource"]);
        Assert.Equal(RuntimeStackTurnManagementKinds.ExternalObserved, manifest.Matrix.RuntimeMetadata["turnManagement"]);
    }

    [Fact]
    public async Task Candidate_rejection_does_not_replace_config_or_restart_matrix()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: false);
        fixture.Candidate.Valid = false;
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-connect-rejected-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnConnectStatuses.CandidateRejected, result!.Status);
        Assert.False(result.ConfigurationChanged);
        Assert.False(result.RollbackAttempted);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));
        Assert.Equal("failed", fixture.Operations.Status);
    }

    [Fact]
    public async Task Restart_failure_restores_the_exact_previous_config_and_reports_rolled_back()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: false);
        fixture.Lifecycle.ThrowOnRestartCalls.Add(1);
        var before = await File.ReadAllBytesAsync(fixture.ConfigPath);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-connect-rollback-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnConnectStatuses.RolledBack, result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal("turn_connect_restart_failed", result.ErrorCode);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.ConfigPath));

        var manifest = await fixture.ManifestStore.FindAsync("tester", CancellationToken.None);
        Assert.False(manifest!.Matrix.RuntimeMetadata.ContainsKey("turnConfigured"));
    }

    [Fact]
    public async Task Completed_idempotent_result_replays_without_a_second_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: true);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);
        var request = Request(review!.ReviewHash, "turn-connect-replay-1");

        var first = await fixture.Service.ConnectAsync(
            "tester", request, "owner", Guid.NewGuid(), CancellationToken.None);
        var replay = await fixture.Service.ConnectAsync(
            "tester", request, "owner", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(first!.OperationId, replay!.OperationId);
        Assert.Equal(RuntimeStackTurnConnectStatuses.Succeeded, replay.Status);
        Assert.Equal(1, fixture.Operations.StartCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
    }


    [Fact]
    public async Task Rejects_a_stale_review_hash_before_starting_an_operation()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: false);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var error = await Assert.ThrowsAsync<RuntimeStackTurnConnectionException>(() =>
            fixture.Service.ConnectAsync(
                "tester",
                Request(review!.ReviewHash + "-stale", "turn-connect-stale-1"),
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("turn_connect_review_stale", error.Code);
        Assert.Equal(0, fixture.Operations.StartCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Rejects_an_active_same_stack_mutating_operation_before_start()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: false);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);
        fixture.Operations.ActiveConflict = new RuntimeOperationSummary(
            Guid.NewGuid(),
            Guid.Parse("47dad438-56b0-4f47-a4b5-1fd4e92dc228"),
            "destroy-runtime-stack",
            "running",
            null,
            "owner",
            "filesystem,docker",
            "running",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null);

        var error = await Assert.ThrowsAsync<RuntimeStackTurnConnectionException>(() =>
            fixture.Service.ConnectAsync(
                "tester",
                Request(review!.ReviewHash, "turn-connect-conflict-1"),
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("turn_connect_operation_in_progress", error.Code);
        Assert.Equal(0, fixture.Operations.StartCount);
    }

    [Fact]
    public async Task Reuse_of_idempotency_key_for_a_different_review_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: true);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);
        await fixture.Service.ConnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-connect-reused-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        var error = await Assert.ThrowsAsync<RuntimeStackTurnConnectionException>(() =>
            fixture.Service.ConnectAsync(
                "tester",
                Request(review.ReviewHash + "-different", "turn-connect-reused-1"),
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("turn_connect_idempotency_conflict", error.Code);
        Assert.Equal(1, fixture.Operations.StartCount);
    }

    [Fact]
    public async Task Rollback_restart_failure_returns_an_unresolved_manual_recovery_outcome()
    {
        await using var fixture = await Fixture.CreateAsync(existingMatchingTurn: false);
        fixture.Lifecycle.ThrowOnRestartCalls.Add(1);
        fixture.Lifecycle.ThrowOnRestartCalls.Add(2);
        var review = await fixture.Service.ReviewAsync("tester", CancellationToken.None);

        var result = await fixture.Service.ConnectAsync(
            "tester",
            Request(review!.ReviewHash, "turn-connect-unresolved-1"),
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(RuntimeStackTurnConnectStatuses.Unresolved, result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.False(result.RollbackSucceeded);
        Assert.Equal("turn_connect_rollback_failed", result.ErrorCode);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
    }

    private static RuntimeStackTurnConnectRequest Request(
        string reviewHash,
        string idempotencyKey,
        bool confirmReplaceExternalTurn = false) =>
        new(
            reviewHash,
            idempotencyKey,
            ConfirmConnectToPlatformTurn: true,
            ConfirmReplaceExternalTurn: confirmReplaceExternalTurn);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly MemDbContext _db;

        private Fixture(
            string root,
            string configPath,
            MemDbContext db,
            RuntimeStackManifestStore manifestStore,
            RuntimeStackTurnConnectionService service,
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
        public RuntimeStackTurnConnectionService Service { get; }
        public FakeOperations Operations { get; }
        public FakeLifecycle Lifecycle { get; }
        public FakeCandidate Candidate { get; }

        public static async Task<Fixture> CreateAsync(
            bool existingMatchingTurn,
            bool migrationPreserved = false,
            bool externalTurn = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-turn-connect-{Guid.NewGuid():N}");
            var matrixData = Path.Combine(root, "instances", "tester", "matrix");
            var manifests = Path.Combine(root, "control-plane", "runtime-stacks");
            Directory.CreateDirectory(matrixData);
            Directory.CreateDirectory(manifests);

            var platform = Platform();
            var editor = new SynapseTurnConfigEditor();
            var baseBytes = Encoding.UTF8.GetBytes(
                "server_name: matrix-e01affa9.matrixeasyhost.com\n" +
                "public_baseurl: https://matrix-e01affa9.matrixeasyhost.com/\n" +
                "report_stats: false\n");
            var configBytes = externalTurn
                ? ExternalTurnConfig(baseBytes)
                : existingMatchingTurn && migrationPreserved
                    ? PlatformTurnConfigWithoutMemMarker(baseBytes, platform)
                    : existingMatchingTurn
                        ? editor.RenderConnect(baseBytes, platform)
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
                    RuntimeMetadata: InitialRuntimeMetadata(
                        configBytes,
                        platform,
                        migrationPreserved,
                        externalTurn)),
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
            var initialState = externalTurn
                ? Inspection(
                    RuntimeStackTurnStates.External,
                    recorded: true,
                    configPath: configPath,
                    management: RuntimeStackTurnManagementKinds.ExternalObserved,
                    liveUris: ExternalUris,
                    sharedSecretMatchesPlatform: false,
                    memManagedMarkerPresent: false,
                    configurationSource: "migration-source-preserved",
                    userLifetime: "2h",
                    allowGuests: false,
                    publicHost: "turn.external.test",
                    realm: "external.test")
                : existingMatchingTurn && migrationPreserved
                    ? Inspection(
                        RuntimeStackTurnStates.Connected,
                        recorded: true,
                        configPath: configPath,
                        management: RuntimeStackTurnManagementKinds.MemManaged,
                        liveUris: platform.TurnUris,
                        sharedSecretMatchesPlatform: true,
                        memManagedMarkerPresent: false,
                        configurationSource: "migration-source-preserved")
                    : existingMatchingTurn
                        ? Inspection(RuntimeStackTurnStates.Drift, false, configPath)
                        : Inspection(RuntimeStackTurnStates.NotConnected, false, configPath);
            var finalState = Inspection(RuntimeStackTurnStates.Connected, true, configPath);
            var inspection = new FakeInspection(initialState, initialState, finalState);
            var reader = new SynapseTurnConfigReader();
            var service = new RuntimeStackTurnConnectionService(
                manifestStore,
                inspection,
                new FakePlatformConfiguration(platform),
                reader,
                new RuntimeStackTurnConfigTransaction(reader, editor),
                operations,
                new RuntimeStackTurnMutationLock(),
                lifecycle,
                candidate,
                new FakeAudit(),
                NullLogger<RuntimeStackTurnConnectionService>.Instance);

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

        private static RuntimeStackTurnInspectionResponse Inspection(
            string state,
            bool recorded,
            string configPath,
            string? management = null,
            IReadOnlyList<string>? liveUris = null,
            bool? sharedSecretMatchesPlatform = null,
            bool? memManagedMarkerPresent = null,
            string? configurationSource = null,
            string? userLifetime = null,
            bool? allowGuests = null,
            string? publicHost = null,
            string? realm = null)
        {
            var connected = state != RuntimeStackTurnStates.NotConnected;
            var effectiveUris = liveUris ?? (connected ? Platform().TurnUris : []);
            var hash = RuntimeStackTurnConfigTransaction.Hash(File.ReadAllBytes(configPath));
            var effectiveManagement = management ?? (connected
                ? RuntimeStackTurnManagementKinds.MemManaged
                : RuntimeStackTurnManagementKinds.None);
            var marker = memManagedMarkerPresent ?? connected;
            var secretMatches = sharedSecretMatchesPlatform ?? (connected ? true : (bool?)null);
            var effectiveLifetime = userLifetime ?? (connected ? "1h" : null);
            var effectiveGuests = allowGuests ?? (connected ? true : (bool?)null);
            var effectivePublicHost = publicHost ?? (connected ? "turn.deltabox.dev" : null);
            var effectiveRealm = realm ?? (connected ? "deltabox.dev" : null);

            return new RuntimeStackTurnInspectionResponse(
                Source: "control-plane",
                Status: state == RuntimeStackTurnStates.Connected ? "ok" : "attention",
                RuntimeStackId: Guid.Parse("47dad438-56b0-4f47-a4b5-1fd4e92dc228"),
                Slug: "tester",
                InspectedAtUtc: DateTimeOffset.UtcNow,
                State: state,
                Management: effectiveManagement,
                LiveConfiguration: new RuntimeStackTurnLiveConfigurationResponse(
                    Supported: true,
                    AnyTurnSettings: connected,
                    MemManagedMarkerPresent: marker,
                    TurnUris: connected ? effectiveUris : [],
                    CredentialMechanism: connected ? "inline-shared-secret" : "none",
                    SharedSecretPresent: connected,
                    SharedSecretMatchesPlatform: secretMatches,
                    UserLifetime: effectiveLifetime,
                    AllowGuests: effectiveGuests,
                    PublicHost: effectivePublicHost,
                    Realm: effectiveRealm,
                    FileSha256: hash,
                    ProblemCode: null,
                    Detail: null),
                PersistedMetadata: new RuntimeStackTurnPersistedMetadataResponse(
                    Recorded: recorded,
                    Configured: recorded ? true : (bool?)null,
                    TurnUris: recorded ? effectiveUris : [],
                    PublicHost: recorded ? effectivePublicHost : null,
                    Realm: recorded ? effectiveRealm : null,
                    ConfigurationSource: recorded
                        ? configurationSource ?? "platform-coturn"
                        : null,
                    RelayPortsPublished: recorded ? true : (bool?)null,
                    SharedSecretPresent: recorded ? true : (bool?)null,
                    UserLifetime: recorded ? effectiveLifetime : null,
                    AllowGuests: recorded ? effectiveGuests : (bool?)null,
                    MatchesLiveConfiguration: recorded ? true : (bool?)null),
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
                Detail: "test");
        }

        private static readonly string[] ExternalUris =
        [
            "turn:turn.external.test:3478?transport=udp",
            "turn:turn.external.test:3478?transport=tcp"
        ];

        private static byte[] PlatformTurnConfigWithoutMemMarker(
            byte[] baseBytes,
            CoturnSynapseConfig platform) =>
            Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(baseBytes) +
                $"turn_uris:\n  - \"{platform.TurnUris[0]}\"\n  - \"{platform.TurnUris[1]}\"\n" +
                $"turn_shared_secret: \"{platform.SharedSecret}\"\n" +
                $"turn_user_lifetime: {platform.UserLifetime}\n" +
                $"turn_allow_guests: {(platform.AllowGuests ? "true" : "false")}\n");

        private static byte[] ExternalTurnConfig(byte[] baseBytes) =>
            Encoding.UTF8.GetBytes(
                Encoding.UTF8.GetString(baseBytes) +
                $"turn_uris:\n  - \"{ExternalUris[0]}\"\n  - \"{ExternalUris[1]}\"\n" +
                "turn_shared_secret: \"external-secret\"\n" +
                "turn_user_lifetime: 2h\n" +
                "turn_allow_guests: false\n");

        private static Dictionary<string, string?> InitialRuntimeMetadata(
            byte[] configBytes,
            CoturnSynapseConfig platform,
            bool migrationPreserved,
            bool externalTurn)
        {
            var metadata = new Dictionary<string, string?>
            {
                ["runtimeNetworkName"] = "mem-runtime-tester",
                ["matrixImage"] = "matrixdotorg/synapse@sha256:approved",
                ["publicForwardPort"] = "8008"
            };

            if (!migrationPreserved && !externalTurn)
            {
                return metadata;
            }

            metadata["turnConfigured"] = "true";
            metadata["turnPublicHost"] = externalTurn ? "turn.external.test" : platform.PublicHost;
            metadata["turnRealm"] = externalTurn ? "external.test" : platform.Realm;
            metadata["turnUris"] = string.Join(", ", externalTurn ? ExternalUris : platform.TurnUris);
            metadata["turnRelayPortsPublished"] = "true";
            metadata["turnSharedSecretPresent"] = "true";
            metadata["turnUserLifetime"] = externalTurn ? "2h" : platform.UserLifetime;
            metadata["turnAllowGuests"] = externalTurn ? "false" : "true";
            metadata["turnConfigurationSource"] = "migration-source-preserved";
            metadata["turnManagement"] = RuntimeStackTurnManagementKinds.ExternalObserved;
            metadata["turnConfigurationSha256"] = RuntimeStackTurnConfigTransaction.Hash(configBytes);
            return metadata;
        }

        private static CoturnSynapseConfig Platform() =>
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

    private sealed class FakePlatformConfiguration
        : IRuntimeStackTurnPlatformConfigurationProvider
    {
        private readonly CoturnSynapseConfig _configuration;

        public FakePlatformConfiguration(CoturnSynapseConfig configuration)
        {
            _configuration = configuration;
        }

        public Task<CoturnSynapseConfig?> GetSynapseConfigAsync(CancellationToken ct) =>
            Task.FromResult<CoturnSynapseConfig?>(_configuration);
    }

    private sealed class FakeInspection : IRuntimeStackTurnInspectionService
    {
        private readonly Queue<RuntimeStackTurnInspectionResponse> _results;
        private RuntimeStackTurnInspectionResponse? _last;

        public FakeInspection(params RuntimeStackTurnInspectionResponse[] results)
        {
            _results = new Queue<RuntimeStackTurnInspectionResponse>(results);
        }

        public Task<RuntimeStackTurnInspectionResponse?> InspectAsync(
            string slugOrId,
            CancellationToken ct)
        {
            if (_results.TryDequeue(out var result))
            {
                _last = result;
            }

            return Task.FromResult(_last);
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
        private object? _result;

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
            _result = result;
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
            _result = result;
            ResultJson = result is null ? null : JsonSerializer.Serialize(result, JsonOptions());
            return Task.CompletedTask;
        }

        public Task<RuntimeOperationDetail?> FindByIdempotencyKeyAsync(
            Guid runtimeStackId,
            string operation,
            string idempotencyKey,
            CancellationToken ct)
        {
            if (_id is null || _stackId != runtimeStackId || _idempotencyKey != idempotencyKey)
            {
                return Task.FromResult<RuntimeOperationDetail?>(null);
            }

            return Task.FromResult<RuntimeOperationDetail?>(new RuntimeOperationDetail(
                _id.Value,
                runtimeStackId,
                RuntimeStackTurnConnectionService.OperationName,
                Status ?? "running",
                idempotencyKey,
                "owner",
                "filesystem",
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
        public List<MemOperatorAuditEventWrite> Events { get; } = [];

        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
