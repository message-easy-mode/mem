using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Operations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Runtime;

public sealed class CoturnPlatformMaintenanceOperationTests
{
    [Fact]
    public async Task Restart_and_verify_runs_under_a_durable_operation_and_accepts_warning_level_functional_evidence()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Warning);
        var processor = fixture.CreateProcessor(runtime);
        var operationId = await fixture.StartAsync(
            CoturnPlatformMaintenanceActions.RestartVerify);

        await processor.ExecuteAsync(
            operationId,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify,
                IdempotencyKey: "restart-test"));

        var operation = await fixture.Operations.FindByIdAsync(
            operationId,
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal("succeeded", operation!.Status);
        Assert.Equal("completed", operation.CurrentStep);
        Assert.Equal(1, runtime.RestartCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Equal(1, runtime.InspectCalls);
        Assert.Equal(0, runtime.RepairCalls);
        Assert.Contains("restart-verify", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("warning", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("requestAbortCancelsMutation", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repair_force_recreates_then_requires_structural_and_functional_verification()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed);
        var processor = fixture.CreateProcessor(runtime);
        var operationId = await fixture.StartAsync(
            CoturnPlatformMaintenanceActions.Repair);

        await processor.ExecuteAsync(
            operationId,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.Repair,
                ExternalIp: "203.0.113.9",
                IdempotencyKey: "repair-test"));

        var operation = await fixture.Operations.FindByIdAsync(
            operationId,
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal("succeeded", operation!.Status);
        Assert.Equal(1, runtime.RepairCalls);
        Assert.Equal(1, runtime.InspectCalls);
        Assert.Equal("203.0.113.9", runtime.LastRepairExternalIp);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Contains("repair", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("203.0.113.9", operation.InputJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_functional_verification_fails_the_durable_operation_with_correlated_safe_evidence()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Failed,
            incidentId: "inc_coturn_functional_failure");
        var processor = fixture.CreateProcessor(runtime);
        var operationId = await fixture.StartAsync(
            CoturnPlatformMaintenanceActions.RestartVerify);

        await processor.ExecuteAsync(
            operationId,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify));

        var operation = await fixture.Operations.FindByIdAsync(
            operationId,
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal("failed", operation!.Status);
        Assert.Equal("verify-platform-turn-functional", operation.CurrentStep);
        Assert.Contains("functional TURN verification failed", operation.LastError ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inc_coturn_functional_failure", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_failure_persists_bounded_sanitized_recent_logs_with_the_failed_operation()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed,
            restartFailure: new InvalidOperationException("bounded restart failure"));
        var processor = fixture.CreateProcessor(runtime);
        var operationId = await fixture.StartAsync(
            CoturnPlatformMaintenanceActions.RestartVerify);

        await processor.ExecuteAsync(
            operationId,
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify));

        var operation = await fixture.Operations.FindByIdAsync(
            operationId,
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal("failed", operation!.Status);
        Assert.Contains("sanitized coturn log line", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admission_replays_the_same_idempotency_key_and_request_abort_after_enqueue_does_not_cancel_the_accepted_operation()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        using var requestCancellation = new CancellationTokenSource();
        var dispatcher = new RecordingDispatcher(() => requestCancellation.Cancel());
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());
        var request = new CoturnPlatformMaintenanceRequest(
            CoturnPlatformMaintenanceActions.RestartVerify,
            IdempotencyKey: "browser-retry-1");

        var first = await service.AcceptAsync(
            request,
            "owner",
            requestCancellation.Token);

        Assert.True(requestCancellation.IsCancellationRequested);
        Assert.False(first.ReusedExistingOperation);
        Assert.Equal(1, dispatcher.EnqueueCalls);

        using var replayCancellation = new CancellationTokenSource();
        var replay = await service.AcceptAsync(
            request,
            "owner",
            replayCancellation.Token);

        Assert.Equal(first.OperationId, replay.OperationId);
        Assert.True(replay.ReusedExistingOperation);
        Assert.Equal(1, dispatcher.EnqueueCalls);

        var operation = await fixture.Operations.FindByIdAsync(
            first.OperationId,
            CancellationToken.None);
        Assert.NotNull(operation);
        Assert.Equal("queued", operation!.CurrentStep);
        Assert.Contains("coturn-container-1", operation.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("requestAbortCancelsMutation", operation.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.InputJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admission_rejects_maintenance_while_platform_install_is_active()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var installOperationId = await fixture.Operations.StartAsync(
            runtimeStackId: null,
            operation: CoturnPlatformInstallOperationService.OperationName,
            idempotencyKey: null,
            requestedBy: "owner",
            hostMutationLevel: "docker,platform-turn,host-ports",
            input: new { imageBoundary = "installation-approved-immutable" },
            CancellationToken.None);

        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var exception = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() =>
            service.AcceptAsync(
                new CoturnPlatformMaintenanceRequest(
                    CoturnPlatformMaintenanceActions.RestartVerify,
                    IdempotencyKey: "restart-during-install"),
                "owner",
                CancellationToken.None));

        Assert.NotEqual(Guid.Empty, installOperationId);
        Assert.Equal("coturn_operation_in_progress", exception.Code);
        Assert.Equal(0, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Admission_rejects_restart_when_protected_authority_is_restricted()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed,
            runtime: ReadyRuntime() with
            {
                Status = "protected_evidence_restricted",
                Readiness = "verification-limited",
                OperatorStatus = CoturnOperatorStatuses.VerificationLimited,
                ProtectedEvidenceAccess = CoturnProtectedEvidenceAccess.Restricted
            });
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var exception = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() =>
            service.AcceptAsync(
                new CoturnPlatformMaintenanceRequest(
                    CoturnPlatformMaintenanceActions.RestartVerify,
                    IdempotencyKey: "restricted-restart"),
                "owner",
                CancellationToken.None));

        Assert.Equal("coturn_maintenance_authority_required", exception.Code);
        Assert.Equal(0, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Admission_requires_an_idempotency_key_before_any_mutation_is_enqueued()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var exception = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() =>
            service.AcceptAsync(
                new CoturnPlatformMaintenanceRequest(
                    CoturnPlatformMaintenanceActions.RestartVerify),
                "owner",
                CancellationToken.None));

        Assert.Equal("coturn_maintenance_idempotency_key_invalid", exception.Code);
        Assert.Equal(0, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Reusing_an_idempotency_key_for_different_repair_input_is_rejected()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var first = await service.AcceptAsync(
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.Repair,
                ExternalIp: "203.0.113.9",
                IdempotencyKey: "repair-retry"),
            "owner",
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() =>
            service.AcceptAsync(
                new CoturnPlatformMaintenanceRequest(
                    CoturnPlatformMaintenanceActions.Repair,
                    ExternalIp: "203.0.113.10",
                    IdempotencyKey: "repair-retry"),
                "owner",
                CancellationToken.None));

        Assert.False(first.ReusedExistingOperation);
        Assert.Equal("coturn_maintenance_idempotency_conflict", exception.Code);
        Assert.Equal(1, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Admission_allows_restart_when_restart_policy_is_the_only_safe_runtime_drift()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed,
            runtime: ReadyRuntime() with
            {
                Status = "runtime_drift",
                Readiness = "repair-required",
                OperatorStatus = CoturnOperatorStatuses.RepairRequired,
                RuntimeExact = false,
                RuntimeDrift = ["restart-policy"]
            });
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var accepted = await service.AcceptAsync(
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify,
                IdempotencyKey: "restart-policy-correction"),
            "owner",
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, accepted.OperationId);
        Assert.Equal(1, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Admission_allows_no_pull_repair_of_a_missing_container_when_the_approved_image_is_local()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed,
            runtime: ReadyRuntime() with
            {
                Status = "not_deployed",
                ContainerState = "not-deployed",
                Readiness = "not-deployed",
                OperatorStatus = CoturnOperatorStatuses.NotDeployed,
                RuntimeExact = false,
                ContainerExists = false,
                Running = false,
                OwnershipVerified = false,
                ContainerId = null,
                DockerState = null,
                ImageApproved = false,
                RuntimeDrift = []
            });
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var accepted = await service.AcceptAsync(
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.Repair,
                IdempotencyKey: "repair-missing-container"),
            "owner",
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, accepted.OperationId);
        Assert.Equal(1, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Admission_rejects_operational_repair_when_the_approved_image_is_not_available_locally()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed,
            runtime: ReadyRuntime() with
            {
                ResolvedImageId = null,
                ImageApproved = false,
                RuntimeExact = false,
                OperatorStatus = CoturnOperatorStatuses.RepairRequired
            });
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var exception = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() =>
            service.AcceptAsync(
                new CoturnPlatformMaintenanceRequest(
                    CoturnPlatformMaintenanceActions.Repair,
                    IdempotencyKey: "repair-no-local-image"),
                "owner",
                CancellationToken.None));

        Assert.Equal("coturn_repair_approved_image_unavailable", exception.Code);
        Assert.Equal(0, dispatcher.EnqueueCalls);
    }

    [Fact]
    public async Task Active_maintenance_can_be_rediscovered_after_the_accepting_request_is_gone()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(
            checkStatus: CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db,
            fixture.Operations,
            dispatcher,
            runtime,
            new CoturnPlatformMutationAdmissionGate());

        var accepted = await service.AcceptAsync(
            new CoturnPlatformMaintenanceRequest(
                CoturnPlatformMaintenanceActions.RestartVerify,
                IdempotencyKey: "rediscover-restart"),
            "owner",
            CancellationToken.None);

        var active = await service.FindActiveAsync(CancellationToken.None);

        Assert.True(active.Active);
        Assert.NotNull(active.Operation);
        Assert.Equal(accepted.OperationId, active.Operation!.OperationId);
        Assert.Equal(CoturnPlatformMaintenanceActions.RestartVerify, active.Operation.Action);
        Assert.Equal("queued", active.Operation.CurrentStep);
        Assert.False(active.Operation.Terminal);
    }

    [Theory]
    [InlineData(" 8.8.8.8 ", "8.8.8.8")]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public async Task Network_setting_apply_on_a_healthy_runtime_uses_repair_and_replays_exact_reviewed_intent(
        string? enteredIp, string? normalizedIp)
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(CoturnCheckStatuses.Passed,
            runtime: ReadyRuntime() with { ExternalIp = "1.1.1.1" });
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db, fixture.Operations, dispatcher, runtime,
            new CoturnPlatformMutationAdmissionGate());
        var request = new CoturnPlatformMaintenanceRequest(
            CoturnPlatformMaintenanceActions.Repair, enteredIp, "apply-reviewed-network-setting");

        var accepted = await service.AcceptAsync(request, "owner", CancellationToken.None);
        var replay = await service.AcceptAsync(request with { ExternalIp = normalizedIp },
            "owner", CancellationToken.None);
        Assert.Equal(accepted.OperationId, replay.OperationId);
        Assert.True(replay.ReusedExistingOperation);
        Assert.Equal(1, dispatcher.EnqueueCalls);
        Assert.NotNull(dispatcher.LastRequest);
        Assert.Equal(CoturnPlatformMaintenanceActions.Repair, dispatcher.LastRequest!.Action);
        Assert.Equal(normalizedIp, dispatcher.LastRequest.ExternalIp);
        Assert.Equal("apply-reviewed-network-setting", dispatcher.LastRequest.IdempotencyKey);

        await fixture.CreateProcessor(runtime).ExecuteAsync(accepted.OperationId, dispatcher.LastRequest);
        Assert.Equal(1, runtime.RepairCalls);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(normalizedIp, runtime.LastRepairExternalIp);
        Assert.Equal(1, runtime.CheckCalls);
        var operation = await fixture.Operations.FindByIdAsync(accepted.OperationId, CancellationToken.None);
        Assert.Equal("succeeded", operation!.Status);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("not-an-ip")]
    public async Task Plain_restart_rejects_network_configuration_instead_of_dropping_it_or_escalating_authority(string value)
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db, fixture.Operations, dispatcher, runtime,
            new CoturnPlatformMutationAdmissionGate());

        var error = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() => service.AcceptAsync(
            new CoturnPlatformMaintenanceRequest(CoturnPlatformMaintenanceActions.RestartVerify,
                value, "must-not-change-config"), "owner", CancellationToken.None));
        Assert.Equal("coturn_restart_configuration_not_allowed", error.Code);
        Assert.Equal(400, error.StatusCode);
        Assert.Equal(0, dispatcher.EnqueueCalls);
        Assert.Equal(0, runtime.InspectCalls);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(0, runtime.RepairCalls);
        Assert.Equal(0, await fixture.Db.RuntimeOperations.CountAsync());
    }

    [Fact]
    public async Task Invalid_external_ip_is_rejected_before_any_runtime_mutation()
    {
        await using var fixture = await OperationFixture.CreateAsync();
        var dispatcher = new RecordingDispatcher();
        var runtime = new RecordingMaintenanceRuntime(CoturnCheckStatuses.Passed);
        var service = new CoturnPlatformMaintenanceOperationService(
            fixture.Db, fixture.Operations, dispatcher, runtime,
            new CoturnPlatformMutationAdmissionGate());

        var error = await Assert.ThrowsAsync<CoturnPlatformMaintenanceException>(() => service.AcceptAsync(
            new CoturnPlatformMaintenanceRequest(CoturnPlatformMaintenanceActions.Repair,
                "8.8.8.8/172.18.0.5", "invalid-mapping"), "owner", CancellationToken.None));
        Assert.Equal("coturn_external_ip_invalid", error.Code);
        Assert.Equal(0, dispatcher.EnqueueCalls);
        Assert.Equal(0, runtime.InspectCalls);
    }

    private static CoturnRuntimeResponse ReadyRuntime() =>
        new(
            Source: "control-plane",
            Status: "ok",
            ContainerState: "running",
            Readiness: "ready",
            ServiceKey: "coturn",
            ContainerName: "mem-coturn",
            Image: "sha256:approved",
            ApprovedImageReference: "coturn/coturn@sha256:approved",
            ResolvedImageId: "sha256:approved",
            ImageApproved: true,
            ContainerExists: true,
            Running: true,
            OwnershipVerified: true,
            ContainerId: "coturn-container-1",
            DockerState: "running",
            OperatorStatus: CoturnOperatorStatuses.RuntimeReady,
            RuntimeExact: true,
            DockerRuntime: null,
            RuntimeDrift: [],
            ProtectedEvidenceAccess: CoturnProtectedEvidenceAccess.Available,
            Realm: "example.test",
            PublicHost: "turn.example.test",
            TurnPort: 3478,
            RelayMinPort: 49160,
            RelayMaxPort: 49200,
            TurnUris: ["turn:turn.example.test:3478?transport=udp"],
            SecretPresent: true,
            SecretSource: "protected-file",
            SecretStorage: "protected-host-file",
            SecretFilePermissionsApplied: true,
            ExpectedBaseDomain: "example.test",
            ConfiguredBaseDomain: "example.test",
            DomainDriftDetected: false,
            Recreated: false,
            ExternalIp: null,
            RelayPortsPublished: true,
            SecurityPolicyApplied: true,
            SecurityPolicyVersion: "mem-coturn-v1",
            PublishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
            RequiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
            Warnings: [],
            Detail: "ready");

    private sealed class RecordingMaintenanceRuntime : ICoturnPlatformMaintenanceRuntime
    {
        private readonly string _checkStatus;
        private readonly string? _incidentId;
        private readonly CoturnRuntimeResponse _runtime;
        private readonly Exception? _restartFailure;

        public RecordingMaintenanceRuntime(
            string checkStatus,
            string? incidentId = null,
            CoturnRuntimeResponse? runtime = null,
            Exception? restartFailure = null)
        {
            _checkStatus = checkStatus;
            _incidentId = incidentId;
            _runtime = runtime ?? ReadyRuntime();
            _restartFailure = restartFailure;
        }

        public int RestartCalls { get; private set; }
        public int RepairCalls { get; private set; }
        public int InspectCalls { get; private set; }
        public int CheckCalls { get; private set; }
        public string? LastRepairExternalIp { get; private set; }

        public Task<CoturnRuntimeResponse> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCalls++;
            return Task.FromResult(_runtime);
        }

        public Task<CoturnRuntimeResponse> RestartOwnedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestartCalls++;
            if (_restartFailure is not null)
            {
                return Task.FromException<CoturnRuntimeResponse>(_restartFailure);
            }

            return Task.FromResult(_runtime);
        }

        public Task<CoturnRuntimeResponse> RepairAsync(
            string? externalIp,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepairCalls++;
            LastRepairExternalIp = externalIp;
            return Task.FromResult(_runtime);
        }

        public Task<CoturnCheckResponse> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCalls++;
            var checkedAt = DateTimeOffset.Parse("2026-08-23T09:00:00Z");
            var allocationStatus = _checkStatus == CoturnCheckStatuses.Failed
                ? CoturnCheckStatuses.Failed
                : CoturnCheckStatuses.Passed;

            return Task.FromResult(new CoturnCheckResponse(
                Source: "control-plane",
                Status: _checkStatus,
                CheckedAtUtc: checkedAt,
                FreshUntilUtc: checkedAt.AddMinutes(30),
                ContainerState: "running",
                Readiness: "ready",
                PublicHost: "turn.example.test",
                RuntimeContainerId: "coturn-container-1",
                RuntimeStartedAtUtc: checkedAt.AddMinutes(-1),
                RuntimeRestartCount: 0,
                Checks:
                [
                    new CoturnCheckItem(
                        "container",
                        _checkStatus == CoturnCheckStatuses.Failed
                            ? CoturnCheckStatuses.Failed
                            : CoturnCheckStatuses.Passed,
                        "bounded")
                ],
                Allocation: new CoturnAllocationProbeResponse(
                    allocationStatus,
                    "udp",
                    "bounded",
                    null),
                Warnings: _checkStatus == CoturnCheckStatuses.Warning
                    ? ["Automatic external-IP detection remains warning-level evidence."]
                    : [],
                Detail: "bounded",
                EvidencePersisted: true,
                IncidentId: _incidentId));
        }

        public Task<CoturnLogsResponse> GetRecentLogsAsync(
            int? tail,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new CoturnLogsResponse(
                Source: "control-plane",
                Status: "ok",
                RetrievedAtUtc: DateTimeOffset.Parse("2026-08-23T09:01:00Z"),
                ContainerName: "mem-coturn",
                RequestedTail: tail ?? 120,
                ReturnedLines: 1,
                Truncated: false,
                Content: "sanitized coturn log line",
                Warnings: []));
        }
    }

    private sealed class RecordingDispatcher(Action? onEnqueue = null) :
        ICoturnPlatformMaintenanceDispatcher
    {
        public int EnqueueCalls { get; private set; }
        public CoturnPlatformMaintenanceRequest? LastRequest { get; private set; }

        public bool TryEnqueue(
            Guid operationId,
            CoturnPlatformMaintenanceRequest request)
        {
            EnqueueCalls++;
            LastRequest = request;
            onEnqueue?.Invoke();
            return true;
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class OperationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private OperationFixture(
            SqliteConnection connection,
            MemDbContext db,
            RuntimeOperationStore operations)
        {
            _connection = connection;
            Db = db;
            Operations = operations;
        }

        public MemDbContext Db { get; }
        public RuntimeOperationStore Operations { get; }

        public static async Task<OperationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new OperationFixture(
                connection,
                db,
                new RuntimeOperationStore(db));
        }

        public CoturnPlatformMaintenanceOperationProcessor CreateProcessor(
            ICoturnPlatformMaintenanceRuntime runtime) =>
            new(
                runtime,
                Operations,
                new CoturnPlatformMaintenanceOperationLifetime(
                    new FakeHostApplicationLifetime()),
                NullLogger<CoturnPlatformMaintenanceOperationProcessor>.Instance);

        public Task<Guid> StartAsync(string action) =>
            Operations.StartAsync(
                runtimeStackId: null,
                operation: CoturnPlatformMaintenanceOperationService.OperationName,
                idempotencyKey: null,
                requestedBy: "owner",
                hostMutationLevel: "docker,platform-turn",
                input: new { action },
                CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
