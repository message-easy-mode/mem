using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnPlatformMaintenanceOperationProcessor
{
    private static readonly TimeSpan TerminalJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly ICoturnPlatformMaintenanceRuntime _runtime;
    private readonly RuntimeOperationStore _operations;
    private readonly CoturnPlatformMaintenanceOperationLifetime _operationLifetime;
    private readonly ILogger<CoturnPlatformMaintenanceOperationProcessor> _logger;

    public CoturnPlatformMaintenanceOperationProcessor(
        ICoturnPlatformMaintenanceRuntime runtime,
        RuntimeOperationStore operations,
        CoturnPlatformMaintenanceOperationLifetime operationLifetime,
        ILogger<CoturnPlatformMaintenanceOperationProcessor> logger)
    {
        _runtime = runtime;
        _operations = operations;
        _operationLifetime = operationLifetime;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        Guid operationId,
        CoturnPlatformMaintenanceRequest request)
    {
        var action = CoturnPlatformMaintenanceActions.Normalize(request.Action);
        using var operationScope = _operationLifetime.Begin();
        var ct = operationScope.CancellationToken;
        var currentStep = "queued";

        try
        {
            if (action == CoturnPlatformMaintenanceActions.RestartVerify)
            {
                currentStep = "restart-platform-turn";
                await _operations.UpdateStepAsync(operationId, currentStep, ct);

                var restarted = await _runtime.RestartOwnedAsync(ct);
                if (!restarted.Running ||
                    !restarted.RuntimeExact ||
                    !string.Equals(
                        restarted.OperatorStatus,
                        CoturnOperatorStatuses.RuntimeReady,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        restarted.Detail ??
                        "Coturn did not return to exact Runtime ready state after restart.");
                }
            }
            else
            {
                currentStep = "repair-platform-turn";
                await _operations.UpdateStepAsync(operationId, currentStep, ct);

                var repaired = await _runtime.RepairAsync(
                    request.ExternalIp,
                    ct);

                if (!repaired.Running ||
                    !repaired.RuntimeExact ||
                    !string.Equals(
                        repaired.OperatorStatus,
                        CoturnOperatorStatuses.RuntimeReady,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        repaired.Detail ??
                        "Coturn did not reach exact Runtime ready state after repair.");
                }
            }

            currentStep = "verify-platform-turn-runtime";
            await _operations.UpdateStepAsync(operationId, currentStep, ct);

            var verified = await _runtime.InspectAsync(ct);
            if (!verified.Running ||
                !verified.RuntimeExact ||
                !string.Equals(
                    verified.OperatorStatus,
                    CoturnOperatorStatuses.RuntimeReady,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    verified.Detail ??
                    "Coturn did not remain exact and Runtime ready after maintenance.");
            }

            currentStep = "verify-platform-turn-functional";
            await _operations.UpdateStepAsync(operationId, currentStep, ct);

            var functional = await _runtime.CheckAsync(ct);
            var safeResult = BuildSafeResult(action, verified, functional);
            var safeEvidence = BuildSafeEvidence(action, functional);

            using var terminal = new CancellationTokenSource(TerminalJournalTimeout);
            if (string.Equals(
                    functional.Status,
                    CoturnCheckStatuses.Failed,
                    StringComparison.Ordinal))
            {
                await _operations.FailAsync(
                    operationId,
                    currentStep,
                    error: "Coturn maintenance completed its Docker mutation, but the required functional TURN verification failed. Review the retained check and correlated Diagnostics evidence.",
                    result: safeResult,
                    evidence: safeEvidence,
                    terminal.Token);
                return;
            }

            await _operations.CompleteAsync(
                operationId,
                status: "succeeded",
                currentStep: "completed",
                result: safeResult,
                evidence: safeEvidence,
                terminal.Token);
        }
        catch (OperationCanceledException ex)
        {
            var reason = operationScope.OperationTimeoutRequested
                ? "The shared platform TURN maintenance operation exceeded its bounded server-owned timeout."
                : operationScope.ApplicationStoppingRequested
                    ? "MEM stopped while the shared platform TURN maintenance operation was running."
                    : "The shared platform TURN maintenance operation was cancelled.";

            await TryFailAsync(
                operationId,
                action,
                currentStep,
                reason,
                ex);
        }
        catch (Exception ex)
        {
            await TryFailAsync(
                operationId,
                action,
                currentStep,
                "Shared platform TURN maintenance failed. Review the Coturn workspace and Diagnostics before retrying.",
                ex);
        }
    }

    private static object BuildSafeResult(
        string action,
        CoturnRuntimeResponse verified,
        CoturnCheckResponse functional) =>
        new
        {
            action,
            runtime = new
            {
                verified.ContainerName,
                verified.ContainerId,
                verified.PublicHost,
                verified.Realm,
                verified.Readiness,
                verified.Running,
                verified.OwnershipVerified,
                verified.RuntimeExact,
                verified.Recreated,
                verified.ImageApproved,
                verified.SecretPresent,
                verified.RelayPortsPublished,
                verified.SecurityPolicyApplied,
                verified.ResolvedImageId,
                restartPolicy = verified.DockerRuntime?.RestartPolicy,
                restartCount = verified.DockerRuntime?.RestartCount,
                startedAtUtc = verified.DockerRuntime?.StartedAtUtc
            },
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

    private static object BuildSafeEvidence(
        string action,
        CoturnCheckResponse functional) =>
        new
        {
            action,
            requestAbortCancelsMutation = false,
            operationTimeoutMinutes = CoturnPlatformMaintenanceOperationLifetime
                .DefaultOperationTimeout
                .TotalMinutes,
            functionalVerificationRequired = true,
            functionalVerificationStatus = functional.Status,
            functionalCheckIncidentId = functional.IncidentId,
            externalClientReachabilityProven = false
        };

    private async Task TryFailAsync(
        Guid operationId,
        string action,
        string currentStep,
        string error,
        Exception exception)
    {
        _logger.LogWarning(
            "Platform TURN maintenance failed. OperationId={OperationId} Action={Action} Step={Step} ExceptionType={ExceptionType}",
            operationId,
            action,
            currentStep,
            exception.GetType().FullName);

        try
        {
            using var lookupJournal = new CancellationTokenSource(TerminalJournalTimeout);
            var detail = await _operations.FindByIdAsync(
                operationId,
                lookupJournal.Token);
            if (detail?.CompletedAtUtc is null)
            {
                var logs = await TryReadFailureLogsAsync();
                using var terminalJournal = new CancellationTokenSource(TerminalJournalTimeout);
                await _operations.FailAsync(
                    operationId,
                    currentStep: string.IsNullOrWhiteSpace(detail?.CurrentStep)
                        ? currentStep
                        : detail!.CurrentStep,
                    error: error,
                    evidence: new
                    {
                        action,
                        failureKind = "coturn-maintenance-failed",
                        exceptionType = exception.GetType().FullName,
                        requestAbortCancelsMutation = false,
                        recentLogs = logs
                    },
                    terminalJournal.Token);
            }
        }
        catch (Exception journalException)
        {
            _logger.LogError(
                journalException,
                "Could not persist terminal failure for platform TURN maintenance operation {OperationId}.",
                operationId);
        }
    }

    private async Task<object?> TryReadFailureLogsAsync()
    {
        try
        {
            using var logsTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var logs = await _runtime.GetRecentLogsAsync(
                tail: 120,
                logsTimeout.Token);

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
                warning = "Bounded sanitized Coturn logs could not be collected after the maintenance failure."
            };
        }
    }
}
