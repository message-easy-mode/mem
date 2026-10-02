using HostAgent.Runtime.Backups.Coordination;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.StandardRecreate.Reconciliation;

/// <summary>
/// Reconciles Standard Recreate operations that were persisted as running by a
/// previous Control Plane process. Standard Recreate executes in-process under a
/// server-owned lifetime; it cannot continue across an API process restart.
/// </summary>
public sealed class AbandonedStandardRecreateReconciliationService
{
    private static readonly TimeSpan TerminalEvidenceTimeout = TimeSpan.FromSeconds(20);

    private readonly MemDbContext _db;
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;
    private readonly ILogger<AbandonedStandardRecreateReconciliationService> _logger;

    public AbandonedStandardRecreateReconciliationService(
        MemDbContext db,
        RestoreAttemptCoordinator restoreAttemptCoordinator,
        ILogger<AbandonedStandardRecreateReconciliationService> logger)
    {
        _db = db;
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
        _logger = logger;
    }

    public async Task<AbandonedStandardRecreateReconciliationResult>
        ReconcileAfterControlPlaneStartAsync(CancellationToken ct)
    {
        var candidates = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(operation =>
                operation.Operation == "restore.standard-recreate" &&
                operation.Status == "running" &&
                operation.RestoreAttemptId != null)
            .Join(
                _db.RestoreAttempts.AsNoTracking(),
                operation => operation.RestoreAttemptId!.Value,
                attempt => attempt.Id,
                (operation, attempt) => new { Operation = operation, Attempt = attempt })
            .Where(item =>
                item.Attempt.Status == RestoreAttemptStatuses.Recreating &&
                item.Attempt.RuntimeOperationId == item.Operation.Id)
            .OrderBy(item => item.Operation.RequestedAtUtc)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return new AbandonedStandardRecreateReconciliationResult(
                ReconciledCount: 0,
                RestoreSessionIds: []);
        }

        var reconciled = new List<string>();

        foreach (var candidate in candidates)
        {
            // Once a previous-process operation has been identified, terminal
            // journaling must not depend on the startup request/lifetime token.
            // Give each reconciliation a bounded independent authority.
            using var terminalEvidenceCancellation =
                new CancellationTokenSource(TerminalEvidenceTimeout);

            const string errorCode =
                "restore.standard-recreate.abandoned-after-control-plane-restart";
            const string failureCategory = "control-plane-restart";
            const string operatorSummary =
                "The Control Plane restarted while Standard Recreate was recorded as running. " +
                "The prior in-process execution cannot continue after a Control Plane restart, " +
                "so MEM marked the abandoned operation as failed. Review the retained partial-resource evidence, " +
                "then cancel the restore before performing any reviewed cleanup.";

            try
            {
                await _restoreAttemptCoordinator.RecordStandardRecreateFailureAsync(
                    candidate.Attempt.Id,
                    candidate.Operation.Id,
                    errorCode,
                    failureCategory,
                    operatorSummary,
                    new
                    {
                        ReconciliationReason = "control-plane-restart",
                        PreviousOperationStatus = candidate.Operation.Status,
                        PreviousOperationStep = candidate.Operation.CurrentStep,
                        candidate.Operation.RequestedAtUtc,
                        candidate.Operation.StartedAtUtc,
                        candidate.Operation.LockedUntilUtc,
                        candidate.Attempt.RestoreSessionId
                    },
                    terminalEvidenceCancellation.Token);

                reconciled.Add(candidate.Attempt.RestoreSessionId);

                _logger.LogWarning(
                    "Reconciled abandoned Standard Recreate after Control Plane restart. RestoreSessionId={RestoreSessionId} RuntimeOperationId={RuntimeOperationId}",
                    candidate.Attempt.RestoreSessionId,
                    candidate.Operation.Id);
            }
            catch (Exception ex) when (
                ex is not StackOverflowException and not OutOfMemoryException)
            {
                // Startup reconciliation is best effort per item. One damaged
                // historical attempt must not prevent the Control Plane from
                // starting or block reconciliation of other attempts.
                _logger.LogError(
                    ex,
                    "Could not reconcile abandoned Standard Recreate after Control Plane restart. RestoreSessionId={RestoreSessionId} RuntimeOperationId={RuntimeOperationId}",
                    candidate.Attempt.RestoreSessionId,
                    candidate.Operation.Id);
            }
        }

        return new AbandonedStandardRecreateReconciliationResult(
            ReconciledCount: reconciled.Count,
            RestoreSessionIds: reconciled);
    }
}

public sealed record AbandonedStandardRecreateReconciliationResult(
    int ReconciledCount,
    IReadOnlyList<string> RestoreSessionIds);
