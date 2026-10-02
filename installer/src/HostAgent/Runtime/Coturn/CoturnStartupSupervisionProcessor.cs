using System.Text.Json;
using HostAgent.Runtime.Operations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Setup.InstallRuns;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnStartupSupervisionProcessor : ICoturnStartupSupervisionProcessor
{
    public const string OperationName = "supervise-platform-coturn-startup";

    internal static readonly TimeSpan CooldownDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan JournalTimeout = TimeSpan.FromSeconds(5);

    private readonly MemDbContext _db;
    private readonly RuntimeOperationStore _operations;
    private readonly ICoturnStartupSupervisionRuntime _runtime;
    private readonly CoturnPlatformMutationAdmissionGate _admissionGate;
    private readonly CoturnStartupSupervisionState _state;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;
    private readonly TimeProvider _timeProvider;
    private readonly IMemDiagnosticEventWriter? _diagnostics;
    private readonly ILogger<CoturnStartupSupervisionProcessor> _logger;

    public CoturnStartupSupervisionProcessor(
        MemDbContext db,
        RuntimeOperationStore operations,
        ICoturnStartupSupervisionRuntime runtime,
        CoturnPlatformMutationAdmissionGate admissionGate,
        CoturnStartupSupervisionState state,
        MemControlPlaneRuntimeContext runtimeContext,
        TimeProvider timeProvider,
        ILogger<CoturnStartupSupervisionProcessor> logger,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _db = db;
        _operations = operations;
        _runtime = runtime;
        _admissionGate = admissionGate;
        _state = state;
        _runtimeContext = runtimeContext;
        _timeProvider = timeProvider;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task<CoturnStartupSupervisionExecutionOutcome> ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!CoturnStartupSupervisionPolicy.IsEnabled(_runtimeContext))
        {
            SetState(
                CoturnStartupSupervisionStatuses.Disabled,
                CoturnStartupSupervisionDecisions.NotEvaluated,
                detail: "Coturn automatic startup supervision is disabled outside an authorized containerized MEM mutation runtime.");
            return CoturnStartupSupervisionExecutionOutcome.Finished;
        }

        Guid? operationId = null;
        var decisionForFailure = CoturnStartupSupervisionDecisions.NotEvaluated;
        var automaticMutationAttempted = false;
        var mutationPerformed = false;
        var automaticRestartAttempted = false;

        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        operationLifetime.CancelAfter(OperationTimeout);
        var ct = operationLifetime.Token;

        try
        {
            if (!await IsCoturnExpectedAsync(ct))
            {
                SetState(
                    CoturnStartupSupervisionStatuses.NotInstalled,
                    CoturnStartupSupervisionDecisions.NotEvaluated,
                    detail: "Coturn is not yet an expected managed platform service. Startup supervision is idle and will re-evaluate after first-time installation authoritatively verifies shared Coturn.");
                return CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected;
            }

            await ReconcileAbandonedCoturnMutationsAsync(ct);

            using var admission = await _admissionGate.EnterAsync(ct);

            // A browser request can be accepted shortly after this API process
            // starts while the hosted supervisor is still waiting for Docker to
            // settle. Such an operation belongs to this process and must never be
            // mistaken for abandoned work from the previous process. Do not
            // compete with it; the explicit operator operation owns recovery.
            var currentProcessMutation = await FindCurrentProcessMutationAsync(ct);
            if (currentProcessMutation is not null)
            {
                SetState(
                    CoturnStartupSupervisionStatuses.Deferred,
                    CoturnStartupSupervisionDecisions.NotEvaluated,
                    operationId: currentProcessMutation.Id,
                    detail: "An operator-requested Coturn mutation is already active in this API process. Startup supervision will not compete with it and will resume after the operation reaches a terminal state.");
                return CoturnStartupSupervisionExecutionOutcome.DeferredCurrentProcessMutation;
            }

            var current = await _runtime.InspectAsync(ct);
            var decision = CoturnStartupSupervisionPolicy.Evaluate(current);
            decisionForFailure = decision;

            if (string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.NotInstalled,
                    StringComparison.Ordinal))
            {
                SetState(
                    CoturnStartupSupervisionStatuses.NotInstalled,
                    decision,
                    detail: "Coturn has not been provisioned yet, so startup supervision has nothing to recover.");
                return CoturnStartupSupervisionExecutionOutcome.Finished;
            }

            // Cooldown exists only to suppress another automatic host mutation.
            // A new Conflict / Repair required / Unavailable state is stronger
            // current truth and must never be hidden behind an older recovery
            // cooldown. Those decisions continue below and are journalled as
            // non-mutating operator-review failures.
            if (!IsUnsafeDecision(decision))
            {
                var cooldown = await FindCooldownAsync(ct);
                if (cooldown is not null)
                {
                    var cooldownUntil = ToDateTimeOffset(cooldown.CompletedAtUtc!.Value)
                        .Add(CooldownDuration);

                    if (string.Equals(
                            decision,
                            CoturnStartupSupervisionDecisions.Ready,
                            StringComparison.Ordinal))
                    {
                        var verification = await _runtime.CheckAsync(ct);
                        if (!string.Equals(
                                verification.Status,
                                CoturnCheckStatuses.Failed,
                                StringComparison.Ordinal))
                        {
                            SetState(
                                CoturnStartupSupervisionStatuses.Verified,
                                decision,
                                operationId: null,
                                detail: "Coturn is exact and the current startup functional check completed without failure. No automatic restart was required.");
                            return CoturnStartupSupervisionExecutionOutcome.Finished;
                        }
                    }

                    SetState(
                        CoturnStartupSupervisionStatuses.Cooldown,
                        decision,
                        cooldownActive: true,
                        cooldownUntilUtc: cooldownUntil,
                        operationId: cooldown.Id,
                        detail: "A recent automatic Coturn startup recovery failed. MEM is in cooldown and will not enter an automatic restart loop; operator review is required.");
                    return CoturnStartupSupervisionExecutionOutcome.Finished;
                }
            }

            // Even an exact, already-running service may require the one allowed
            // automatic Restart & Verify when its current functional check fails.
            // Journal the operation as mutation-capable up front rather than
            // understating host impact as "none".
            var hostMutationLevel = decision is
                CoturnStartupSupervisionDecisions.Ready or
                CoturnStartupSupervisionDecisions.StartStopped or
                CoturnStartupSupervisionDecisions.CorrectRestartPolicy
                    ? "docker,platform-turn"
                    : "none";

            using (var journal = new CancellationTokenSource(JournalTimeout))
            {
                operationId = await _operations.StartAsync(
                    runtimeStackId: null,
                    operation: OperationName,
                    idempotencyKey: $"coturn-startup:{_runtimeContext.ApiProcessInstanceId:N}",
                    requestedBy: "system:startup-supervisor",
                    hostMutationLevel: hostMutationLevel,
                    input: new
                    {
                        runtimeMode = _runtimeContext.RuntimeMode,
                        apiProcessInstanceId = _runtimeContext.ApiProcessInstanceId,
                        decision,
                        preflightContainerId = current.ContainerId,
                        preflightStartedAtUtc = current.DockerRuntime?.StartedAtUtc,
                        preflightRestartCount = current.DockerRuntime?.RestartCount,
                        automaticRestartLimit = 1,
                        destructiveRepairAllowed = false,
                        imagePullAllowed = false,
                        secretRotationAllowed = false
                    },
                    journal.Token);
            }

            SetState(
                CoturnStartupSupervisionStatuses.Recovering,
                decision,
                operationId: operationId,
                detail: "MEM is supervising the shared Coturn service under a bounded startup recovery lifetime.");

            await WriteDiagnosticAsync(
                CoturnStartupSupervisionEventCodes.Started,
                MemDiagnosticSeverities.Information,
                "Coturn startup supervision began.",
                operationId,
                decision,
                createIncident: false,
                retryable: false,
                ct);

            if (string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.Conflict,
                    StringComparison.Ordinal))
            {
                await FailUnsafeAsync(
                    operationId.Value,
                    decision,
                    CoturnStartupSupervisionStatuses.Conflict,
                    CoturnStartupSupervisionEventCodes.ForeignContainerConflict,
                    "A colliding Coturn container is not owned by the current MEM authority. Automatic recovery refused to mutate it.",
                    current,
                    ct);
                return CoturnStartupSupervisionExecutionOutcome.Finished;
            }

            if (string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.RepairRequired,
                    StringComparison.Ordinal))
            {
                await FailUnsafeAsync(
                    operationId.Value,
                    decision,
                    CoturnStartupSupervisionStatuses.RepairRequired,
                    CoturnStartupSupervisionEventCodes.RepairRequired,
                    "Coturn startup supervision detected state that requires operator-reviewed repair. MEM did not recreate the container, rotate secrets, replace protected configuration, or pull an image.",
                    current,
                    ct);
                return CoturnStartupSupervisionExecutionOutcome.Finished;
            }

            if (string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.Unavailable,
                    StringComparison.Ordinal))
            {
                await FailUnsafeAsync(
                    operationId.Value,
                    decision,
                    CoturnStartupSupervisionStatuses.Failed,
                    CoturnStartupSupervisionEventCodes.Failed,
                    "Coturn startup supervision could not obtain the exact Docker/protected evidence required for safe recovery. MEM performed no automatic mutation.",
                    current,
                    ct);
                return CoturnStartupSupervisionExecutionOutcome.Finished;
            }

            if (string.Equals(
                    decision,
                    CoturnStartupSupervisionDecisions.StartStopped,
                    StringComparison.Ordinal))
            {
                await _operations.UpdateStepAsync(
                    operationId.Value,
                    "start-exact-stopped-coturn",
                    ct);
                await WriteDiagnosticAsync(
                    CoturnStartupSupervisionEventCodes.ContainerStopped,
                    MemDiagnosticSeverities.Warning,
                    "Startup supervision found the exact MEM-owned Coturn container stopped.",
                    operationId,
                    decision,
                    createIncident: false,
                    retryable: true,
                    ct);

                automaticMutationAttempted = true;
                var recovered = await _runtime.RecoverStartupAsync(
                    decision,
                    current.ContainerId!,
                    ct);
                current = recovered.Runtime;
                mutationPerformed = recovered.MutationPerformed;
            }
            else if (string.Equals(
                         decision,
                         CoturnStartupSupervisionDecisions.CorrectRestartPolicy,
                         StringComparison.Ordinal))
            {
                await _operations.UpdateStepAsync(
                    operationId.Value,
                    "correct-restart-policy",
                    ct);
                await WriteDiagnosticAsync(
                    CoturnStartupSupervisionEventCodes.RestartPolicyDrift,
                    MemDiagnosticSeverities.Warning,
                    "Startup supervision found safe Coturn restart-policy drift.",
                    operationId,
                    decision,
                    createIncident: false,
                    retryable: true,
                    ct);

                automaticMutationAttempted = true;
                var recovered = await _runtime.RecoverStartupAsync(
                    decision,
                    current.ContainerId!,
                    ct);
                current = recovered.Runtime;
                mutationPerformed = recovered.MutationPerformed;
            }

            await _operations.UpdateStepAsync(
                operationId.Value,
                "verify-startup-runtime",
                ct);

            current = await _runtime.InspectAsync(ct);
            if (!current.Running ||
                !current.RuntimeExact ||
                !string.Equals(
                    current.OperatorStatus,
                    CoturnOperatorStatuses.RuntimeReady,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    current.Detail ??
                    "Coturn did not reach exact Runtime ready state during startup supervision.");
            }

            await _operations.UpdateStepAsync(
                operationId.Value,
                "verify-startup-functional",
                ct);
            var functional = await _runtime.CheckAsync(ct);

            if (string.Equals(
                    functional.Status,
                    CoturnCheckStatuses.Failed,
                    StringComparison.Ordinal) &&
                ShouldAttemptAutomaticRestart(functional))
            {
                automaticRestartAttempted = true;
                automaticMutationAttempted = true;
                await _operations.UpdateStepAsync(
                    operationId.Value,
                    "automatic-restart-and-verify",
                    ct);

                current = await _runtime.RestartOwnedAsync(ct);
                mutationPerformed = true;

                await _operations.UpdateStepAsync(
                    operationId.Value,
                    "verify-after-automatic-restart",
                    ct);
                functional = await _runtime.CheckAsync(ct);
            }

            if (string.Equals(
                    functional.Status,
                    CoturnCheckStatuses.Failed,
                    StringComparison.Ordinal))
            {
                var failedStep = automaticRestartAttempted
                    ? "verify-after-automatic-restart"
                    : "verify-startup-functional";
                var failureMessage = automaticRestartAttempted
                    ? "Coturn startup supervision exhausted its single bounded automatic Restart & Verify attempt and functional TURN verification still failed."
                    : "Coturn startup functional verification failed, but the local TURN allocation itself did not fail. MEM performed no speculative Coturn restart for an external/prerequisite failure.";

                await FailOperationAsync(
                    operationId.Value,
                    decision,
                    failedStep,
                    failureMessage,
                    current,
                    functional,
                    mutationPerformed,
                    automaticRestartAttempted,
                    cooldownEligible: automaticMutationAttempted,
                    ct: ct);
                return CoturnStartupSupervisionExecutionOutcome.Finished;
            }

            using (var terminal = new CancellationTokenSource(JournalTimeout))
            {
                await _operations.CompleteAsync(
                    operationId.Value,
                    status: "succeeded",
                    currentStep: "completed",
                    result: BuildSafeResult(
                        decision,
                        current,
                        functional,
                        mutationPerformed,
                        automaticRestartAttempted),
                    evidence: new
                    {
                        startupSupervision = true,
                        boundedServerOwnedLifetime = true,
                        automaticRestartLimit = 1,
                        automaticRestartAttempted,
                        destructiveRepairPerformed = false,
                        imagePulled = false,
                        secretRotated = false,
                        functionalVerificationStatus = functional.Status,
                        functionalCheckIncidentId = functional.IncidentId
                    },
                    terminal.Token);
            }

            var status = mutationPerformed || automaticRestartAttempted
                ? CoturnStartupSupervisionStatuses.Recovered
                : CoturnStartupSupervisionStatuses.Verified;
            SetState(
                status,
                decision,
                mutationPerformed,
                automaticRestartAttempted,
                operationId: operationId,
                detail: status == CoturnStartupSupervisionStatuses.Recovered
                    ? "Coturn startup supervision safely restored the service and completed functional TURN verification."
                    : "Coturn startup supervision verified exact runtime and current functional TURN health without mutation.");

            await WriteDiagnosticAsync(
                CoturnStartupSupervisionEventCodes.Succeeded,
                MemDiagnosticSeverities.Information,
                status == CoturnStartupSupervisionStatuses.Recovered
                    ? "Coturn startup recovery succeeded."
                    : "Coturn startup supervision completed without requiring recovery.",
                operationId,
                decision,
                createIncident: false,
                retryable: false,
                ct);
        }
        catch (OperationCanceledException ex)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                if (operationId.HasValue)
                {
                    await TryCancelForControlPlaneShutdownAsync(
                        operationId.Value,
                        decisionForFailure,
                        automaticMutationAttempted,
                        mutationPerformed,
                        automaticRestartAttempted);
                }

                // The process-local supervision state is intentionally not
                // rewritten to Failed while the Control Plane is shutting down.
                // The next API process starts with a fresh Waiting state and
                // performs authoritative Coturn inspection again.
                throw;
            }

            if (operationId.HasValue)
            {
                await TryFailTerminalAsync(
                    operationId.Value,
                    "startup-supervision-timeout",
                    "Coturn startup supervision exceeded its bounded server-owned lifetime.",
                    decisionForFailure,
                    automaticMutationAttempted,
                    ex);
            }

            SetState(
                CoturnStartupSupervisionStatuses.Failed,
                CoturnStartupSupervisionDecisions.Unavailable,
                operationId: operationId,
                detail: "Coturn startup supervision exceeded its bounded recovery lifetime.");
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogError(
                ex,
                "Coturn startup supervision failed. OperationId={OperationId}",
                operationId);

            if (operationId.HasValue)
            {
                await TryFailTerminalAsync(
                    operationId.Value,
                    "startup-supervision-failed",
                    "Coturn startup supervision failed. Review the Coturn workspace and Diagnostics before retrying.",
                    decisionForFailure,
                    automaticMutationAttempted,
                    ex);
            }

            SetState(
                CoturnStartupSupervisionStatuses.Failed,
                CoturnStartupSupervisionDecisions.Unavailable,
                operationId: operationId,
                detail: "Coturn startup supervision failed. Review Diagnostics and bounded Coturn logs before retrying.");

            await WriteDiagnosticAsync(
                CoturnStartupSupervisionEventCodes.Failed,
                MemDiagnosticSeverities.Error,
                "Coturn startup supervision failed.",
                operationId,
                CoturnStartupSupervisionDecisions.Unavailable,
                createIncident: !operationId.HasValue,
                retryable: true,
                CancellationToken.None,
                ex);
        }

        return CoturnStartupSupervisionExecutionOutcome.Finished;
    }

    private async Task<bool> IsCoturnExpectedAsync(CancellationToken ct)
    {
        // The Setup workflow owns first provisioning. A successful durable Coturn
        // verification step is the strongest source-owned boundary proving that
        // the shared service now exists and startup supervision may own later
        // reconciliation. A fully succeeded installation is also authoritative.
        if (await _db.InstallationStepExecutions
            .AsNoTracking()
            .AnyAsync(
                step =>
                    step.StepName == InstallStepNames.VerifySharedPlatformTurn &&
                    step.Status == InstallationStepStatuses.Succeeded,
                ct))
        {
            return true;
        }

        if (await _db.Installations
            .AsNoTracking()
            .AnyAsync(installation => installation.Status == InstallationStatuses.Succeeded, ct))
        {
            return true;
        }

        // An active first-time Setup is explicitly not sufficient. In particular,
        // merely having configured the platform domain must not make startup
        // supervision race the installation workflow before Coturn provisioning.
        if (await _db.Installations
            .AsNoTracking()
            .AnyAsync(
                installation => InstallationStatuses.ActiveFirstTimeSetup.Contains(installation.Status),
                ct))
        {
            return false;
        }

        // Compatibility boundary for an established/upgraded authority whose
        // historical Setup rows predate the current installation ledger. With no
        // active first-time Setup, an active main platform domain is sufficient to
        // preserve post-install startup supervision rather than silently disabling
        // it on older durable authorities.
        return await _db.Domains
            .AsNoTracking()
            .AnyAsync(
                domain => domain.IsMainPlatformDomain && domain.Status == "Active",
                ct);
    }

    private async Task ReconcileAbandonedCoturnMutationsAsync(CancellationToken ct)
    {
        var startupBoundary = _state.StartupBoundaryAtUtc.UtcDateTime;
        var abandoned = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(item =>
                item.Status == "running" &&
                item.CompletedAtUtc == null &&
                item.RequestedAtUtc < startupBoundary &&
                (item.Operation == CoturnPlatformInstallOperationService.OperationName ||
                 item.Operation == CoturnPlatformMaintenanceOperationService.OperationName ||
                 item.Operation == OperationName))
            .OrderBy(item => item.RequestedAtUtc)
            .ToArrayAsync(ct);

        foreach (var item in abandoned)
        {
            using var terminal = new CancellationTokenSource(JournalTimeout);
            await _operations.FailAsync(
                item.Id,
                currentStep: item.CurrentStep ?? "startup-reconciliation",
                error: "MEM restarted before this in-process Coturn mutation reached a terminal state. Startup supervision reconciled it as abandoned before inspecting the authoritative runtime.",
                evidence: new
                {
                    failureKind = "coturn-operation-abandoned-on-control-plane-restart",
                    reconciledBy = OperationName,
                    previousOperation = item.Operation
                },
                terminal.Token);
        }
    }

    private async Task<Infrastructure.Data.Entities.RuntimeOperationEntity?> FindCurrentProcessMutationAsync(
        CancellationToken ct)
    {
        var startupBoundary = _state.StartupBoundaryAtUtc.UtcDateTime;
        return await _db.RuntimeOperations
            .AsNoTracking()
            .Where(item =>
                item.Status == "running" &&
                item.CompletedAtUtc == null &&
                item.RequestedAtUtc >= startupBoundary &&
                (item.Operation == CoturnPlatformInstallOperationService.OperationName ||
                 item.Operation == CoturnPlatformMaintenanceOperationService.OperationName))
            .OrderByDescending(item => item.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<Infrastructure.Data.Entities.RuntimeOperationEntity?> FindCooldownAsync(
        CancellationToken ct)
    {
        var cutoff = _timeProvider.GetUtcNow().Subtract(CooldownDuration).UtcDateTime;
        var recentFailures = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(item =>
                item.Operation == OperationName &&
                item.Status == "failed" &&
                item.CompletedAtUtc != null &&
                item.CompletedAtUtc >= cutoff)
            .OrderByDescending(item => item.CompletedAtUtc)
            .Take(20)
            .ToArrayAsync(ct);

        // Cooldown exists to prevent repeated automatic host mutation, not to
        // hide a persistent Repair required / Conflict state after a later API
        // restart. Only failures that actually entered an automatic mutation
        // path are eligible.
        return recentFailures.FirstOrDefault(item =>
            IsCooldownEligible(item.EvidenceJson));
    }

    private static bool IsCooldownEligible(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("cooldownEligible", out var value) &&
                value.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            // Historical or malformed evidence must never invent cooldown.
            return false;
        }
    }

    private async Task FailUnsafeAsync(
        Guid operationId,
        string decision,
        string status,
        string eventCode,
        string message,
        CoturnRuntimeResponse runtime,
        CancellationToken ct)
    {
        using (var terminal = new CancellationTokenSource(JournalTimeout))
        {
            await _operations.FailAsync(
                operationId,
                currentStep: "operator-review-required",
                error: message,
                result: new
                {
                    decision,
                    runtime = BuildSafeRuntime(runtime)
                },
                evidence: new
                {
                    startupSupervision = true,
                    cooldownEligible = false,
                    automaticMutationPerformed = false,
                    destructiveRepairPerformed = false,
                    imagePulled = false,
                    secretRotated = false,
                    runtimeDrift = runtime.RuntimeDrift
                },
                terminal.Token);
        }

        SetState(
            status,
            decision,
            operationId: operationId,
            detail: message);

        await WriteDiagnosticAsync(
            eventCode,
            MemDiagnosticSeverities.Error,
            message,
            operationId,
            decision,
            createIncident: false,
            retryable: true,
            ct);
    }

    private async Task FailOperationAsync(
        Guid operationId,
        string decision,
        string step,
        string message,
        CoturnRuntimeResponse runtime,
        CoturnCheckResponse functional,
        bool mutationPerformed,
        bool automaticRestartAttempted,
        bool cooldownEligible,
        CancellationToken ct)
    {
        var logs = await TryReadLogsAsync();
        using (var terminal = new CancellationTokenSource(JournalTimeout))
        {
            await _operations.FailAsync(
                operationId,
                currentStep: step,
                error: message,
                result: BuildSafeResult(
                    decision,
                    runtime,
                    functional,
                    mutationPerformed,
                    automaticRestartAttempted),
                evidence: new
                {
                    startupSupervision = true,
                    cooldownEligible,
                    automaticRestartLimit = 1,
                    automaticRestartAttempted,
                    destructiveRepairPerformed = false,
                    imagePulled = false,
                    secretRotated = false,
                    functionalCheckIncidentId = functional.IncidentId,
                    recentLogs = logs
                },
                terminal.Token);
        }

        SetState(
            CoturnStartupSupervisionStatuses.Failed,
            decision,
            mutationPerformed,
            automaticRestartAttempted,
            operationId: operationId,
            detail: message);

        await WriteDiagnosticAsync(
            CoturnStartupSupervisionEventCodes.Failed,
            MemDiagnosticSeverities.Error,
            message,
            operationId,
            decision,
            createIncident: false,
            retryable: true,
            ct);
    }

    private async Task TryCancelForControlPlaneShutdownAsync(
        Guid operationId,
        string decision,
        bool automaticMutationAttempted,
        bool mutationPerformed,
        bool automaticRestartAttempted)
    {
        try
        {
            using var terminal = new CancellationTokenSource(JournalTimeout);
            await _operations.CancelIfRunningAsync(
                operationId,
                currentStep: "control-plane-shutdown",
                result: new
                {
                    terminationKind = "control-plane-shutdown",
                    decision
                },
                evidence: new
                {
                    startupSupervision = true,
                    cancellationKind = "control-plane-shutdown",
                    decision,
                    cooldownEligible = false,
                    automaticMutationAttempted,
                    mutationPerformed,
                    automaticRestartAttempted,
                    destructiveRepairPerformed = false,
                    imagePulled = false,
                    secretRotated = false
                },
                terminal.Token);
        }
        catch (Exception journalException) when (
            journalException is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogWarning(
                journalException,
                "Could not persist Coturn startup-supervision cancellation during Control Plane shutdown. OperationId={OperationId}",
                operationId);
        }
    }

    private async Task TryFailTerminalAsync(
        Guid operationId,
        string failureKind,
        string message,
        string decision,
        bool cooldownEligible,
        Exception exception)
    {
        try
        {
            using var lookup = new CancellationTokenSource(JournalTimeout);
            var detail = await _operations.FindByIdAsync(operationId, lookup.Token);
            if (detail?.CompletedAtUtc is null)
            {
                var logs = await TryReadLogsAsync();
                using var terminal = new CancellationTokenSource(JournalTimeout);
                await _operations.FailAsync(
                    operationId,
                    currentStep: detail?.CurrentStep ?? "startup-supervision",
                    error: message,
                    evidence: new
                    {
                        startupSupervision = true,
                        failureKind,
                        decision,
                        cooldownEligible,
                        exceptionType = exception.GetType().FullName,
                        recentLogs = logs
                    },
                    terminal.Token);
            }
        }
        catch (Exception journalException) when (
            journalException is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogError(
                journalException,
                "Could not persist terminal Coturn startup-supervision failure. OperationId={OperationId}",
                operationId);
        }
    }

    private async Task<object?> TryReadLogsAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var logs = await _runtime.GetRecentLogsAsync(120, timeout.Token);
            return new
            {
                logs.Status,
                logs.RetrievedAtUtc,
                logs.ContainerName,
                logs.RequestedTail,
                logs.ReturnedLines,
                logs.Truncated,
                logs.Content,
                logs.Warnings
            };
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            return new
            {
                status = "unavailable",
                warning = "Bounded sanitized Coturn logs could not be collected after startup-supervision failure."
            };
        }
    }

    private async Task WriteDiagnosticAsync(
        string eventCode,
        string severity,
        string message,
        Guid? operationId,
        string decision,
        bool createIncident,
        bool retryable,
        CancellationToken ct,
        Exception? exception = null)
    {
        if (_diagnostics is null)
        {
            return;
        }

        await _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "host-agent.coturn.startup-supervisor",
            Feature: "coturn",
            Stage: "startup-supervision",
            Message: message,
            // Operation correlation and Incident correlation are different
            // concerns. Safe startup warnings such as a stopped container or
            // restart-policy drift remain technical events tied to the
            // RuntimeOperation, but must not become "Needs attention" Incidents
            // after automatic recovery succeeds. Error events may attach to the
            // operation Incident already created by RuntimeOperationStore.FailAsync.
            IncidentId:
                operationId.HasValue &&
                string.Equals(severity, MemDiagnosticSeverities.Error, StringComparison.Ordinal)
                    ? $"inc_op_{operationId.Value:N}"
                    : null,
            CreateIncident: createIncident,
            OperationId: operationId,
            Resource: new MemDiagnosticResource(
                Kind: "service",
                Id: CoturnRuntimePolicy.ServiceKey,
                DisplayName: "Coturn TURN server",
                Service: CoturnRuntimePolicy.ServiceKey,
                WorkspacePath: "/services/coturn"),
            Expected: new Dictionary<string, string?>
            {
                ["desiredState"] = "running-and-functionally-verified"
            },
            Observed: new Dictionary<string, string?>
            {
                ["decision"] = decision,
                ["runtimeMode"] = _runtimeContext.RuntimeMode
            },
            Details: new Dictionary<string, string?>
            {
                ["automaticRestartLimit"] = "1",
                ["destructiveRepairAllowed"] = "false",
                ["imagePullAllowed"] = "false",
                ["secretRotationAllowed"] = "false"
            },
            Exception: exception,
            SuggestedAction: retryable
                ? "Open Services > Coturn and Diagnostics. Review the exact runtime, current functional check, and bounded logs before retrying or using Repair."
                : null,
            Retryable: retryable));
    }

    private static bool IsUnsafeDecision(string decision) =>
        decision is
            CoturnStartupSupervisionDecisions.RepairRequired or
            CoturnStartupSupervisionDecisions.Conflict or
            CoturnStartupSupervisionDecisions.Unavailable;

    private static bool ShouldAttemptAutomaticRestart(CoturnCheckResponse functional) =>
        string.Equals(
            functional.Allocation.Status,
            CoturnCheckStatuses.Failed,
            StringComparison.Ordinal);

    private object BuildSafeResult(
        string decision,
        CoturnRuntimeResponse runtime,
        CoturnCheckResponse functional,
        bool mutationPerformed,
        bool automaticRestartAttempted) =>
        new
        {
            decision,
            mutationPerformed,
            automaticRestartAttempted,
            runtime = BuildSafeRuntime(runtime),
            functionalCheck = new
            {
                functional.Status,
                functional.CheckedAtUtc,
                functional.FreshUntilUtc,
                functional.EvidencePersisted,
                functional.IncidentId,
                warningCount = functional.Warnings.Count
            }
        };

    private static object BuildSafeRuntime(CoturnRuntimeResponse runtime) =>
        new
        {
            runtime.ContainerName,
            runtime.ContainerId,
            runtime.PublicHost,
            runtime.Readiness,
            runtime.Running,
            runtime.OwnershipVerified,
            runtime.RuntimeExact,
            runtime.ConfigurationPresent,
            runtime.ConfigurationExact,
            runtime.ImageApproved,
            runtime.RelayPortsPublished,
            runtime.SecurityPolicyApplied,
            restartPolicy = runtime.DockerRuntime?.RestartPolicy,
            restartCount = runtime.DockerRuntime?.RestartCount,
            startedAtUtc = runtime.DockerRuntime?.StartedAtUtc,
            runtimeDrift = runtime.RuntimeDrift
        };

    private void SetState(
        string status,
        string decision,
        bool mutationPerformed = false,
        bool automaticRestartAttempted = false,
        bool cooldownActive = false,
        DateTimeOffset? cooldownUntilUtc = null,
        Guid? operationId = null,
        string? detail = null) =>
        _state.Set(new CoturnStartupSupervisionResponse(
            Source: "control-plane",
            Status: status,
            RuntimeMode: _runtimeContext.RuntimeMode,
            Enabled: true,
            Decision: decision,
            MutationPerformed: mutationPerformed,
            AutomaticRestartAttempted: automaticRestartAttempted,
            CooldownActive: cooldownActive,
            CooldownUntilUtc: cooldownUntilUtc,
            OperationId: operationId,
            ObservedAtUtc: _timeProvider.GetUtcNow(),
            Detail: detail));

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
