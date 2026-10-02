using HostAgent.Commands;
using HostAgent.Matrix.Provisioning;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Services;

public sealed class CreateChatStackRuntimeFailureFinalizer
{
    internal static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan JournalTimeout = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(5);

    private readonly IRuntimeOperationStore _operations;
    private readonly IRuntimeStackDatabaseService _databases;
    private readonly IMemDiagnosticEventWriter _diagnostics;
    private readonly ILogger<CreateChatStackRuntimeFailureFinalizer> _logger;

    public CreateChatStackRuntimeFailureFinalizer(
        IRuntimeOperationStore operations,
        IRuntimeStackDatabaseService databases,
        IMemDiagnosticEventWriter diagnostics,
        ILogger<CreateChatStackRuntimeFailureFinalizer> logger)
    {
        _operations = operations;
        _databases = databases;
        _diagnostics = diagnostics;
        _logger = logger;
    }

    public async Task FinalizeAsync(
        Guid operationId,
        string currentStep,
        CreateChatStackRuntimeCommand command,
        RuntimeStackDatabaseProvisioningResult? databaseProvisioning,
        bool databaseOwnershipPersisted,
        bool activeRuntimeMutationStarted,
        Exception failure,
        IList<HostAgentEvidence> evidence,
        bool requestCancellationRequested,
        bool operationTimeoutRequested = false,
        bool applicationStoppingRequested = false)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(evidence);

        evidence.Add(new HostAgentEvidence(
            Code: "host-agent.create-stack.failed",
            Message: "Create chat stack runtime failed before the workflow reached a successful terminal state.",
            Data: new Dictionary<string, string?>
            {
                ["operationId"] = operationId.ToString(),
                ["stackId"] = command.StackId.ToString(),
                ["stackSlug"] = command.StackSlug,
                ["stage"] = currentStep,
                ["exceptionType"] = failure.GetType().FullName,
                ["requestCancellationRequested"] = requestCancellationRequested ? "true" : "false",
                ["operationTimeoutRequested"] = operationTimeoutRequested ? "true" : "false",
                ["applicationStoppingRequested"] = applicationStoppingRequested ? "true" : "false",
                ["failureKind"] = ClassifyFailure(
                    failure,
                    requestCancellationRequested,
                    operationTimeoutRequested,
                    applicationStoppingRequested)
            }));

        if (failure is SynapseConfigGenerationCanceledException synapseCancellation)
        {
            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.synapse-config.cancelled",
                Message: "Synapse configuration generation ended because the Docker operation was cancelled.",
                Data: new Dictionary<string, string?>
                {
                    ["generationStage"] = synapseCancellation.Stage,
                    ["containerId"] = synapseCancellation.ContainerId,
                    ["callerCancellationRequested"] = synapseCancellation.CallerCancellationRequested ? "true" : "false",
                    ["exceptionCancellationRequested"] = synapseCancellation.ExceptionCancellationRequested ? "true" : "false",
                    ["elapsedMilliseconds"] = Math.Round(synapseCancellation.Elapsed.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }

        if (failure is CreateChatStackPlatformTurnUnavailableException)
        {
            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.create-stack.platform-turn.required",
                Message: "Stack creation stopped before stack-specific mutation because the required shared platform TURN service was not ready.",
                Data: new Dictionary<string, string?>
                {
                    ["errorCode"] = CreateChatStackPlatformTurnUnavailableException.ErrorCodeValue,
                    ["repairWorkspace"] = "/services/coturn",
                    ["mutationStarted"] = "false"
                }));
        }

        // The failure must become durable and operator-visible before cleanup begins.
        // Cleanup is deliberately secondary: a slow or failed rollback must never keep
        // Diagnostics projecting an already-terminated create-stack attempt as running.
        await TryJournalFailureAsync(
            operationId,
            currentStep,
            failure,
            evidence);

        await TryWriteFailureDiagnosticAsync(
            operationId,
            currentStep,
            command,
            failure,
            requestCancellationRequested,
            operationTimeoutRequested,
            applicationStoppingRequested);

        if (databaseProvisioning is not null && !databaseOwnershipPersisted)
        {
            if (!activeRuntimeMutationStarted)
            {
                await TryCleanupDatabaseAsync(
                    command,
                    databaseProvisioning,
                    evidence);
            }
            else
            {
                evidence.Add(new HostAgentEvidence(
                    Code: "host-agent.matrix.postgres.pre-registration-cleanup.deferred",
                    Message: "MEM preserved the PostgreSQL database because active Matrix runtime mutation had already started and isolated database deletion was not proven safe.",
                    Data: new Dictionary<string, string?>
                    {
                        ["databaseName"] = databaseProvisioning.DatabaseName,
                        ["databaseUsername"] = databaseProvisioning.DatabaseUsername,
                        ["recoveryRequired"] = "true"
                    }));
            }
        }
    }

    private async Task TryWriteFailureDiagnosticAsync(
        Guid operationId,
        string currentStep,
        CreateChatStackRuntimeCommand command,
        Exception failure,
        bool requestCancellationRequested,
        bool operationTimeoutRequested,
        bool applicationStoppingRequested)
    {
        using var diagnosticCancellation = new CancellationTokenSource(DiagnosticTimeout);
        try
        {
            var result = await _diagnostics.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Error,
                    EventCode: "host-agent.create-stack.failed",
                    Source: "host-agent.create-stack",
                    Feature: "stack",
                    Stage: currentStep,
                    Message: "A chat server runtime creation attempt failed.",
                    IncidentId: $"inc_op_{operationId:N}",
                    CreateIncident: true,
                    OperationId: operationId,
                    Resource: new MemDiagnosticResource(
                        Kind: "runtime-operation",
                        Id: operationId.ToString("D"),
                        DisplayName: $"Create {command.StackSlug}",
                        StackId: command.StackId.ToString("D"),
                        StackSlug: command.StackSlug),
                    Expected: new Dictionary<string, string?>
                    {
                        ["terminalStatus"] = "succeeded"
                    },
                    Observed: new Dictionary<string, string?>
                    {
                        ["status"] = "failed",
                        ["stage"] = currentStep,
                        ["requestCancellationRequested"] = requestCancellationRequested ? "true" : "false",
                        ["operationTimeoutRequested"] = operationTimeoutRequested ? "true" : "false",
                        ["applicationStoppingRequested"] = applicationStoppingRequested ? "true" : "false"
                    },
                    Details: new Dictionary<string, string?>
                    {
                        ["failureKind"] = ClassifyFailure(
                            failure,
                            requestCancellationRequested,
                            operationTimeoutRequested,
                            applicationStoppingRequested),
                        ["exceptionType"] = failure.GetType().FullName
                    },
                    Exception: failure,
                    SuggestedAction: failure is CreateChatStackPlatformTurnUnavailableException
                        ? "Install or repair the shared platform TURN service, verify that it is Ready, then start a new stack-creation attempt."
                        : "Review this incident and the correlated runtime operation before retrying stack creation.",
                    Retryable: failure is OperationCanceledException or CreateChatStackPlatformTurnUnavailableException),
                diagnosticCancellation.Token);

            if (!result.Stored)
            {
                _logger.LogError(
                    "Create-stack failure diagnostic was not stored. OperationId={OperationId} Step={Step} WarningCode={WarningCode}",
                    operationId,
                    currentStep,
                    result.WarningCode);
            }
        }
        catch (OperationCanceledException ex) when (diagnosticCancellation.IsCancellationRequested)
        {
            _logger.LogError(
                ex,
                "Create-stack failure diagnostic timed out. OperationId={OperationId} Step={Step}",
                operationId,
                currentStep);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogError(
                ex,
                "Create-stack failure diagnostic could not be stored. OperationId={OperationId} Step={Step}",
                operationId,
                currentStep);
        }
    }

    private async Task TryCleanupDatabaseAsync(
        CreateChatStackRuntimeCommand command,
        RuntimeStackDatabaseProvisioningResult databaseProvisioning,
        IList<HostAgentEvidence> evidence)
    {
        using var cleanupCancellation = new CancellationTokenSource(CleanupTimeout);
        try
        {
            var result = await _databases.DropUnregisteredMatrixDatabaseAsync(
                command.StackId,
                command.StackSlug,
                databaseProvisioning.DatabaseName,
                databaseProvisioning.DatabaseUsername,
                cleanupCancellation.Token);

            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.postgres.pre-registration-cleanup.completed",
                Message: "MEM completed guarded PostgreSQL cleanup for the failed pre-registration stack attempt.",
                Data: new Dictionary<string, string?>
                {
                    ["dropped"] = result.Dropped ? "true" : "false",
                    ["databaseName"] = result.DatabaseName,
                    ["databaseUsername"] = result.DatabaseUsername,
                    ["detail"] = result.Detail
                }));
        }
        catch (OperationCanceledException ex) when (cleanupCancellation.IsCancellationRequested)
        {
            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.postgres.pre-registration-cleanup.timed-out",
                Message: "Guarded PostgreSQL cleanup did not finish within its bounded cleanup window.",
                Data: new Dictionary<string, string?>
                {
                    ["databaseName"] = databaseProvisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.DatabaseUsername,
                    ["recoveryRequired"] = "true"
                }));

            _logger.LogError(
                ex,
                "Create-stack guarded PostgreSQL cleanup timed out. RuntimeStackId={RuntimeStackId} StackSlug={StackSlug}",
                command.StackId,
                command.StackSlug);
        }
        catch (OperationCanceledException ex)
        {
            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.postgres.pre-registration-cleanup.failed",
                Message: "Guarded PostgreSQL cleanup was cancelled by a dependency before the bounded cleanup window expired.",
                Data: new Dictionary<string, string?>
                {
                    ["databaseName"] = databaseProvisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.DatabaseUsername,
                    ["recoveryRequired"] = "true"
                }));

            _logger.LogError(
                ex,
                "Create-stack guarded PostgreSQL cleanup was cancelled unexpectedly. RuntimeStackId={RuntimeStackId} StackSlug={StackSlug}",
                command.StackId,
                command.StackSlug);
        }
        catch (RuntimeStackDatabaseCleanupRefusedException ex)
        {
            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.postgres.pre-registration-cleanup.refused",
                Message: "MEM refused automatic PostgreSQL cleanup because the guarded ownership checks did not prove it was safe.",
                Data: new Dictionary<string, string?>
                {
                    ["databaseName"] = databaseProvisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.DatabaseUsername,
                    ["recoveryRequired"] = "true"
                }));

            _logger.LogWarning(
                ex,
                "Create-stack guarded PostgreSQL cleanup was refused. RuntimeStackId={RuntimeStackId} StackSlug={StackSlug}",
                command.StackId,
                command.StackSlug);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            evidence.Add(new HostAgentEvidence(
                Code: "host-agent.matrix.postgres.pre-registration-cleanup.failed",
                Message: "MEM could not complete guarded PostgreSQL cleanup for the failed pre-registration stack attempt.",
                Data: new Dictionary<string, string?>
                {
                    ["databaseName"] = databaseProvisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.DatabaseUsername,
                    ["recoveryRequired"] = "true"
                }));

            _logger.LogError(
                ex,
                "Create-stack guarded PostgreSQL cleanup failed. RuntimeStackId={RuntimeStackId} StackSlug={StackSlug}",
                command.StackId,
                command.StackSlug);
        }
    }

    private async Task TryJournalFailureAsync(
        Guid operationId,
        string currentStep,
        Exception failure,
        IList<HostAgentEvidence> evidence)
    {
        try
        {
            using var journalCancellation = new CancellationTokenSource(JournalTimeout);
            await _operations.FailAsync(
                operationId,
                currentStep,
                failure.Message,
                evidence,
                journalCancellation.Token);
        }
        catch (Exception journalError) when (journalError is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogError(
                journalError,
                "Create-stack Runtime Operation failure outcome could not be journalled. OperationId={OperationId} Step={Step}",
                operationId,
                currentStep);
        }
    }

    internal static string ClassifyFailure(
        Exception failure,
        bool requestCancellationRequested,
        bool operationTimeoutRequested = false,
        bool applicationStoppingRequested = false) =>
        failure switch
        {
            SynapseConfigGenerationCanceledException when operationTimeoutRequested =>
                "synapse-generation-operation-timeout",
            SynapseConfigGenerationCanceledException when applicationStoppingRequested =>
                "synapse-generation-application-stopping",
            SynapseConfigGenerationCanceledException synapseCancellation when synapseCancellation.CallerCancellationRequested =>
                "synapse-generation-caller-cancelled",
            SynapseConfigGenerationCanceledException =>
                "synapse-generation-docker-wait-cancelled",
            CreateChatStackPlatformTurnUnavailableException =>
                "platform-turn-not-ready",
            CreateChatStackTurnVerificationException =>
                "generated-turn-configuration-mismatch",
            OperationCanceledException when operationTimeoutRequested =>
                "operation-timeout",
            OperationCanceledException when applicationStoppingRequested =>
                "application-stopping",
            OperationCanceledException when requestCancellationRequested =>
                "operation-cancelled-after-transport-abort",
            OperationCanceledException =>
                "operation-cancelled",
            _ => "exception"
        };
}
