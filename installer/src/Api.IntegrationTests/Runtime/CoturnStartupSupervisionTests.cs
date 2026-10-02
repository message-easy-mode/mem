using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.InstallRuns;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Runtime;

public sealed class CoturnStartupSupervisionTests
{
    [Fact]
    public async Task Fresh_first_time_setup_defers_before_coturn_is_expected_and_creates_no_incident()
    {
        await using var fixture = await SupervisionFixture.CreateAsync(coturnExpected: false);
        await fixture.SeedActiveFirstTimeSetupAsync(domainConfigured: false);
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        var outcome = await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected, outcome);
        Assert.Equal(CoturnStartupSupervisionStatuses.NotInstalled, fixture.State.Read().Status);
        Assert.Equal(CoturnStartupSupervisionDecisions.NotEvaluated, fixture.State.Read().Decision);
        Assert.Equal(0, runtime.InspectCalls);
        Assert.Equal(0, runtime.RecoverCalls);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(0, runtime.CheckCalls);
        Assert.Null(await fixture.LatestStartupOperationAsync());
        Assert.DoesNotContain(
            fixture.Diagnostics.Requests,
            request => request.CreateIncident ||
                string.Equals(
                    request.EventCode,
                    CoturnStartupSupervisionEventCodes.Failed,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task Configured_domain_during_active_first_time_setup_still_does_not_make_coturn_expected()
    {
        await using var fixture = await SupervisionFixture.CreateAsync(coturnExpected: false);
        await fixture.SeedActiveFirstTimeSetupAsync(domainConfigured: true);
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        var outcome = await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected, outcome);
        Assert.Equal(CoturnStartupSupervisionStatuses.NotInstalled, fixture.State.Read().Status);
        Assert.Equal(0, runtime.InspectCalls);
        Assert.Null(await fixture.LatestStartupOperationAsync());
        Assert.Empty(fixture.Diagnostics.Requests);
    }

    [Fact]
    public async Task Established_authority_without_current_installation_ledger_preserves_startup_supervision()
    {
        await using var fixture = await SupervisionFixture.CreateAsync(coturnExpected: false);
        await fixture.SeedEstablishedPlatformDomainAsync();
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        // Startup supervision intentionally inspects twice on the healthy path:
        // preflight decision, then verify-startup-runtime before the functional check.
        Assert.Equal(2, runtime.InspectCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Equal(CoturnStartupSupervisionStatuses.Verified, fixture.State.Read().Status);
    }

    [Fact]
    public async Task Successful_coturn_verification_step_makes_service_expected_before_setup_finishes()
    {
        await using var fixture = await SupervisionFixture.CreateAsync(coturnExpected: false);
        await fixture.SeedVerifiedCoturnStepAsync();
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        // Startup supervision intentionally inspects twice on the healthy path:
        // preflight decision, then verify-startup-runtime before the functional check.
        Assert.Equal(2, runtime.InspectCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Equal(CoturnStartupSupervisionStatuses.Verified, fixture.State.Read().Status);
        var operation = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(operation);
        Assert.Equal("succeeded", operation!.Status);
    }


    [Fact]
    public async Task Same_process_recheck_transitions_from_first_time_setup_idle_to_verified_after_coturn_step_succeeds()
    {
        await using var fixture = await SupervisionFixture.CreateAsync(coturnExpected: false);
        await fixture.SeedActiveFirstTimeSetupAsync(domainConfigured: true);
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);
        var processor = fixture.CreateProcessor(runtime);

        var beforeVerification = await processor.ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected, beforeVerification);
        Assert.Equal(CoturnStartupSupervisionStatuses.NotInstalled, fixture.State.Read().Status);
        Assert.Equal(0, runtime.InspectCalls);
        Assert.Equal(0, runtime.CheckCalls);

        await fixture.MarkActiveSetupCoturnVerifiedAsync();

        var afterVerification = await processor.ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.Finished, afterVerification);
        Assert.Equal(CoturnStartupSupervisionStatuses.Verified, fixture.State.Read().Status);
        Assert.Equal(2, runtime.InspectCalls);
        Assert.Equal(1, runtime.CheckCalls);
        var operation = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(operation);
        Assert.Equal("succeeded", operation!.Status);
    }

    [Fact]
    public async Task Exact_stopped_container_is_started_without_recreation_and_then_functionally_verified()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var stopped = StoppedRuntime();
        var runtime = new RecordingStartupRuntime(
            initial: stopped,
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Warning)]);
        var processor = fixture.CreateProcessor(runtime);

        await processor.ExecuteAsync(CancellationToken.None);

        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest!.Status);
        Assert.Equal("docker,platform-turn", latest.HostMutationLevel);
        Assert.Equal(1, runtime.RecoverCalls);
        Assert.Equal(CoturnStartupSupervisionDecisions.StartStopped, runtime.LastRecoveryDecision);
        Assert.Equal("coturn-container-1", runtime.LastExpectedContainerId);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.DoesNotContain("repair", latest.ResultJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mutationPerformed", latest.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(CoturnStartupSupervisionStatuses.Recovered, fixture.State.Read().Status);
    }

    [Fact]
    public async Task Restart_policy_only_drift_is_corrected_in_place_and_functionally_verified()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var drifted = ReadyRuntime() with
        {
            Status = "runtime_drift",
            Readiness = "repair-required",
            OperatorStatus = CoturnOperatorStatuses.RepairRequired,
            RuntimeExact = false,
            RuntimeDrift = ["restart-policy"],
            DockerRuntime = ReadyDocker() with
            {
                RestartPolicy = "no",
                RestartPolicyMatches = false
            }
        };
        var runtime = new RecordingStartupRuntime(
            initial: drifted,
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest!.Status);
        Assert.Equal("docker,platform-turn", latest.HostMutationLevel);
        Assert.Equal(1, runtime.RecoverCalls);
        Assert.Equal(
            CoturnStartupSupervisionDecisions.CorrectRestartPolicy,
            runtime.LastRecoveryDecision);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Equal(CoturnStartupSupervisionStatuses.Recovered, fixture.State.Read().Status);

        var driftEvent = Assert.Single(
            fixture.Diagnostics.Requests,
            request => string.Equals(
                request.EventCode,
                CoturnStartupSupervisionEventCodes.RestartPolicyDrift,
                StringComparison.Ordinal));
        Assert.Equal(MemDiagnosticSeverities.Warning, driftEvent.Severity);
        Assert.NotNull(driftEvent.OperationId);
        Assert.Null(driftEvent.IncidentId);
        Assert.False(driftEvent.CreateIncident);
    }

    [Fact]
    public async Task Functional_failure_gets_exactly_one_automatic_restart_and_can_recover()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks:
            [
                Check(CoturnCheckStatuses.Failed, "inc_first_check"),
                Check(CoturnCheckStatuses.Passed)
            ]);
        var processor = fixture.CreateProcessor(runtime);

        await processor.ExecuteAsync(CancellationToken.None);

        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest!.Status);
        Assert.Equal("docker,platform-turn", latest.HostMutationLevel);
        Assert.Equal(1, runtime.RestartCalls);
        Assert.Equal(2, runtime.CheckCalls);
        Assert.Contains("automaticRestartAttempted", latest.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("true", latest.EvidenceJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CoturnStartupSupervisionStatuses.Recovered, fixture.State.Read().Status);
    }

    [Fact]
    public async Task Probe_runtime_dns_warning_does_not_restart_exact_coturn()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [ProbeRuntimeDnsWarningCheck()]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest!.Status);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Equal(CoturnStartupSupervisionStatuses.Verified, fixture.State.Read().Status);
    }

    [Fact]
    public async Task External_or_prerequisite_functional_failure_does_not_speculatively_restart_exact_coturn()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [DependencyFailureCheck()]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        var failed = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(failed);
        Assert.Equal("failed", failed!.Status);
        Assert.Equal("docker,platform-turn", failed.HostMutationLevel);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Contains("cooldownEligible", failed.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("false", failed.EvidenceJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var laterRuntime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(laterRuntime).ExecuteAsync(CancellationToken.None);

        Assert.False(fixture.State.Read().CooldownActive);
        Assert.Equal(CoturnStartupSupervisionStatuses.Verified, fixture.State.Read().Status);
        Assert.Equal(0, laterRuntime.RestartCalls);
    }

    [Fact]
    public async Task Repeated_functional_failure_stops_after_one_restart_and_enters_cooldown_on_next_process_pass()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var firstRuntime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks:
            [
                Check(CoturnCheckStatuses.Failed, "inc_first"),
                Check(CoturnCheckStatuses.Failed, "inc_second")
            ]);

        await fixture.CreateProcessor(firstRuntime).ExecuteAsync(CancellationToken.None);

        var failed = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(failed);
        Assert.Equal("failed", failed!.Status);
        Assert.Equal("docker,platform-turn", failed.HostMutationLevel);
        Assert.Contains("cooldownEligible", failed.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("true", failed.EvidenceJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, firstRuntime.RestartCalls);
        Assert.Equal(2, firstRuntime.CheckCalls);

        var secondRuntime = new RecordingStartupRuntime(
            initial: StoppedRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(secondRuntime).ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, secondRuntime.RecoverCalls);
        Assert.Equal(0, secondRuntime.RestartCalls);
        Assert.True(fixture.State.Read().CooldownActive);
        Assert.Equal(CoturnStartupSupervisionStatuses.Cooldown, fixture.State.Read().Status);
    }

    [Fact]
    public async Task Current_unsafe_state_is_not_hidden_by_an_older_automatic_recovery_cooldown()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var failedRuntime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks:
            [
                Check(CoturnCheckStatuses.Failed, "inc_first"),
                Check(CoturnCheckStatuses.Failed, "inc_second")
            ]);
        await fixture.CreateProcessor(failedRuntime).ExecuteAsync(CancellationToken.None);

        var unsafeRuntime = ReadyRuntime() with
        {
            RuntimeExact = false,
            OperatorStatus = CoturnOperatorStatuses.RepairRequired,
            Readiness = "repair-required",
            RuntimeDrift = ["config-mount"],
            DockerRuntime = ReadyDocker() with
            {
                ConfigMountSourceMatches = false
            }
        };
        var current = new RecordingStartupRuntime(
            initial: unsafeRuntime,
            recovered: ReadyRuntime(),
            checks: []);

        await fixture.CreateProcessor(current).ExecuteAsync(CancellationToken.None);

        Assert.False(fixture.State.Read().CooldownActive);
        Assert.Equal(CoturnStartupSupervisionStatuses.RepairRequired, fixture.State.Read().Status);
        Assert.Equal(0, current.RecoverCalls);
        Assert.Equal(0, current.RestartCalls);
        Assert.Equal(0, current.CheckCalls);
        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("failed", latest!.Status);
        Assert.Equal("none", latest.HostMutationLevel);
        Assert.Contains("cooldownEligible", latest.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("false", latest.EvidenceJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unsafe_runtime_drift_fails_closed_and_never_calls_destructive_repair()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var unsafeRuntime = ReadyRuntime() with
        {
            RuntimeExact = false,
            OperatorStatus = CoturnOperatorStatuses.RepairRequired,
            Readiness = "repair-required",
            RuntimeDrift = ["config-mount"],
            DockerRuntime = ReadyDocker() with
            {
                ConfigMountSourceMatches = false
            }
        };
        var runtime = new RecordingStartupRuntime(
            initial: unsafeRuntime,
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("failed", latest!.Status);
        Assert.Equal("none", latest.HostMutationLevel);
        Assert.Contains("cooldownEligible", latest.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(0, runtime.RecoverCalls);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(0, runtime.CheckCalls);
        Assert.Contains("destructiveRepairPerformed", latest.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Equal(CoturnStartupSupervisionStatuses.RepairRequired, fixture.State.Read().Status);

        // A persistent unsafe drift must remain Repair required on a later API
        // process pass. Cooldown exists to stop repeated automatic mutation, not
        // to hide a non-mutating safety refusal.
        var laterRuntime = new RecordingStartupRuntime(
            initial: unsafeRuntime,
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(laterRuntime).ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionStatuses.RepairRequired, fixture.State.Read().Status);
        Assert.False(fixture.State.Read().CooldownActive);
        Assert.Equal(0, laterRuntime.RecoverCalls);
        Assert.Equal(0, laterRuntime.RestartCalls);
    }

    [Fact]
    public async Task Fresh_not_installed_control_plane_is_not_treated_as_a_recovery_failure()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var missing = ReadyRuntime() with
        {
            ContainerExists = false,
            Running = false,
            OwnershipVerified = false,
            ContainerId = null,
            DockerState = null,
            RuntimeExact = false,
            DockerRuntime = null,
            RuntimeDrift = [],
            SecretPresent = false,
            ConfigurationPresent = false,
            ConfigurationExact = false,
            OperatorStatus = CoturnOperatorStatuses.NotDeployed,
            Readiness = "not-deployed"
        };
        var runtime = new RecordingStartupRuntime(
            initial: missing,
            recovered: ReadyRuntime(),
            checks: []);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        Assert.Null(await fixture.LatestStartupOperationAsync());
        Assert.Equal(CoturnStartupSupervisionStatuses.NotInstalled, fixture.State.Read().Status);
        Assert.Equal(0, runtime.RecoverCalls);
        Assert.Equal(0, runtime.RestartCalls);
    }

    [Fact]
    public async Task Abandoned_in_process_coturn_maintenance_is_terminalized_before_startup_supervision_continues()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var abandonedId = await fixture.Operations.StartAsync(
            runtimeStackId: null,
            operation: CoturnPlatformMaintenanceOperationService.OperationName,
            idempotencyKey: "abandoned-maintenance",
            requestedBy: "owner",
            hostMutationLevel: "docker,platform-turn",
            input: new { action = "restart-verify" },
            CancellationToken.None);

        // The operation belongs to the previous API process. Startup
        // reconciliation must not infer abandonment from status alone because a
        // new operator request can be accepted while the hosted supervisor is
        // still settling.
        var abandonedEntity = await fixture.Db.RuntimeOperations
            .SingleAsync(item => item.Id == abandonedId);
        abandonedEntity.RequestedAtUtc = fixture.State.StartupBoundaryAtUtc
            .Subtract(TimeSpan.FromMinutes(1))
            .UtcDateTime;
        abandonedEntity.StartedAtUtc = abandonedEntity.RequestedAtUtc;
        await fixture.Db.SaveChangesAsync();

        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        var abandoned = await fixture.Operations.FindByIdAsync(
            abandonedId,
            CancellationToken.None);
        Assert.NotNull(abandoned);
        Assert.Equal("failed", abandoned!.Status);
        Assert.Contains("abandoned", abandoned.EvidenceJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var startup = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(startup);
        Assert.Equal("succeeded", startup!.Status);
    }

    [Fact]
    public async Task Current_process_operator_mutation_is_not_terminalized_or_competed_with()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var activeId = await fixture.Operations.StartAsync(
            runtimeStackId: null,
            operation: CoturnPlatformMaintenanceOperationService.OperationName,
            idempotencyKey: "current-process-maintenance",
            requestedBy: "owner",
            hostMutationLevel: "docker,platform-turn",
            input: new { action = "restart-verify" },
            CancellationToken.None);
        // Current-process ownership is stronger than the generic durable lock
        // freshness hint. Even if that hint expires while a bounded operation is
        // still running, startup supervision must not begin a competing mutation.
        var activeEntity = await fixture.Db.RuntimeOperations
            .SingleAsync(item => item.Id == activeId);
        activeEntity.LockedUntilUtc = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();

        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        var outcome = await fixture.CreateProcessor(runtime).ExecuteAsync(CancellationToken.None);

        var active = await fixture.Operations.FindByIdAsync(
            activeId,
            CancellationToken.None);
        Assert.NotNull(active);
        Assert.Equal("running", active!.Status);
        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.DeferredCurrentProcessMutation, outcome);
        Assert.Equal(CoturnStartupSupervisionStatuses.Deferred, fixture.State.Read().Status);
        Assert.Equal(activeId, fixture.State.Read().OperationId);
        Assert.Contains("will resume", fixture.State.Read().Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runtime.RecoverCalls);
        Assert.Equal(0, runtime.RestartCalls);
        Assert.Equal(0, runtime.CheckCalls);
        Assert.Null(await fixture.LatestStartupOperationAsync());
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public async Task Terminal_operator_mutation_allows_startup_supervision_to_resume_in_same_api_process(
        string terminalStatus)
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var activeId = await fixture.Operations.StartAsync(
            runtimeStackId: null,
            operation: CoturnPlatformMaintenanceOperationService.OperationName,
            idempotencyKey: $"current-process-{terminalStatus}",
            requestedBy: "owner",
            hostMutationLevel: "docker,platform-turn",
            input: new { action = "restart-verify" },
            CancellationToken.None);
        var runtime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);
        var processor = fixture.CreateProcessor(runtime);

        var deferred = await processor.ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.DeferredCurrentProcessMutation, deferred);
        Assert.Equal(CoturnStartupSupervisionStatuses.Deferred, fixture.State.Read().Status);
        Assert.Equal(activeId, fixture.State.Read().OperationId);

        await fixture.Operations.CompleteAsync(
            activeId,
            status: terminalStatus,
            currentStep: "completed",
            result: new { terminalStatus },
            evidence: new { terminal = true },
            ct: CancellationToken.None);

        var resumed = await processor.ExecuteAsync(CancellationToken.None);

        Assert.Equal(CoturnStartupSupervisionExecutionOutcome.Finished, resumed);
        Assert.Equal(CoturnStartupSupervisionStatuses.Verified, fixture.State.Read().Status);
        Assert.Equal(2, runtime.InspectCalls);
        Assert.Equal(1, runtime.CheckCalls);
        Assert.Equal(0, runtime.RecoverCalls);
        Assert.Equal(0, runtime.RestartCalls);
        var startup = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(startup);
        Assert.Equal("succeeded", startup!.Status);
    }

    [Fact]
    public async Task Control_plane_shutdown_cancels_running_startup_supervision_without_failure_incident()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var runtime = new BlockingCheckStartupRuntime(ReadyRuntime());
        using var stopping = new CancellationTokenSource();

        var execution = fixture.CreateProcessor(runtime).ExecuteAsync(stopping.Token);
        await runtime.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);

        var cancelled = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(cancelled);
        Assert.Equal("cancelled", cancelled!.Status);
        Assert.Equal("control-plane-shutdown", cancelled.CurrentStep);
        Assert.NotNull(cancelled.CompletedAtUtc);
        Assert.Null(cancelled.LastError);
        Assert.Contains(
            "control-plane-shutdown",
            cancelled.ResultJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(
            "control-plane-shutdown",
            cancelled.EvidenceJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(
            "cooldownEligible",
            cancelled.EvidenceJson ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Contains(
            "false",
            cancelled.EvidenceJson ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        var cancellationEvent = Assert.Single(
            fixture.Diagnostics.Requests,
            request => string.Equals(
                request.EventCode,
                "runtime.operation.cancelled",
                StringComparison.Ordinal));
        Assert.Equal(MemDiagnosticSeverities.Warning, cancellationEvent.Severity);
        Assert.False(cancellationEvent.CreateIncident);
        Assert.Null(cancellationEvent.IncidentId);

        Assert.DoesNotContain(
            fixture.Diagnostics.Requests,
            request =>
                request.CreateIncident ||
                string.Equals(
                    request.EventCode,
                    "runtime.operation.failed",
                    StringComparison.Ordinal) ||
                string.Equals(
                    request.EventCode,
                    CoturnStartupSupervisionEventCodes.Failed,
                    StringComparison.Ordinal));
        Assert.False(fixture.State.Read().CooldownActive);
    }

    [Fact]
    public async Task Gracefully_cancelled_startup_supervision_does_not_trigger_cooldown_on_next_pass()
    {
        await using var fixture = await SupervisionFixture.CreateAsync();
        var interruptedRuntime = new BlockingCheckStartupRuntime(ReadyRuntime());
        using var stopping = new CancellationTokenSource();

        var interruptedExecution = fixture
            .CreateProcessor(interruptedRuntime)
            .ExecuteAsync(stopping.Token);
        await interruptedRuntime.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interruptedExecution);

        var cancelled = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(cancelled);
        Assert.Equal("cancelled", cancelled!.Status);

        var nextRuntime = new RecordingStartupRuntime(
            initial: ReadyRuntime(),
            recovered: ReadyRuntime(),
            checks: [Check(CoturnCheckStatuses.Passed)]);

        await fixture.CreateProcessor(nextRuntime).ExecuteAsync(CancellationToken.None);

        var latest = await fixture.LatestStartupOperationAsync();
        Assert.NotNull(latest);
        Assert.Equal("succeeded", latest!.Status);
        Assert.NotEqual(cancelled.Id, latest.Id);
        Assert.False(fixture.State.Read().CooldownActive);
        Assert.Equal(
            CoturnStartupSupervisionStatuses.Verified,
            fixture.State.Read().Status);
        Assert.Equal(1, nextRuntime.CheckCalls);
        Assert.Equal(0, nextRuntime.RestartCalls);
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
            DockerRuntime: ReadyDocker(),
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
            SecurityPolicyVersion: "mem-0.2.0-v2",
            PublishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
            RequiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
            Warnings: [],
            Detail: "ready")
        {
            ConfigurationPresent = true,
            ConfigurationExact = true
        };

    private static CoturnRuntimeResponse StoppedRuntime() =>
        ReadyRuntime() with
        {
            Status = "not_ready",
            ContainerState = "stopped",
            Readiness = "stopped",
            Running = false,
            DockerState = "exited",
            OperatorStatus = CoturnOperatorStatuses.Stopped,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state"],
            RelayPortsPublished = false,
            PublishedPorts = []
        };

    private static CoturnDockerRuntimeEvidence ReadyDocker() =>
        new(
            RestartPolicy: CoturnRuntimePolicy.ExpectedRestartPolicy,
            ExpectedRestartPolicy: CoturnRuntimePolicy.ExpectedRestartPolicy,
            RestartPolicyMatches: true,
            RestartCount: 0,
            Restarting: false,
            Paused: false,
            ExitCode: 0,
            OomKilled: false,
            Dead: false,
            StateErrorPresent: false,
            HealthStatus: null,
            StartedAtUtc: DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            FinishedAtUtc: null,
            NetworkMode: "default",
            ExpectedNetwork: "mem-gateway",
            NetworkModeMatches: false,
            AttachedNetworks: ["mem-gateway"],
            ExpectedNetworkAliases: ["coturn", "mem-coturn"],
            ObservedExpectedNetworkAliases: ["coturn", "mem-coturn"],
            ExpectedNetworkAttached: true,
            NetworkAliasesMatch: true,
            ConfigMountPresent: true,
            ConfigMountReadOnly: true,
            ConfigMountSourceMatches: true,
            ConfigMountDestinationMatches: true,
            CommandMatches: true,
            StartupUserMatches: true);

    private static CoturnCheckResponse Check(string status, string? incidentId = null) =>
        new(
            Source: "control-plane",
            Status: status,
            CheckedAtUtc: DateTimeOffset.Parse("2026-08-23T01:00:00Z"),
            FreshUntilUtc: DateTimeOffset.Parse("2026-08-23T01:30:00Z"),
            ContainerState: "running",
            Readiness: "ready",
            PublicHost: "turn.example.test",
            RuntimeContainerId: "coturn-container-1",
            RuntimeStartedAtUtc: DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            RuntimeRestartCount: 0,
            Checks: [],
            Allocation: new CoturnAllocationProbeResponse(
                status,
                "udp",
                "allocation result",
                LogTail: null),
            Warnings: [],
            Detail: "functional result",
            EvidencePersisted: true,
            IncidentId: incidentId);

    private static CoturnCheckResponse ProbeRuntimeDnsWarningCheck() =>
        Check(CoturnCheckStatuses.Warning) with
        {
            Checks =
            [
                new CoturnCheckItem(
                    Key: "probe-dns",
                    Status: CoturnCheckStatuses.Warning,
                    Summary: "The Coturn probe runtime could not resolve the TURN host.",
                    Detail: "Probe-runtime DNS prerequisite unavailable")
            ],
            Allocation = new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.NotRun,
                Transport: "udp",
                Summary: "Allocation was not run because probe-runtime DNS was unavailable.",
                LogTail: null)
        };

    private static CoturnCheckResponse DependencyFailureCheck() =>
        Check(CoturnCheckStatuses.Failed, "inc_dependency") with
        {
            Checks =
            [
                new CoturnCheckItem(
                    Key: "host-dns",
                    Status: CoturnCheckStatuses.Failed,
                    Summary: "The host resolver is not ready.",
                    Detail: "External/prerequisite failure")
            ],
            Allocation = new CoturnAllocationProbeResponse(
                Status: CoturnCheckStatuses.NotRun,
                Transport: "udp",
                Summary: "Allocation was not run because DNS was unavailable.",
                LogTail: null)
        };

    private sealed class BlockingCheckStartupRuntime : ICoturnStartupSupervisionRuntime
    {
        private readonly CoturnRuntimeResponse _runtime;

        public BlockingCheckStartupRuntime(CoturnRuntimeResponse runtime)
        {
            _runtime = runtime;
        }

        public TaskCompletionSource<bool> CheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CoturnRuntimeResponse> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_runtime);
        }

        public Task<CoturnStartupRecoveryMutationResult> RecoverStartupAsync(
            string decision,
            string expectedContainerId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The blocking shutdown fixture must not enter startup recovery.");

        public Task<CoturnRuntimeResponse> RestartOwnedAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "The blocking shutdown fixture must not restart Coturn.");

        public async Task<CoturnCheckResponse> CheckAsync(CancellationToken cancellationToken)
        {
            CheckStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The blocking Coturn check unexpectedly completed.");
        }

        public Task<CoturnLogsResponse> GetRecentLogsAsync(
            int? tail,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                "Graceful Control Plane shutdown must not collect failure logs.");
    }

    private sealed class RecordingStartupRuntime : ICoturnStartupSupervisionRuntime
    {
        private readonly CoturnRuntimeResponse _initial;
        private readonly CoturnRuntimeResponse _recovered;
        private readonly Queue<CoturnCheckResponse> _checks;
        private bool _recoveryComplete;

        public RecordingStartupRuntime(
            CoturnRuntimeResponse initial,
            CoturnRuntimeResponse recovered,
            IEnumerable<CoturnCheckResponse> checks)
        {
            _initial = initial;
            _recovered = recovered;
            _checks = new Queue<CoturnCheckResponse>(checks);
        }

        public int InspectCalls { get; private set; }
        public int RecoverCalls { get; private set; }
        public int RestartCalls { get; private set; }
        public int CheckCalls { get; private set; }
        public string? LastRecoveryDecision { get; private set; }
        public string? LastExpectedContainerId { get; private set; }

        public Task<CoturnRuntimeResponse> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCalls++;
            return Task.FromResult(_recoveryComplete ? _recovered : _initial);
        }

        public Task<CoturnStartupRecoveryMutationResult> RecoverStartupAsync(
            string decision,
            string expectedContainerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RecoverCalls++;
            LastRecoveryDecision = decision;
            LastExpectedContainerId = expectedContainerId;
            _recoveryComplete = true;
            return Task.FromResult(new CoturnStartupRecoveryMutationResult(
                _recovered,
                MutationPerformed: true,
                ContainerStarted: string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.StartStopped,
                    StringComparison.Ordinal),
                RestartPolicyCorrected: string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.CorrectRestartPolicy,
                    StringComparison.Ordinal)));
        }

        public Task<CoturnRuntimeResponse> RestartOwnedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestartCalls++;
            _recoveryComplete = true;
            return Task.FromResult(_recovered);
        }

        public Task<CoturnCheckResponse> CheckAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCalls++;
            if (_checks.Count == 0)
            {
                throw new InvalidOperationException("No fake Coturn check remains.");
            }

            return Task.FromResult(_checks.Dequeue());
        }

        public Task<CoturnLogsResponse> GetRecentLogsAsync(
            int? tail,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CoturnLogsResponse(
                Source: "control-plane",
                Status: "available",
                RetrievedAtUtc: DateTimeOffset.Parse("2026-08-23T01:00:00Z"),
                ContainerName: "mem-coturn",
                RequestedTail: tail ?? 120,
                ReturnedLines: 1,
                Truncated: false,
                Content: "sanitized startup failure log",
                Warnings: []));
    }

    private sealed class SupervisionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private SupervisionFixture(
            SqliteConnection connection,
            MemDbContext db,
            RuntimeOperationStore operations,
            MemControlPlaneRuntimeContext runtimeContext,
            CoturnStartupSupervisionState state,
            RecordingDiagnosticWriter diagnostics)
        {
            _connection = connection;
            Db = db;
            Operations = operations;
            RuntimeContext = runtimeContext;
            State = state;
            Diagnostics = diagnostics;
        }

        public MemDbContext Db { get; }
        public RuntimeOperationStore Operations { get; }
        public MemControlPlaneRuntimeContext RuntimeContext { get; }
        public CoturnStartupSupervisionState State { get; }
        public RecordingDiagnosticWriter Diagnostics { get; }

        public static async Task<SupervisionFixture> CreateAsync(bool coturnExpected = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();
            if (coturnExpected)
            {
                var now = DateTime.UtcNow;
                db.Installations.Add(new InstallationEntity
                {
                    Id = Guid.NewGuid(),
                    Status = InstallationStatuses.Succeeded,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    StartedAtUtc = now,
                    CompletedAtUtc = now
                });
                await db.SaveChangesAsync();
            }
            var runtimeContext = CreateRuntimeContext();
            var timeProvider = TimeProvider.System;
            var diagnostics = new RecordingDiagnosticWriter();
            return new SupervisionFixture(
                connection,
                db,
                new RuntimeOperationStore(db, diagnostics),
                runtimeContext,
                new CoturnStartupSupervisionState(runtimeContext, timeProvider),
                diagnostics);
        }

        public async Task SeedActiveFirstTimeSetupAsync(bool domainConfigured)
        {
            var now = DateTime.UtcNow;
            Db.Installations.Add(new InstallationEntity
            {
                Id = Guid.NewGuid(),
                Status = InstallationStatuses.Draft,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

            if (domainConfigured)
            {
                Db.Domains.Add(new DomainEntity
                {
                    Id = Guid.NewGuid(),
                    BaseDomain = "example.test",
                    DisplayName = "Example",
                    Purpose = "platform",
                    IsMainPlatformDomain = true,
                    DnsProvider = "desec",
                    DnsZone = "example.test",
                    Status = "Active",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            await Db.SaveChangesAsync();
        }

        public async Task SeedEstablishedPlatformDomainAsync()
        {
            var now = DateTime.UtcNow;
            Db.Domains.Add(new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = "example.test",
                DisplayName = "Example",
                Purpose = "platform",
                IsMainPlatformDomain = true,
                DnsProvider = "desec",
                DnsZone = "example.test",
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            await Db.SaveChangesAsync();
        }

        public async Task SeedVerifiedCoturnStepAsync()
        {
            var now = DateTime.UtcNow;
            var installation = new InstallationEntity
            {
                Id = Guid.NewGuid(),
                Status = InstallationStatuses.WaitingForUser,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                StartedAtUtc = now
            };
            installation.Steps.Add(new InstallationStepExecutionEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installation.Id,
                StepName = InstallStepNames.VerifySharedPlatformTurn,
                Sequence = 10,
                Status = InstallationStepStatuses.Succeeded,
                AttemptCount = 1,
                StartedAtUtc = now,
                CompletedAtUtc = now
            });
            Db.Installations.Add(installation);
            await Db.SaveChangesAsync();
        }


        public async Task MarkActiveSetupCoturnVerifiedAsync()
        {
            // Model Setup advancing as an independent durable write. The processor
            // only consumes the successful verification-step contract; updating the
            // already-tracked parent installation is incidental and can manufacture
            // an EF affected-row concurrency failure inside this shared test fixture.
            var installationId = await Db.Installations
                .AsNoTracking()
                .Select(item => item.Id)
                .SingleAsync();

            Db.ChangeTracker.Clear();

            var now = DateTime.UtcNow;
            Db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installationId,
                StepName = InstallStepNames.VerifySharedPlatformTurn,
                Sequence = 10,
                Status = InstallationStepStatuses.Succeeded,
                AttemptCount = 1,
                StartedAtUtc = now,
                CompletedAtUtc = now
            });
            await Db.SaveChangesAsync();
        }

        public CoturnStartupSupervisionProcessor CreateProcessor(
            ICoturnStartupSupervisionRuntime runtime) =>
            new(
                Db,
                Operations,
                runtime,
                new CoturnPlatformMutationAdmissionGate(),
                State,
                RuntimeContext,
                TimeProvider.System,
                NullLogger<CoturnStartupSupervisionProcessor>.Instance,
                Diagnostics);

        public async Task<HostAgent.Runtime.Operations.RuntimeOperationDetail?> LatestStartupOperationAsync()
        {
            var entity = await Db.RuntimeOperations
                .AsNoTracking()
                .Where(item => item.Operation == CoturnStartupSupervisionProcessor.OperationName)
                .OrderByDescending(item => item.RequestedAtUtc)
                .FirstOrDefaultAsync();
            return entity is null
                ? null
                : await Operations.FindByIdAsync(entity.Id, CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
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
                EventId: $"evt_test_{Requests.Count}",
                IncidentId: request.IncidentId,
                WarningCode: null));
        }
    }

    private static MemControlPlaneRuntimeContext CreateRuntimeContext() =>
        new(
            SchemaVersion: 1,
            RuntimeMode: MemRuntimeModes.ContainerizedDevelopment,
            ControlPlaneInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ApiProcessInstanceId: Guid.NewGuid(),
            EnvironmentName: "Development",
            RunningInContainer: true,
            ContentRootPath: "/app",
            ContentRootKind: "container-image",
            StateRootPath: "/data",
            StateRootKind: "docker-volume",
            StateRootProfile: MemStateRootProfiles.Default,
            UiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
            DockerEndpointKind: "unix-socket",
            ConfiguredContainerName: "mem-control-plane-dev",
            ApplicationName: "mem-control-plane",
            Version: "0.2.0",
            Commit: "test",
            ValidationState: MemRuntimeValidationStates.Valid,
            MutationsAllowed: true,
            ShowDevelopmentBanner: true,
            Warnings: []);
}
