using HostAgent.Runtime.Operations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modules.Setup.Platform.Coturn;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnPlatformInstallOperationProcessor
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan TerminalJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly IPlatformCoturnSetupService _coturn;
    private readonly RuntimeOperationStore _operations;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<CoturnPlatformInstallOperationProcessor> _logger;

    public CoturnPlatformInstallOperationProcessor(
        IPlatformCoturnSetupService coturn,
        RuntimeOperationStore operations,
        IHostApplicationLifetime applicationLifetime,
        ILogger<CoturnPlatformInstallOperationProcessor> logger)
    {
        _coturn = coturn;
        _operations = operations;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public async Task ExecuteAsync(
        Guid operationId,
        CoturnPlatformInstallRequest request)
    {
        using var timeout = new CancellationTokenSource(OperationTimeout);
        using var operationLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token,
            _applicationLifetime.ApplicationStopping);
        var ct = operationLifetime.Token;

        try
        {
            await _operations.UpdateStepAsync(operationId, "install-platform-turn", ct);

            var installed = await _coturn.EnsureInstalledAsync(
                new PlatformCoturnSetupRequest(
                    ExternalIp: request.ExternalIp,
                    ForceRecreate: false),
                ct);

            if (!installed.Ready)
            {
                throw new InvalidOperationException(
                    installed.Detail ??
                    "The shared platform TURN runtime did not reach structural Ready state.");
            }

            await _operations.UpdateStepAsync(operationId, "verify-platform-turn", ct);
            var verified = await _coturn.InspectAsync(ct);

            if (!verified.Ready)
            {
                throw new InvalidOperationException(
                    verified.Detail ??
                    "The shared platform TURN runtime did not remain structurally Ready after installation.");
            }

            using var terminal = new CancellationTokenSource(TerminalJournalTimeout);
            await _operations.CompleteAsync(
                operationId,
                status: "succeeded",
                currentStep: "completed",
                result: new
                {
                    verified.ContainerName,
                    verified.PublicHost,
                    verified.Realm,
                    verified.Readiness,
                    verified.Running,
                    verified.OwnershipVerified,
                    verified.ImageApproved,
                    verified.SecretPresent,
                    verified.RelayPortsPublished,
                    verified.SecurityPolicyApplied,
                    verified.PublishedPorts,
                    verified.Warnings
                },
                evidence: new
                {
                    imageBoundary = "installation-approved-immutable",
                    approvedImageReference = verified.ApprovedImageReference,
                    verified.ResolvedImageId,
                    externalClientReachabilityProven = false
                },
                terminal.Token);
        }
        catch (OperationCanceledException ex)
        {
            var reason = timeout.IsCancellationRequested
                ? "The shared platform TURN installation exceeded its bounded server-owned timeout."
                : _applicationLifetime.ApplicationStopping.IsCancellationRequested
                    ? "MEM stopped while the shared platform TURN installation was running."
                    : "The shared platform TURN installation was cancelled.";

            await TryFailAsync(operationId, reason, ex);
        }
        catch (Exception ex)
        {
            await TryFailAsync(
                operationId,
                "Shared platform TURN installation failed. Review the Coturn workspace and Diagnostics for bounded evidence.",
                ex);
        }
    }

    private async Task TryFailAsync(Guid operationId, string error, Exception exception)
    {
        _logger.LogWarning(
            "Platform TURN installation failed. OperationId={OperationId} ExceptionType={ExceptionType}",
            operationId,
            exception.GetType().FullName);

        try
        {
            using var journal = new CancellationTokenSource(TerminalJournalTimeout);
            var detail = await _operations.FindByIdAsync(operationId, journal.Token);
            if (detail?.CompletedAtUtc is null)
            {
                await _operations.FailAsync(
                    operationId,
                    currentStep: detail?.CurrentStep ?? "platform-turn",
                    error: error,
                    evidence: new
                    {
                        failureKind = "coturn-install-failed",
                        exceptionType = exception.GetType().FullName
                    },
                    journal.Token);
            }
        }
        catch (Exception journalException)
        {
            _logger.LogError(
                journalException,
                "Could not persist terminal failure for platform TURN installation operation {OperationId}.",
                operationId);
        }
    }
}
