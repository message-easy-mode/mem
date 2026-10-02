using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using HostAgent.Runtime.Operations;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnPlatformInstallOperationService
{
    public const string OperationName = "install-platform-coturn";
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);
    private readonly MemDbContext _db;
    private readonly RuntimeOperationStore _operations;
    private readonly CoturnPlatformInstallBackgroundDispatcher _dispatcher;
    private readonly CoturnPlatformMutationAdmissionGate _admissionGate;

    public CoturnPlatformInstallOperationService(
        MemDbContext db,
        RuntimeOperationStore operations,
        CoturnPlatformInstallBackgroundDispatcher dispatcher,
        CoturnPlatformMutationAdmissionGate admissionGate)
    {
        _db = db;
        _operations = operations;
        _dispatcher = dispatcher;
        _admissionGate = admissionGate;
    }

    public async Task<CoturnPlatformInstallAcceptedResponse> AcceptAsync(
        CoturnPlatformInstallRequest request,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var admission = await _admissionGate.EnterAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var active = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x =>
                (x.Operation == OperationName ||
                 x.Operation == CoturnPlatformMaintenanceOperationService.OperationName ||
                 x.Operation == CoturnStartupSupervisionProcessor.OperationName) &&
                x.Status == "running" &&
                x.CompletedAtUtc == null &&
                (x.LockedUntilUtc == null || x.LockedUntilUtc > now))
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (active is not null)
        {
            if (string.Equals(active.Operation, OperationName, StringComparison.Ordinal))
            {
                return Accepted(active.Id, reusedExistingOperation: true);
            }

            throw new CoturnPlatformInstallException(
                "coturn_operation_in_progress",
                "Another shared platform TURN mutation is already running. Wait for it to finish before installing or repairing the service.",
                StatusCodes.Status409Conflict);
        }

        // Once admission has succeeded, create and queue the durable operation
        // under a bounded server-owned journal lifetime rather than the browser
        // request lifetime.
        using var acceptanceJournal = new CancellationTokenSource(FailureJournalTimeout);
        var operationId = await _operations.StartAsync(
            runtimeStackId: null,
            operation: OperationName,
            idempotencyKey: null,
            requestedBy: string.IsNullOrWhiteSpace(requestedBy)
                ? "platform-owner"
                : requestedBy.Trim(),
            hostMutationLevel: "docker,platform-turn,host-ports",
            input: new
            {
                externalIpConfigured = !string.IsNullOrWhiteSpace(request.ExternalIp),
                publishRelayPorts = true,
                imageBoundary = "installation-approved-immutable"
            },
            acceptanceJournal.Token);

        // The durable RuntimeOperation exists now. Queue journaling must not
        // inherit HttpContext.RequestAborted after acceptance.
        using var queueJournal = new CancellationTokenSource(FailureJournalTimeout);
        await _operations.UpdateStepAsync(
            operationId,
            "queued",
            queueJournal.Token);

        if (!_dispatcher.TryEnqueue(operationId, request))
        {
            using var journalTimeout = new CancellationTokenSource(FailureJournalTimeout);
            await _operations.FailAsync(
                operationId,
                currentStep: "queued",
                error: "The platform TURN installation queue is full.",
                evidence: new
                {
                    failureKind = "coturn-install-background-queue-full"
                },
                journalTimeout.Token);

            throw new CoturnPlatformInstallException(
                "coturn_install_queue_full",
                "MEM could not accept another shared platform TURN installation operation at this time.",
                StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted(operationId, reusedExistingOperation: false);
    }

    private static CoturnPlatformInstallAcceptedResponse Accepted(
        Guid operationId,
        bool reusedExistingOperation)
    {
        var pollUrl = $"/internal/host-agent/operations/{operationId:D}";
        return new CoturnPlatformInstallAcceptedResponse(
            operationId,
            Status: "accepted",
            PollUrl: pollUrl,
            ReusedExistingOperation: reusedExistingOperation);
    }
}
