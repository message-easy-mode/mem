using System.Text.Json;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Services.Identity;
using Modules.Integrations.Npm.Contracts;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class RuntimeStackFederationApplyServiceTests
{
    [Fact]
    public async Task Applies_and_verifies_a_Public_to_Restricted_transition()
    {
        var fixture = CreateFixture();
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Restricted));
        var request = ApplyRequest(fixture.State.State, FederationModes.Restricted, ["partner.example"]);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("succeeded", result!.Status);
        Assert.Equal(FederationModes.Restricted, result.ObservedMode);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
        Assert.Equal(0, fixture.Transaction.RestoreCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);
        Assert.Equal("succeeded", fixture.Operations.Detail!.Status);
        Assert.Contains(fixture.Audit.Events, x => x.EventType == "federation.policy.apply.succeeded");
    }

    [Fact]
    public async Task Rejects_a_stale_review_before_creating_an_operation()
    {
        var fixture = CreateFixture();
        var request = new RuntimeStackFederationApplyRequest(
            FederationModes.Restricted,
            ["partner.example"],
            "sha256:" + new string('0', 64),
            "federation-test-stale");

        var error = await Assert.ThrowsAsync<FederationApplyBlockedException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Manifest.Slug,
                request,
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("federation_review_stale", error.Code);
        Assert.Equal(0, fixture.Operations.StartCount);
        Assert.Equal(0, fixture.Transaction.ApplyCount);
    }

    [Fact]
    public async Task Candidate_rejection_never_replaces_the_active_config()
    {
        var fixture = CreateFixture();
        fixture.Candidate.Result = new SynapseFederationCandidateValidationResult(
            false,
            1,
            "matrixdotorg/synapse:latest",
            false,
            "candidate rejected",
            "federation_config_validation_failed",
            "Rejected.");
        var request = ApplyRequest(fixture.State.State, FederationModes.Restricted, ["partner.example"]);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("candidate_rejected", result!.Status);
        Assert.False(result.RollbackAttempted);
        Assert.Equal(0, fixture.Transaction.ApplyCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
        Assert.Equal("failed", fixture.Operations.Detail!.Status);
    }


    [Fact]
    public async Task Refuses_a_changed_active_config_before_mutation_without_attempting_rollback()
    {
        var fixture = CreateFixture();
        fixture.Transaction.ThrowStateChangedOnApply = true;
        var request = ApplyRequest(fixture.State.State, FederationModes.Restricted, ["partner.example"]);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("failed", result!.Status);
        Assert.Equal("federation_review_stale", result.ErrorCode);
        Assert.False(result.RollbackAttempted);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
        Assert.Equal(0, fixture.Transaction.RestoreCount);
        Assert.Equal(0, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Verification_failure_restores_restarts_and_verifies_the_previous_policy()
    {
        var fixture = CreateFixture();
        fixture.Verifier.Results.Enqueue(Verification(false, FederationModes.Restricted));
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Public));
        var request = ApplyRequest(fixture.State.State, FederationModes.Restricted, ["partner.example"]);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("rolled_back", result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(FederationModes.Public, result.ObservedMode);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
        Assert.Equal(1, fixture.Transaction.RestoreCount);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
        Assert.Equal("rolled_back", fixture.Operations.Detail!.Status);
        Assert.Contains(fixture.Audit.Events, x => x.EventType == "federation.policy.apply.rolled_back");
    }

    [Fact]
    public async Task Rollback_failure_returns_a_truthful_manual_recovery_outcome()
    {
        var fixture = CreateFixture();
        fixture.Verifier.Results.Enqueue(Verification(false, FederationModes.Restricted));
        fixture.Transaction.ThrowOnRestore = true;
        var request = ApplyRequest(fixture.State.State, FederationModes.Restricted, ["partner.example"]);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("failed", result!.Status);
        Assert.True(result.RollbackAttempted);
        Assert.False(result.RollbackSucceeded);
        Assert.Equal("federation_rollback_failed", result.ErrorCode);
        Assert.Equal("failed", fixture.Operations.Detail!.Status);
    }

    [Fact]
    public async Task Rejects_reuse_of_an_idempotency_key_for_different_canonical_input()
    {
        var fixture = CreateFixture();
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Restricted));
        const string idempotencyKey = "federation-test-reused-key";
        var firstPolicy = new CanonicalFederationPolicyRequest(
            FederationModes.Restricted,
            ["partner.example"]);
        var first = new RuntimeStackFederationApplyRequest(
            firstPolicy.Mode,
            firstPolicy.Allowlist,
            FederationReviewHash.Compute(fixture.State.State.StateFingerprint!, firstPolicy),
            idempotencyKey);
        await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            first,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        var secondPolicy = new CanonicalFederationPolicyRequest(
            FederationModes.Restricted,
            ["other.example"]);
        var second = new RuntimeStackFederationApplyRequest(
            secondPolicy.Mode,
            secondPolicy.Allowlist,
            FederationReviewHash.Compute(fixture.State.State.StateFingerprint!, secondPolicy),
            idempotencyKey);

        var error = await Assert.ThrowsAsync<FederationApplyBlockedException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Manifest.Slug,
                second,
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("federation_idempotency_conflict", error.Code);
        Assert.Equal(1, fixture.Operations.StartCount);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
    }

    [Fact]
    public async Task Rejects_an_active_same_stack_mutating_operation_before_starting()
    {
        var fixture = CreateFixture();
        fixture.Operations.ActiveConflict = new RuntimeOperationSummary(
            Guid.NewGuid(),
            fixture.Manifest.StackId,
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
        var request = ApplyRequest(
            fixture.State.State,
            FederationModes.Restricted,
            ["partner.example"]);

        var error = await Assert.ThrowsAsync<FederationApplyBlockedException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Manifest.Slug,
                request,
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("federation_operation_in_progress", error.Code);
        Assert.Equal(0, fixture.Operations.StartCount);
        Assert.Equal(0, fixture.Transaction.ApplyCount);
    }

    [Fact]
    public async Task Restart_failure_restores_and_restarts_the_previous_policy()
    {
        var fixture = CreateFixture();
        fixture.Lifecycle.ThrowOnRestartCall = 1;
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Public));
        var request = ApplyRequest(
            fixture.State.State,
            FederationModes.Restricted,
            ["partner.example"]);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("rolled_back", result!.Status);
        Assert.Equal("federation_restart_failed", result.ErrorCode);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(1, fixture.Transaction.RestoreCount);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Replays_the_same_completed_idempotent_result_without_a_second_mutation()
    {
        var fixture = CreateFixture();
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Restricted));
        var request = ApplyRequest(fixture.State.State, FederationModes.Restricted, ["partner.example"]);

        var first = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);
        var replay = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(first!.OperationId, replay!.OperationId);
        Assert.Equal("succeeded", replay.Status);
        Assert.Equal(1, fixture.Operations.StartCount);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Enters_Local_only_by_closing_ingress_before_full_verification()
    {
        var fixture = CreateFixture();
        fixture.Verifier.IngressGuardResults.Enqueue(Verification(true, FederationModes.Public));
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.LocalOnly));
        var request = ApplyRequest(fixture.State.State, FederationModes.LocalOnly, []);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("succeeded", result!.Status);
        Assert.Equal(FederationModes.LocalOnly, result.ObservedMode);
        Assert.Equal([FederationModes.LocalOnly], fixture.Ingress.AppliedModes);
        Assert.Equal(1, fixture.Verifier.IngressGuardCount);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Leaves_Local_only_only_after_client_readiness_is_verified()
    {
        var fixture = CreateFixture();
        fixture.State.State = LocalOnlyState(fixture.Manifest);
        fixture.Verifier.ClientReadinessResults.Enqueue(Verification(true, FederationModes.Public));
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Public));
        var request = ApplyRequest(fixture.State.State, FederationModes.Public, []);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("succeeded", result!.Status);
        Assert.Equal([FederationModes.Public], fixture.Ingress.AppliedModes);
        Assert.Equal(1, fixture.Verifier.ClientReadinessCount);
        Assert.Equal(1, fixture.Transaction.ApplyCount);
        Assert.Equal(1, fixture.Lifecycle.RestartCount);
    }

    [Fact]
    public async Task Refuses_Local_only_when_an_alternate_same_upstream_route_exists()
    {
        var fixture = CreateFixture();
        fixture.State.State = PublicState(fixture.Manifest) with
        {
            AlternateMatrixRouteDetected = true
        };
        var request = ApplyRequest(fixture.State.State, FederationModes.LocalOnly, []);

        var error = await Assert.ThrowsAsync<FederationApplyBlockedException>(() =>
            fixture.Service.ApplyAsync(
                fixture.Manifest.Slug,
                request,
                "owner",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("federation_alternate_route_detected", error.Code);
        Assert.Equal(0, fixture.Operations.StartCount);
    }

    [Fact]
    public async Task Local_only_verification_failure_restores_config_and_normal_ingress()
    {
        var fixture = CreateFixture();
        fixture.Verifier.IngressGuardResults.Enqueue(Verification(true, FederationModes.Public));
        fixture.Verifier.Results.Enqueue(Verification(false, FederationModes.LocalOnly));
        fixture.Verifier.Results.Enqueue(Verification(true, FederationModes.Public));
        var request = ApplyRequest(fixture.State.State, FederationModes.LocalOnly, []);

        var result = await fixture.Service.ApplyAsync(
            fixture.Manifest.Slug,
            request,
            "owner",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("rolled_back", result!.Status);
        Assert.True(result.RollbackSucceeded);
        Assert.Equal(1, fixture.Ingress.RestoreCount);
        Assert.Equal(1, fixture.Transaction.RestoreCount);
        Assert.Equal(2, fixture.Lifecycle.RestartCount);
    }

    private static Fixture CreateFixture()
    {
        var manifest = Manifest();
        var state = PublicState(manifest);
        var stateService = new FakeStateService(state);
        var operations = new FakeOperationStore();
        var lifecycle = new FakeLifecycle();
        var candidate = new FakeCandidateValidator();
        var transaction = new FakeConfigTransaction();
        var ingress = new FakeIngressTransaction();
        var verifier = new FakeVerifier();
        var audit = new FakeAudit();
        var service = new RuntimeStackFederationApplyService(
            new FakeManifestResolver(manifest),
            stateService,
            new FederationPolicyRequestValidator(new FederationDomainValidator()),
            operations,
            new RuntimeStackFederationApplyLock(),
            new FakeSnapshotService(),
            lifecycle,
            candidate,
            transaction,
            ingress,
            verifier,
            audit,
            NullLogger<RuntimeStackFederationApplyService>.Instance);

        return new Fixture(
            service,
            manifest,
            stateService,
            operations,
            lifecycle,
            candidate,
            transaction,
            ingress,
            verifier,
            audit);
    }

    private static RuntimeStackFederationApplyRequest ApplyRequest(
        RuntimeStackFederationStateResponse state,
        string mode,
        IReadOnlyList<string> allowlist)
    {
        var canonical = new CanonicalFederationPolicyRequest(mode, allowlist);
        return new RuntimeStackFederationApplyRequest(
            mode,
            allowlist,
            FederationReviewHash.Compute(state.StateFingerprint!, canonical),
            "federation-test-" + Guid.NewGuid().ToString("N"));
    }

    private static RuntimeStackFederationVerificationResult Verification(
        bool succeeded,
        string observedMode) =>
        new(
            succeeded,
            observedMode,
            [
                new FederationCheckResponse(
                    "federation.test",
                    succeeded ? FederationCheckStatuses.Passed : FederationCheckStatuses.Failed,
                    succeeded ? "Passed." : "Failed.")
            ]);

    private static RuntimeStackManifest Manifest() =>
        new(
            Source: "control-plane",
            StackId: Guid.NewGuid(),
            Slug: "federation-test-stack",
            LastVerifiedStatus: "ready",
            LastVerifiedAtUtc: DateTimeOffset.UtcNow,
            Matrix: new RuntimeStackServiceManifest(
                InstanceId: Guid.NewGuid(),
                ServiceKey: "matrix",
                ContainerId: "abcdef123456",
                ContainerName: "mem-matrix-test",
                HostPort: 0,
                DataPath: "/tmp/mem-federation-test",
                ServerName: "example.test",
                PublicHost: "matrix.example.test",
                PublicBaseUrl: "https://matrix.example.test",
                InternalHost: "mem-matrix-test",
                InternalBaseUrl: "http://mem-matrix-test:8008",
                PublicRouteId: "1",
                InternalRouteId: null,
                NpmCertificateId: 1,
                RuntimeMetadata: new Dictionary<string, string?>
                {
                    ["runtimeNetworkName"] = "mem-runtime"
                }),
            Element: null,
            Warnings: [],
            Metadata: new Dictionary<string, string?>());

    private static RuntimeStackFederationStateResponse PublicState(
        RuntimeStackManifest manifest) =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            Mode: FederationModes.Public,
            ConfigurationState: FederationConfigurationStates.Healthy,
            Allowlist: [],
            EnforcementKind: "synapse_unrestricted",
            MatrixContainerRunning: true,
            MatrixDirectHostPortExposed: false,
            IngressMode: FederationIngressModes.Normal,
            ServerWellKnownPublished: true,
            FederationPathsPubliclyForwarded: true,
            SigningKeyPathsPubliclyForwarded: true,
            CanonicalRouteEnabled: true,
            CanonicalRouteTargetsMatrix: true,
            CanonicalCertificatePresent: true,
            AlternateMatrixRouteDetected: false,
            StateFingerprint: "sha256:" + new string('a', 64),
            LatestOperation: null,
            Checks: [],
            Warnings: [],
            Problems: []);

    private static RuntimeStackFederationStateResponse LocalOnlyState(
        RuntimeStackManifest manifest,
        bool alternateRoute = false) =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: manifest.StackId,
            Slug: manifest.Slug,
            Mode: FederationModes.LocalOnly,
            ConfigurationState: FederationConfigurationStates.Healthy,
            Allowlist: [],
            EnforcementKind: "synapse_empty_allowlist_and_npm_ingress",
            MatrixContainerRunning: true,
            MatrixDirectHostPortExposed: false,
            IngressMode: FederationIngressModes.LocalOnly,
            ServerWellKnownPublished: false,
            FederationPathsPubliclyForwarded: false,
            SigningKeyPathsPubliclyForwarded: false,
            CanonicalRouteEnabled: true,
            CanonicalRouteTargetsMatrix: true,
            CanonicalCertificatePresent: true,
            AlternateMatrixRouteDetected: alternateRoute,
            StateFingerprint: "sha256:" + new string('b', 64),
            LatestOperation: null,
            Checks: [],
            Warnings: [],
            Problems: []);

    private sealed record Fixture(
        RuntimeStackFederationApplyService Service,
        RuntimeStackManifest Manifest,
        FakeStateService State,
        FakeOperationStore Operations,
        FakeLifecycle Lifecycle,
        FakeCandidateValidator Candidate,
        FakeConfigTransaction Transaction,
        FakeIngressTransaction Ingress,
        FakeVerifier Verifier,
        FakeAudit Audit);

    private sealed class FakeManifestResolver : IRuntimeStackFederationManifestResolver
    {
        private readonly RuntimeStackManifest _manifest;
        public FakeManifestResolver(RuntimeStackManifest manifest) => _manifest = manifest;
        public Task<RuntimeStackManifest?> FindAsync(string slugOrId, CancellationToken ct) =>
            Task.FromResult<RuntimeStackManifest?>(_manifest);
    }

    private sealed class FakeStateService : IRuntimeStackFederationStateService
    {
        public FakeStateService(RuntimeStackFederationStateResponse state) => State = state;
        public RuntimeStackFederationStateResponse State { get; set; }
        public Task<RuntimeStackFederationStateResponse?> GetAsync(string slugOrId, CancellationToken ct) =>
            Task.FromResult<RuntimeStackFederationStateResponse?>(State);
    }

    private sealed class FakeSnapshotService : IRuntimeStackFederationSnapshotService
    {
        public Task<RuntimeStackFederationSnapshot> CreateAsync(
            RuntimeStackManifest manifest,
            Guid operationId,
            CanonicalFederationPolicyRequest request,
            CancellationToken ct) =>
            Task.FromResult(new RuntimeStackFederationSnapshot(
                "/tmp/op",
                "/tmp/homeserver.yaml",
                "/tmp/before.yaml",
                "/tmp/candidate.yaml",
                "/tmp/npm.json",
                "matrix.example.test",
                new NpmProxyHostSnapshot(
                    1,
                    ["matrix.example.test"],
                    "mem-matrix-test",
                    8008,
                    0,
                    1,
                    "http",
                    string.Empty,
                    [],
                    true,
                    true,
                    true,
                    true,
                    false,
                    true,
                    false,
                    false,
                    false),
                "sha256:before",
                "sha256:candidate",
                "sha256:npm"));
    }

    private sealed class FakeLifecycle : IRuntimeMatrixContainerLifecycleService
    {
        public int RestartCount { get; private set; }
        public int? ThrowOnRestartCall { get; set; }

        public Task<RuntimeMatrixContainerObservation> InspectAsync(
            RuntimeStackServiceManifest matrix,
            CancellationToken ct) =>
            Task.FromResult(Observation());

        public Task<RuntimeMatrixContainerObservation> RestartAsync(
            RuntimeStackServiceManifest matrix,
            CancellationToken ct)
        {
            RestartCount++;
            if (ThrowOnRestartCall == RestartCount)
            {
                throw new TimeoutException("restart failed");
            }

            return Task.FromResult(Observation());
        }

        public Task<string> GetBoundedLogsAsync(string containerId, int tail, CancellationToken ct) =>
            Task.FromResult(string.Empty);

        private static RuntimeMatrixContainerObservation Observation() =>
            new(
                "abcdef123456",
                "mem-matrix-test",
                "matrixdotorg/synapse:latest",
                null,
                true,
                true,
                true,
                true,
                false);
    }

    private sealed class FakeCandidateValidator : ISynapseFederationConfigCandidateValidator
    {
        public SynapseFederationCandidateValidationResult Result { get; set; } = new(
            true,
            0,
            "matrixdotorg/synapse:latest",
            false,
            string.Empty,
            null,
            "Valid.");

        public Task<SynapseFederationCandidateValidationResult> ValidateAsync(
            RuntimeStackManifest manifest,
            RuntimeMatrixContainerObservation container,
            string candidatePath,
            CancellationToken ct) =>
            Task.FromResult(Result);
    }

    private sealed class FakeConfigTransaction : IRuntimeStackFederationConfigTransaction
    {
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }
        public bool ThrowOnRestore { get; set; }
        public bool ThrowStateChangedOnApply { get; set; }

        public Task ApplyCandidateAsync(RuntimeStackFederationSnapshot snapshot, CancellationToken ct)
        {
            ApplyCount++;
            if (ThrowStateChangedOnApply)
            {
                throw new RuntimeStackFederationConfigStateChangedException("changed");
            }

            return Task.CompletedTask;
        }

        public Task RestoreBeforeAsync(RuntimeStackFederationSnapshot snapshot, CancellationToken ct)
        {
            RestoreCount++;
            if (ThrowOnRestore)
            {
                throw new IOException("restore failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeIngressTransaction : IRuntimeStackFederationIngressTransaction
    {
        public int ApplyCount { get; private set; }
        public int RestoreCount { get; private set; }
        public List<string> AppliedModes { get; } = [];
        public bool ThrowOnApply { get; set; }
        public bool ThrowOnRestore { get; set; }

        public Task ApplyModeAsync(
            RuntimeStackFederationSnapshot snapshot,
            string mode,
            CancellationToken ct)
        {
            ApplyCount++;
            AppliedModes.Add(mode);
            if (ThrowOnApply)
            {
                throw new IOException("ingress apply failed");
            }

            return Task.CompletedTask;
        }

        public Task RestoreBeforeAsync(
            RuntimeStackFederationSnapshot snapshot,
            CancellationToken ct)
        {
            RestoreCount++;
            if (ThrowOnRestore)
            {
                throw new IOException("ingress restore failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeVerifier : IRuntimeStackFederationVerifier
    {
        public Queue<RuntimeStackFederationVerificationResult> Results { get; } = new();
        public Queue<RuntimeStackFederationVerificationResult> IngressGuardResults { get; } = new();
        public Queue<RuntimeStackFederationVerificationResult> ClientReadinessResults { get; } = new();
        public int IngressGuardCount { get; private set; }
        public int ClientReadinessCount { get; private set; }

        public Task<RuntimeStackFederationVerificationResult> VerifyAsync(
            RuntimeStackManifest manifest,
            CanonicalFederationPolicyRequest expected,
            CancellationToken ct) =>
            Task.FromResult(Results.Dequeue());

        public Task<RuntimeStackFederationVerificationResult> VerifyLocalOnlyIngressGuardAsync(
            RuntimeStackManifest manifest,
            CancellationToken ct)
        {
            IngressGuardCount++;
            return Task.FromResult(IngressGuardResults.Dequeue());
        }

        public Task<RuntimeStackFederationVerificationResult> VerifyClientReadinessAsync(
            RuntimeStackManifest manifest,
            CancellationToken ct)
        {
            ClientReadinessCount++;
            return Task.FromResult(ClientReadinessResults.Dequeue());
        }
    }

    private sealed class FakeAudit : IMemOperatorAuditService
    {
        public List<MemOperatorAuditEventWrite> Events { get; } = [];
        public Task WriteAsync(MemOperatorAuditEventWrite auditEvent, CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOperationStore : IRuntimeOperationStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        public RuntimeOperationDetail? Detail { get; private set; }
        public int StartCount { get; private set; }
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
            var id = Guid.NewGuid();
            Detail = new RuntimeOperationDetail(
                id,
                runtimeStackId,
                operation,
                "running",
                idempotencyKey,
                requestedBy,
                hostMutationLevel,
                "started",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                null,
                null,
                JsonSerializer.Serialize(input, JsonOptions),
                null,
                null,
                DateTimeOffset.UtcNow.AddMinutes(10));
            return Task.FromResult(id);
        }

        public Task UpdateStepAsync(Guid operationId, string currentStep, CancellationToken ct)
        {
            Detail = Detail! with { CurrentStep = currentStep };
            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            Guid operationId,
            string status,
            string? currentStep,
            object? result,
            object? evidence,
            CancellationToken ct)
        {
            Detail = Detail! with
            {
                Status = status,
                CurrentStep = currentStep,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ResultJson = JsonSerializer.Serialize(result, JsonOptions),
                EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
                LockedUntilUtc = null
            };
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
            Detail = Detail! with
            {
                Status = "failed",
                CurrentStep = currentStep,
                LastError = error,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                ResultJson = result is null ? null : JsonSerializer.Serialize(result, JsonOptions),
                EvidenceJson = JsonSerializer.Serialize(evidence, JsonOptions),
                LockedUntilUtc = null
            };
            return Task.CompletedTask;
        }

        public Task<RuntimeOperationDetail?> FindByIdempotencyKeyAsync(
            Guid runtimeStackId,
            string operation,
            string idempotencyKey,
            CancellationToken ct) =>
            Task.FromResult(
                Detail is not null &&
                Detail.RuntimeStackId == runtimeStackId &&
                Detail.Operation == operation &&
                Detail.IdempotencyKey == idempotencyKey
                    ? Detail
                    : null);

        public Task<RuntimeOperationSummary?> FindActiveMutatingOperationForStackAsync(
            Guid runtimeStackId,
            CancellationToken ct) =>
            Task.FromResult(ActiveConflict);

        public Task<IReadOnlyList<RuntimeOperationSummary>> ListForStackAsync(
            Guid runtimeStackId,
            int limit,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RuntimeOperationSummary>>([]);
    }
}
