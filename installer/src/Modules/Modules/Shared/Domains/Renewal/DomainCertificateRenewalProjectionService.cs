using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Modules.Shared.Domains.Renewal;

/// <summary>
/// Composes the durable renewal policy/readiness state with the current
/// Domain-owned renewal operation. This projection is deliberately safe for
/// operator API/Web use: it never returns operation JSON, protected secret
/// material, provider tokens, host paths, or arbitrary exception text.
/// </summary>
public sealed class DomainCertificateRenewalProjectionService(
    MemDbContext db,
    DomainCertificateRenewalService renewal,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<DomainCertificateRenewalProjection>> ListAsync(
        CancellationToken cancellationToken)
    {
        var states = await renewal.ListAsync(cancellationToken);
        var results = new List<DomainCertificateRenewalProjection>(states.Count);
        foreach (var state in states)
        {
            results.Add(await BuildAsync(state, cancellationToken));
        }

        return results;
    }

    public async Task<DomainCertificateRenewalProjection?> GetAsync(
        Guid domainId,
        CancellationToken cancellationToken)
    {
        var state = await renewal.GetAsync(domainId, cancellationToken);
        return state is null
            ? null
            : await BuildAsync(state, cancellationToken);
    }

    public async Task<IReadOnlyList<DomainCertificateRenewalHistoryItem>> HistoryAsync(
        Guid domainId,
        CancellationToken cancellationToken)
    {
        var exists = await db.Domains
            .AsNoTracking()
            .AnyAsync(item => item.Id == domainId, cancellationToken);
        if (!exists)
        {
            return [];
        }

        var operations = await db.RuntimeOperations
            .AsNoTracking()
            .Where(item =>
                item.DomainId == domainId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName)
            .OrderByDescending(item => item.RequestedAtUtc)
            .ThenByDescending(item => item.Id)
            .Take(12)
            .ToListAsync(cancellationToken);

        return operations.Select(ToHistory).ToList();
    }

    private async Task<DomainCertificateRenewalProjection> BuildAsync(
        DomainCertificateRenewalState state,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var domain = await db.Domains
            .AsNoTracking()
            .Include(item => item.ActiveCertificate)
            .SingleAsync(item => item.Id == state.DomainId, cancellationToken);

        string? currentCycleKey = null;
        if (domain.ActiveCertificate is not null &&
            domain.ActiveCertificate.ExpiresAtUtc.HasValue)
        {
            currentCycleKey = DomainCertificateRenewalOperation.BuildIdempotencyKey(
                domain.Id,
                domain.ActiveCertificate.Id,
                domain.ActiveCertificate.ExpiresAtUtc.Value);
        }

        RuntimeOperationEntity? currentOperation = null;
        if (!string.IsNullOrWhiteSpace(currentCycleKey))
        {
            currentOperation = await db.RuntimeOperations
                .AsNoTracking()
                .Where(item =>
                    item.DomainId == state.DomainId &&
                    item.Operation == DomainCertificateRenewalOperation.OperationName &&
                    item.IdempotencyKey == currentCycleKey)
                .OrderByDescending(item => item.RequestedAtUtc)
                .ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var latestOperation = currentOperation ?? await db.RuntimeOperations
            .AsNoTracking()
            .Where(item =>
                item.DomainId == state.DomainId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName)
            .OrderByDescending(item => item.RequestedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var lastSuccessfulRenewalAtUtc = await db.RuntimeOperations
            .AsNoTracking()
            .Where(item =>
                item.DomainId == state.DomainId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName &&
                item.Status == DomainCertificateRenewalOperation.SucceededStatus &&
                item.CurrentStep == "activation-complete" &&
                item.CompletedAtUtc != null)
            .OrderByDescending(item => item.CompletedAtUtc)
            .Select(item => item.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var expiry = state.ActiveCertificateExpiresAtUtc.HasValue
            ? AsUtc(state.ActiveCertificateExpiresAtUtc.Value)
            : (DateTime?)null;
        var certificateExpired = expiry.HasValue && expiry.Value <= AsUtc(nowUtc);
        var daysRemaining = expiry.HasValue
            ? (int)Math.Ceiling((expiry.Value - AsUtc(nowUtc)).TotalDays)
            : (int?)null;
        var nextEligible = expiry.HasValue
            ? expiry.Value.AddDays(-Math.Max(0, state.RenewalWindowDays))
            : (DateTime?)null;

        var operatorRetryQueued = IsOperatorRetryQueued(currentOperation);
        var operationalStatus = ResolveOperationalStatus(
            state,
            currentOperation,
            expiry,
            nowUtc);

        DateTime? nextAutomaticAttempt = null;
        if (operatorRetryQueued)
        {
            nextAutomaticAttempt = currentOperation!.RequestedAtUtc;
        }
        else if (currentOperation is not null &&
            string.Equals(
                currentOperation.Status,
                DomainCertificateRenewalOperation.FailedStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            nextAutomaticAttempt = (currentOperation.CompletedAtUtc ?? currentOperation.RequestedAtUtc)
                .ToUniversalTime()
                .AddHours(Math.Max(1, state.RetryIntervalHours));
        }
        else if (operationalStatus == DomainRenewalOperationalStatuses.Scheduled)
        {
            nextAutomaticAttempt = nextEligible.HasValue && nextEligible.Value > AsUtc(nowUtc)
                ? nextEligible
                : AsUtc(nowUtc);
        }
        else if (operationalStatus == DomainRenewalOperationalStatuses.Queued)
        {
            nextAutomaticAttempt = currentOperation?.RequestedAtUtc;
        }

        string? diagnosticsIncidentId = null;
        string? diagnosticsHref = null;
        if (currentOperation is not null &&
            string.Equals(
                currentOperation.Status,
                DomainCertificateRenewalOperation.FailedStatus,
                StringComparison.OrdinalIgnoreCase) &&
            DomainCertificateRenewalOrchestrator.ShouldCreateRenewalIncident(
                currentOperation.LastError))
        {
            diagnosticsIncidentId =
                DomainCertificateRenewalOrchestrator.BuildRenewalIncidentId(currentOperation.Id);
            diagnosticsHref = $"/diagnostics/logs?incident={Uri.EscapeDataString(diagnosticsIncidentId)}";
        }
        else if (state.AutoRenewEnabled &&
                 state.HasActiveProductionCertificate &&
                 state.ReadinessStatus is
                     DomainRenewalReadinessStatuses.RenewalCredentialRequired or
                     DomainRenewalReadinessStatuses.AcmeEmailRequired)
        {
            diagnosticsIncidentId =
                DomainCertificateRenewalOrchestrator.BuildRenewalReadinessIncidentId(state.DomainId);
            diagnosticsHref = $"/diagnostics/logs?incident={Uri.EscapeDataString(diagnosticsIncidentId)}";
        }

        var currentStatus = currentOperation?.Status;
        var manualRenewAvailable =
            string.Equals(state.ReadinessStatus, DomainRenewalReadinessStatuses.Ready, StringComparison.Ordinal) &&
            !operatorRetryQueued &&
            !string.Equals(currentStatus, DomainCertificateRenewalOperation.RunningStatus, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentStatus, DomainCertificateRenewalOperation.QueuedStatus, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(currentStatus, DomainCertificateRenewalOperation.AwaitingActivationStatus, StringComparison.OrdinalIgnoreCase);

        return new DomainCertificateRenewalProjection(
            State: state,
            OperationalStatus: operationalStatus,
            CertificateExpired: certificateExpired,
            DaysRemaining: daysRemaining,
            NextEligibleRenewalAtUtc: nextEligible,
            NextAutomaticAttemptAtUtc: nextAutomaticAttempt,
            LastAttemptAtUtc: latestOperation?.CompletedAtUtc ??
                latestOperation?.StartedAtUtc ??
                latestOperation?.RequestedAtUtc,
            LastSuccessfulRenewalAtUtc: lastSuccessfulRenewalAtUtc,
            LatestOperationId: currentOperation?.Id ?? latestOperation?.Id,
            LatestOperationStatus: currentOperation?.Status ?? latestOperation?.Status,
            LatestOperationStep: currentOperation?.CurrentStep ?? latestOperation?.CurrentStep,
            LatestOperationAttemptCount: currentOperation?.AttemptCount ?? latestOperation?.AttemptCount ?? 0,
            LatestRequestedBy: latestOperation?.RequestedByUserId.HasValue == true
                ? "operator"
                : latestOperation?.RequestedBy,
            LatestErrorCode: currentOperation?.LastError,
            DiagnosticsIncidentId: diagnosticsIncidentId,
            DiagnosticsHref: diagnosticsHref,
            ManualRenewAvailable: manualRenewAvailable);
    }

    private static string ResolveOperationalStatus(
        DomainCertificateRenewalState state,
        RuntimeOperationEntity? currentOperation,
        DateTime? expiry,
        DateTime nowUtc)
    {
        if (!string.Equals(
                state.ReadinessStatus,
                DomainRenewalReadinessStatuses.Ready,
                StringComparison.Ordinal))
        {
            return string.Equals(
                    state.ReadinessStatus,
                    DomainRenewalReadinessStatuses.Disabled,
                    StringComparison.Ordinal)
                ? DomainRenewalOperationalStatuses.Disabled
                : DomainRenewalOperationalStatuses.Unready;
        }

        if (currentOperation is not null)
        {
            if (IsOperatorRetryQueued(currentOperation))
            {
                return DomainRenewalOperationalStatuses.Queued;
            }

            if (string.Equals(currentOperation.Status, DomainCertificateRenewalOperation.FailedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return DomainRenewalOperationalStatuses.Failed;
            }

            if (string.Equals(currentOperation.Status, DomainCertificateRenewalOperation.RunningStatus, StringComparison.OrdinalIgnoreCase))
            {
                return DomainRenewalOperationalStatuses.Running;
            }

            if (string.Equals(currentOperation.Status, DomainCertificateRenewalOperation.AwaitingActivationStatus, StringComparison.OrdinalIgnoreCase))
            {
                return DomainRenewalOperationalStatuses.AwaitingActivation;
            }

            if (string.Equals(currentOperation.Status, DomainCertificateRenewalOperation.QueuedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return DomainRenewalOperationalStatuses.Queued;
            }
        }

        if (expiry.HasValue &&
            DomainCertificateRenewalOrchestrator.IsDue(
                expiry.Value,
                state.RenewalWindowDays,
                nowUtc))
        {
            return DomainRenewalOperationalStatuses.Scheduled;
        }

        return DomainRenewalOperationalStatuses.Ready;
    }

    private static bool IsOperatorRetryQueued(RuntimeOperationEntity? operation) =>
        operation is not null &&
        string.Equals(
            operation.Status,
            DomainCertificateRenewalOperation.FailedStatus,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(operation.RequestedBy, "operator", StringComparison.OrdinalIgnoreCase) &&
        operation.CompletedAtUtc.HasValue &&
        operation.RequestedAtUtc > operation.CompletedAtUtc.Value;

    private static DomainCertificateRenewalHistoryItem ToHistory(RuntimeOperationEntity operation)
    {
        string? incidentId = null;
        string? href = null;
        if (string.Equals(
                operation.Status,
                DomainCertificateRenewalOperation.FailedStatus,
                StringComparison.OrdinalIgnoreCase) &&
            DomainCertificateRenewalOrchestrator.ShouldCreateRenewalIncident(operation.LastError))
        {
            incidentId = DomainCertificateRenewalOrchestrator.BuildRenewalIncidentId(operation.Id);
            href = $"/diagnostics/logs?incident={Uri.EscapeDataString(incidentId)}";
        }

        return new DomainCertificateRenewalHistoryItem(
            OperationId: operation.Id,
            Status: operation.Status,
            Step: operation.CurrentStep,
            RequestedBy: operation.RequestedByUserId.HasValue ? "operator" : (operation.RequestedBy ?? "system"),
            RequestedAtUtc: operation.RequestedAtUtc,
            StartedAtUtc: operation.StartedAtUtc,
            CompletedAtUtc: operation.CompletedAtUtc,
            AttemptCount: operation.AttemptCount,
            ErrorCode: operation.LastError,
            DiagnosticsIncidentId: incidentId,
            DiagnosticsHref: href);
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
