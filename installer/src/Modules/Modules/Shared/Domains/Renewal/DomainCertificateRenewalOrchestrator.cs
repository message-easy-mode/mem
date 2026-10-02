using System.Net.Mail;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates;
using Shared.Diagnostics;

namespace Modules.Shared.Domains.Renewal;

public sealed class DomainCertificateRenewalOrchestrator(
    MemDbContext db,
    IDomainSecretStore secretStore,
    IDomainCertificateRenewalCandidateIssuer candidateIssuer,
    TimeProvider timeProvider,
    ILogger<DomainCertificateRenewalOrchestrator> logger,
    IMemDiagnosticEventWriter? diagnostics = null,
    IMemDiagnosticIncidentLifecycleWriter? incidentLifecycle = null,
    IDomainCertificateRenewalCandidateActivator? candidateActivator = null,
    IMemDiagnosticEventReader? diagnosticReader = null)
{
    private static readonly SemaphoreSlim ScanGate = new(1, 1);
    private static readonly TimeSpan OperationLease = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DomainCertificateRenewalScanResult> ScanAsync(
        CancellationToken cancellationToken)
    {
        await ScanGate.WaitAsync(cancellationToken);
        try
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var domains = await db.Domains
                .AsNoTracking()
                .Include(domain => domain.CertificateRenewalPolicy)
                .Include(domain => domain.ActiveCertificate)
                .OrderByDescending(domain => domain.IsMainPlatformDomain)
                .ThenBy(domain => domain.BaseDomain)
                .ToListAsync(cancellationToken);

            var due = 0;
            var started = 0;
            var awaitingActivation = 0;
            var failed = 0;

            foreach (var domain in domains)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var policy = domain.CertificateRenewalPolicy;
                var source = domain.ActiveCertificate;

                if (!IsReadyLifecycle(domain, policy, source))
                {
                    continue;
                }

                var idempotencyKey = DomainCertificateRenewalOperation.BuildIdempotencyKey(
                    domain.Id,
                    source!.Id,
                    source.ExpiresAtUtc!.Value);

                var operation = await FindOperationAsync(
                    domain.Id,
                    idempotencyKey,
                    cancellationToken);
                var automaticDue = IsDue(
                    source.ExpiresAtUtc.Value,
                    policy!.RenewalWindowDays,
                    nowUtc);
                var manualCycle =
                    operation is not null &&
                    (operation.RequestedByUserId.HasValue ||
                     string.Equals(operation.RequestedBy, "operator", StringComparison.OrdinalIgnoreCase));

                // Activation is the continuation of an already-issued, validated candidate.
                // It must not be blocked by DNS-provider/credential readiness checks that only
                // govern whether MEM may perform a new ACME issuance. This also preserves
                // restart recovery for awaiting-activation operations if the renewal credential
                // is rotated or removed after issuance but before activation completes.
                if (operation is not null && string.Equals(
                        operation.Status,
                        DomainCertificateRenewalOperation.AwaitingActivationStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    due++;

                    if (candidateActivator is null)
                    {
                        continue;
                    }

                    if (operation.LockedUntilUtc is not null && operation.LockedUntilUtc > nowUtc)
                    {
                        continue;
                    }

                    if (!await TryAcquireActivationLeaseAsync(operation.Id, nowUtc, cancellationToken))
                    {
                        continue;
                    }

                    started++;
                    try
                    {
                        var activationOutcome = await ProcessAwaitingActivationAsync(
                            operation.Id,
                            domain.Id,
                            source.Id,
                            cancellationToken);

                        if (activationOutcome == ProcessOutcome.Failed)
                        {
                            failed++;
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        await MarkInterruptedAsync(operation.Id);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(
                            "Automatic certificate renewal activation failed unexpectedly. OperationId={OperationId} DomainId={DomainId} FailureType={FailureType}",
                            operation.Id,
                            domain.Id,
                            exception.GetType().Name);

                        await MarkFailedWithDiagnosticsAsync(
                            operation.Id,
                            "RenewalActivationWorkerFailed",
                            domain,
                            source,
                            policy,
                            CancellationToken.None);
                        failed++;
                    }

                    continue;
                }

                var providerSupported = string.Equals(
                    domain.DnsProvider,
                    "desec",
                    StringComparison.OrdinalIgnoreCase);

                if (providerSupported)
                {
                    var readinessBlocker = await ResolveImmediateReadinessBlockerAsync(
                        domain,
                        policy,
                        source,
                        cancellationToken);
                    if (readinessBlocker is not null)
                    {
                        await RecordRenewalReadinessFailureAsync(
                            domain,
                            source,
                            policy,
                            readinessBlocker,
                            cancellationToken);
                        continue;
                    }

                    await RecordRenewalReadinessRecoveredIfNeededAsync(
                        domain,
                        source,
                        cancellationToken);
                }

                // Unsupported providers still enter a due renewal cycle so the durable
                // operation fails explicitly with UnsupportedDnsProvider, preserving the
                // established 01B contract. We intentionally do not resolve a previous
                // deSEC-readiness incident merely because the provider changed.
                if (!automaticDue && !manualCycle)
                {
                    continue;
                }

                due++;

                operation ??= await GetOrCreateOperationAsync(
                    domain,
                    source,
                    idempotencyKey,
                    nowUtc,
                    cancellationToken);

                if (string.Equals(
                        operation.Status,
                        DomainCertificateRenewalOperation.SucceededStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(
                        operation.Status,
                        DomainCertificateRenewalOperation.RunningStatus,
                        StringComparison.OrdinalIgnoreCase) &&
                    operation.LockedUntilUtc is not null &&
                    operation.LockedUntilUtc > nowUtc)
                {
                    continue;
                }

                var operatorRetryRequested =
                    string.Equals(operation.RequestedBy, "operator", StringComparison.OrdinalIgnoreCase) &&
                    operation.CompletedAtUtc.HasValue &&
                    operation.RequestedAtUtc > operation.CompletedAtUtc.Value;

                if (string.Equals(
                        operation.Status,
                        DomainCertificateRenewalOperation.FailedStatus,
                        StringComparison.OrdinalIgnoreCase) &&
                    !operatorRetryRequested &&
                    !RetryBoundaryReached(operation, policy!, nowUtc))
                {
                    continue;
                }

                var recoveringIncident =
                    string.Equals(
                        operation.Status,
                        DomainCertificateRenewalOperation.FailedStatus,
                        StringComparison.OrdinalIgnoreCase) &&
                    ShouldCreateRenewalIncident(operation.LastError);

                if (!await TryAcquireLeaseAsync(operation.Id, nowUtc, cancellationToken))
                {
                    continue;
                }

                started++;

                try
                {
                    var outcome = await ProcessOperationAsync(
                        operation.Id,
                        domain.Id,
                        source.Id,
                        recoveringIncident,
                        cancellationToken);

                    if (outcome == ProcessOutcome.AwaitingActivation)
                    {
                        awaitingActivation++;
                    }
                    else if (outcome == ProcessOutcome.Failed)
                    {
                        failed++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await MarkInterruptedAsync(operation.Id);
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        "Automatic certificate renewal operation failed unexpectedly. OperationId={OperationId} DomainId={DomainId} FailureType={FailureType}",
                        operation.Id,
                        domain.Id,
                        exception.GetType().Name);

                    await MarkFailedWithDiagnosticsAsync(
                        operation.Id,
                        "RenewalWorkerFailed",
                        domain,
                        source,
                        policy,
                        CancellationToken.None);
                    failed++;
                }
            }

            return new DomainCertificateRenewalScanResult(
                DomainsEvaluated: domains.Count,
                RenewalCyclesDue: due,
                OperationsStarted: started,
                AwaitingActivation: awaitingActivation,
                Failed: failed);
        }
        finally
        {
            ScanGate.Release();
        }
    }

    public async Task<DomainCertificateRenewalQueueResult> QueueManualRenewalAsync(
        Guid domainId,
        Guid? requestedByUserId,
        CancellationToken cancellationToken)
    {
        await ScanGate.WaitAsync(cancellationToken);
        try
        {
            var domain = await db.Domains
                .AsNoTracking()
                .Include(item => item.CertificateRenewalPolicy)
                .Include(item => item.ActiveCertificate)
                .SingleOrDefaultAsync(item => item.Id == domainId, cancellationToken);

            if (domain is null)
            {
                return new(false, "NotFound", $"Domain '{domainId}' was not found.", null);
            }

            var policy = domain.CertificateRenewalPolicy;
            var source = domain.ActiveCertificate;
            if (!IsReadyLifecycle(domain, policy, source))
            {
                return new(
                    false,
                    "RenewalNotReady",
                    "Automatic renewal must be enabled with an active production certificate before Renew now can be queued.",
                    null);
            }

            if (!string.Equals(domain.DnsProvider, "desec", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, "UnsupportedDnsProvider", "Renew now currently supports deSEC-managed Domains only.", null);
            }

            if (!MailAddress.TryCreate(policy!.AcmeEmail, out _))
            {
                return new(false, "AcmeEmailRequired", "A valid ACME contact email is required before Renew now can be queued.", null);
            }

            var credentialConfigured = await db.DomainSecrets
                .AsNoTracking()
                .AnyAsync(item =>
                    item.DomainId == domainId &&
                    item.Category == DomainRenewalSecretNames.DnsProviderCategory &&
                    item.Key == DomainRenewalSecretNames.DesecProviderToken,
                    cancellationToken);
            if (!credentialConfigured)
            {
                return new(false, "RenewalCredentialRequired", "A verified deSEC renewal credential is required before Renew now can be queued.", null);
            }

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var idempotencyKey = DomainCertificateRenewalOperation.BuildIdempotencyKey(
                domain.Id,
                source!.Id,
                source.ExpiresAtUtc!.Value);

            var operation = await FindOperationAsync(
                domain.Id,
                idempotencyKey,
                cancellationToken);

            if (operation is null)
            {
                operation = await GetOrCreateOperationAsync(
                    domain,
                    source,
                    idempotencyKey,
                    nowUtc,
                    cancellationToken,
                    requestedBy: "operator",
                    requestedByUserId: requestedByUserId);
            }
            else
            {
                if (string.Equals(
                        operation.Status,
                        DomainCertificateRenewalOperation.SucceededStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new(
                        true,
                        "AlreadyCompleted",
                        "The current renewal cycle is already complete.",
                        operation.Id);
                }

                await db.RuntimeOperations
                    .Where(item => item.Id == operation.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(item => item.RequestedBy, "operator")
                            .SetProperty(item => item.RequestedByUserId, requestedByUserId)
                            .SetProperty(item => item.RequestedAtUtc, nowUtc),
                        cancellationToken);

                operation.RequestedBy = "operator";
                operation.RequestedByUserId = requestedByUserId;
                operation.RequestedAtUtc = nowUtc;
            }

            return new(
                true,
                "Queued",
                "Renewal was queued for the server-owned renewal worker.",
                operation.Id);
        }
        finally
        {
            ScanGate.Release();
        }
    }

    internal static bool IsDue(
        DateTime expiresAtUtc,
        int renewalWindowDays,
        DateTime nowUtc)
    {
        var expiry = AsUtc(expiresAtUtc);
        var now = AsUtc(nowUtc);
        var window = Math.Max(0, renewalWindowDays);
        return expiry <= now.AddDays(window);
    }

    private async Task<ProcessOutcome> ProcessOperationAsync(
        Guid operationId,
        Guid domainId,
        Guid sourceCertificateEntityId,
        bool recoveringIncident,
        CancellationToken cancellationToken)
    {
        var domain = await db.Domains
            .AsNoTracking()
            .Include(item => item.CertificateRenewalPolicy)
            .Include(item => item.ActiveCertificate)
            .FirstOrDefaultAsync(item => item.Id == domainId, cancellationToken);

        if (domain is null ||
            domain.ActiveCertificate is null ||
            domain.ActiveCertificateId != sourceCertificateEntityId ||
            domain.ActiveCertificate.Id != sourceCertificateEntityId)
        {
            await MarkSupersededAsync(
                operationId,
                "SourceCertificateChanged",
                cancellationToken);
            return ProcessOutcome.Superseded;
        }

        var source = domain.ActiveCertificate;
        var policy = domain.CertificateRenewalPolicy;

        if (domain.IsMainPlatformDomain != source.IsMainPlatformCertificate)
        {
            await MarkFailedWithDiagnosticsAsync(
                operationId,
                "MainPlatformCertificateRoleInconsistent",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        if (policy is null ||
            !policy.AutoRenewEnabled ||
            source.IsStaging ||
            string.Equals(source.Status, "Deleted", StringComparison.OrdinalIgnoreCase) ||
            !source.IsActive ||
            source.ExpiresAtUtc is null)
        {
            // The same source certificate may become eligible again (for example
            // after an operator re-enables the policy). Keep the cycle retryable
            // instead of permanently completing its stable idempotency key.
            await MarkFailedAsync(
                operationId,
                "RenewalCycleNoLongerEligible",
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        var request = BuildCandidateRequest(
            operationId,
            domain,
            source,
            policy);

        await UpdateProgressAsync(
            operationId,
            "candidate-recovery",
            isHeartbeat: false,
            cancellationToken);

        var recovered = await candidateIssuer.TryRecoverCandidateAsync(
            request,
            cancellationToken);
        if (recovered?.Succeeded == true &&
            recovered.CandidateCertificateEntityId.HasValue &&
            !string.IsNullOrWhiteSpace(recovered.CandidateCertificateId))
        {
            await MarkAwaitingActivationAsync(
                operationId,
                recovered,
                retainLease: candidateActivator is not null,
                recoveringIncident,
                cancellationToken);

            if (candidateActivator is null)
            {
                if (recoveringIncident)
                {
                    await RecordRenewalRecoveredAsync(
                        operationId,
                        domain,
                        source,
                        recovered);
                }

                return ProcessOutcome.AwaitingActivation;
            }

            return await ActivateCandidateAsync(
                operationId,
                domain,
                source,
                policy,
                recovered,
                recoveringIncident,
                cancellationToken);
        }

        if (recovered is { Succeeded: false } &&
            string.Equals(
                recovered.ErrorCode,
                "SourceCertificateChanged",
                StringComparison.OrdinalIgnoreCase))
        {
            await MarkSupersededAsync(
                operationId,
                "SourceCertificateChanged",
                cancellationToken);
            return ProcessOutcome.Superseded;
        }

        if (!string.Equals(
                domain.DnsProvider,
                "desec",
                StringComparison.OrdinalIgnoreCase))
        {
            await MarkFailedWithDiagnosticsAsync(
                operationId,
                "UnsupportedDnsProvider",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        var email = NormalizeEmail(policy.AcmeEmail);
        if (email is null)
        {
            await MarkFailedWithDiagnosticsAsync(
                operationId,
                "AcmeEmailRequired",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        await UpdateProgressAsync(
            operationId,
            "credential",
            isHeartbeat: false,
            cancellationToken);

        string? providerToken;
        try
        {
            // Resolve the secret only at the mutation boundary immediately before
            // DNS/ACME work. It must never be copied into operation persistence.
            providerToken = await secretStore.ResolveProtectedAsync(
                domain.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                DomainRenewalSecretNames.DesecProviderToken,
                cancellationToken);
        }
        catch
        {
            await MarkFailedWithDiagnosticsAsync(
                operationId,
                "RenewalCredentialUnavailable",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        if (string.IsNullOrWhiteSpace(providerToken))
        {
            await MarkFailedWithDiagnosticsAsync(
                operationId,
                "RenewalCredentialRequired",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        var issueRequest = request with { AcmeEmail = email };
        var issued = await candidateIssuer.IssueCandidateAsync(
            issueRequest,
            providerToken,
            (progress, token) => UpdateProgressAsync(
                operationId,
                progress.PhaseCode,
                progress.IsHeartbeat,
                token),
            cancellationToken);

        // Do not retain the credential past the call boundary.
        providerToken = null;

        if (!issued.Succeeded ||
            !issued.CandidateCertificateEntityId.HasValue ||
            string.IsNullOrWhiteSpace(issued.CandidateCertificateId))
        {
            if (string.Equals(
                    issued.ErrorCode,
                    "SourceCertificateChanged",
                    StringComparison.OrdinalIgnoreCase))
            {
                await MarkSupersededAsync(
                    operationId,
                    "SourceCertificateChanged",
                    cancellationToken);
                return ProcessOutcome.Superseded;
            }

            await MarkFailedWithDiagnosticsAsync(
                operationId,
                issued.ErrorCode ?? "CertificateIssueFailed",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        await MarkAwaitingActivationAsync(
            operationId,
            issued,
            retainLease: candidateActivator is not null,
            recoveringIncident,
            cancellationToken);

        if (candidateActivator is null)
        {
            if (recoveringIncident)
            {
                await RecordRenewalRecoveredAsync(
                    operationId,
                    domain,
                    source,
                    issued);
            }

            return ProcessOutcome.AwaitingActivation;
        }

        return await ActivateCandidateAsync(
            operationId,
            domain,
            source,
            policy,
            issued,
            recoveringIncident,
            cancellationToken);
    }

    private async Task<ProcessOutcome> ProcessAwaitingActivationAsync(
        Guid operationId,
        Guid domainId,
        Guid sourceCertificateEntityId,
        CancellationToken cancellationToken)
    {
        if (candidateActivator is null)
        {
            return ProcessOutcome.AwaitingActivation;
        }

        var domain = await db.Domains
            .AsNoTracking()
            .Include(item => item.CertificateRenewalPolicy)
            .Include(item => item.ActiveCertificate)
            .FirstOrDefaultAsync(item => item.Id == domainId, cancellationToken);

        if (domain is null ||
            domain.ActiveCertificate is null ||
            domain.ActiveCertificateId != sourceCertificateEntityId ||
            domain.ActiveCertificate.Id != sourceCertificateEntityId)
        {
            await MarkSupersededAsync(
                operationId,
                "SourceCertificateChanged",
                cancellationToken);
            return ProcessOutcome.Superseded;
        }

        var source = domain.ActiveCertificate;
        var policy = domain.CertificateRenewalPolicy;
        if (policy is null || !policy.AutoRenewEnabled)
        {
            await MarkFailedAsync(
                operationId,
                "RenewalCycleNoLongerEligible",
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == operationId, cancellationToken);
        var recoveringIncident = OperationWasRecoveringIncident(operation?.EvidenceJson);

        var request = BuildCandidateRequest(
            operationId,
            domain,
            source,
            policy);
        var recovered = await candidateIssuer.TryRecoverCandidateAsync(
            request,
            cancellationToken);

        if (recovered is { Succeeded: false } &&
            string.Equals(
                recovered.ErrorCode,
                "SourceCertificateChanged",
                StringComparison.OrdinalIgnoreCase))
        {
            await MarkSupersededAsync(
                operationId,
                "SourceCertificateChanged",
                cancellationToken);
            return ProcessOutcome.Superseded;
        }

        if (recovered?.Succeeded != true ||
            !recovered.CandidateCertificateEntityId.HasValue ||
            string.IsNullOrWhiteSpace(recovered.CandidateCertificateId))
        {
            await MarkFailedWithDiagnosticsAsync(
                operationId,
                "RenewalCandidateMissingForActivation",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        return await ActivateCandidateAsync(
            operationId,
            domain,
            source,
            policy,
            recovered,
            recoveringIncident,
            cancellationToken);
    }

    private async Task<ProcessOutcome> ActivateCandidateAsync(
        Guid operationId,
        DomainEntity domain,
        CertificateEntity source,
        DomainCertificateRenewalPolicyEntity? policy,
        DomainRenewalCandidateIssueResult candidate,
        bool recoveringIncident,
        CancellationToken cancellationToken)
    {
        if (candidateActivator is null ||
            !candidate.CandidateCertificateEntityId.HasValue ||
            string.IsNullOrWhiteSpace(candidate.CandidateCertificateId))
        {
            return ProcessOutcome.AwaitingActivation;
        }

        await SetActivationProgressAsync(operationId, cancellationToken);

        var activation = await candidateActivator.ActivateAsync(
            new DomainRenewalCandidateActivationRequest(
                OperationId: operationId,
                DomainId: domain.Id,
                SourceCertificateEntityId: source.Id,
                SourceCertificateId: source.CertificateId,
                CandidateCertificateEntityId: candidate.CandidateCertificateEntityId.Value,
                CandidateCertificateId: candidate.CandidateCertificateId!),
            cancellationToken);

        if (!activation.Succeeded)
        {
            if (string.Equals(
                    activation.ErrorCode,
                    "SourceCertificateChanged",
                    StringComparison.OrdinalIgnoreCase))
            {
                await MarkSupersededAsync(
                    operationId,
                    "SourceCertificateChanged",
                    cancellationToken);
                return ProcessOutcome.Superseded;
            }

            await MarkFailedWithDiagnosticsAsync(
                operationId,
                activation.ErrorCode ?? "RenewalCandidateActivationFailed",
                domain,
                source,
                policy,
                cancellationToken);
            return ProcessOutcome.Failed;
        }

        if (recoveringIncident)
        {
            await RecordRenewalActivationRecoveredAsync(
                operationId,
                domain,
                source,
                candidate);
        }

        return ProcessOutcome.Activated;
    }

    private static DomainRenewalCandidateRequest BuildCandidateRequest(
        Guid operationId,
        DomainEntity domain,
        CertificateEntity source,
        DomainCertificateRenewalPolicyEntity policy) =>
        new(
            OperationId: operationId,
            DomainId: domain.Id,
            BaseDomain: domain.BaseDomain,
            Zone: string.IsNullOrWhiteSpace(domain.DnsZone)
                ? domain.BaseDomain
                : domain.DnsZone!,
            DnsProvider: domain.DnsProvider,
            AcmeEmail: policy.AcmeEmail ?? string.Empty,
            SourceCertificateEntityId: source.Id,
            SourceCertificateId: source.CertificateId,
            CommonName: source.CommonName,
            IsWildcard: source.IsWildcard,
            SourceCertificateExpiresAtUtc: source.ExpiresAtUtc!.Value);

    private Task<RuntimeOperationEntity?> FindOperationAsync(
        Guid domainId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.DomainId == domainId &&
                    item.Operation == DomainCertificateRenewalOperation.OperationName &&
                    item.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private async Task<RuntimeOperationEntity> GetOrCreateOperationAsync(
        DomainEntity domain,
        CertificateEntity source,
        string idempotencyKey,
        DateTime nowUtc,
        CancellationToken cancellationToken,
        string requestedBy = "system",
        Guid? requestedByUserId = null)
    {
        var existing = await db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                item =>
                    item.DomainId == domain.Id &&
                    item.Operation == DomainCertificateRenewalOperation.OperationName &&
                    item.IdempotencyKey == idempotencyKey,
                cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var entity = new RuntimeOperationEntity
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Operation = DomainCertificateRenewalOperation.OperationName,
            Status = DomainCertificateRenewalOperation.QueuedStatus,
            IdempotencyKey = idempotencyKey,
            RequestedBy = requestedBy,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            CurrentStep = "eligibility",
            AttemptCount = 0,
            InputJson = JsonSerializer.Serialize(
                new
                {
                    domainId = domain.Id,
                    sourceCertificateEntityId = source.Id,
                    sourceCertificateId = source.CertificateId,
                    sourceExpiresAtUtc = source.ExpiresAtUtc
                },
                JsonOptions),
            HostMutationLevel = "certificate-issuance",
            RequiresConfirmation = false
        };

        db.RuntimeOperations.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            db.Entry(entity).State = EntityState.Detached;
            return entity;
        }
        catch (DbUpdateException)
        {
            // The unique Domain/operation/idempotency index is the cross-process
            // boundary. If another Control Plane won the create race, detach our
            // losing row and continue from the authoritative durable cycle.
            db.Entry(entity).State = EntityState.Detached;

            var concurrent = await db.RuntimeOperations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    item =>
                        item.DomainId == domain.Id &&
                        item.Operation == DomainCertificateRenewalOperation.OperationName &&
                        item.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (concurrent is not null)
            {
                return concurrent;
            }

            throw;
        }
    }

    private async Task<bool> TryAcquireActivationLeaseAsync(
        Guid operationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var affected = await db.RuntimeOperations
            .Where(item =>
                item.Id == operationId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName &&
                item.Status == DomainCertificateRenewalOperation.AwaitingActivationStatus &&
                (item.LockedUntilUtc == null || item.LockedUntilUtc <= nowUtc))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CurrentStep, "activation")
                    .SetProperty(item => item.LockedUntilUtc, nowUtc.Add(OperationLease)),
                cancellationToken);

        return affected == 1;
    }

    private Task<bool> TryAcquireLeaseAsync(
        Guid operationId,
        DateTime nowUtc,
        CancellationToken cancellationToken) =>
        TryAcquireLeaseCoreAsync(
            operationId,
            nowUtc,
            nowUtc.Add(OperationLease),
            cancellationToken);

    private async Task<bool> TryAcquireLeaseCoreAsync(
        Guid operationId,
        DateTime nowUtc,
        DateTime lockedUntilUtc,
        CancellationToken cancellationToken)
    {
        var progressJson = JsonSerializer.Serialize(
            new
            {
                phaseCode = "eligibility",
                isHeartbeat = false,
                updatedAtUtc = nowUtc
            },
            JsonOptions);

        var affected = await db.RuntimeOperations
            .Where(item =>
                item.Id == operationId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName &&
                (
                    item.Status == DomainCertificateRenewalOperation.QueuedStatus ||
                    item.Status == DomainCertificateRenewalOperation.FailedStatus ||
                    (
                        item.Status == DomainCertificateRenewalOperation.RunningStatus &&
                        (item.LockedUntilUtc == null || item.LockedUntilUtc <= nowUtc)
                    )
                ))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.Status,
                        DomainCertificateRenewalOperation.RunningStatus)
                    .SetProperty(
                        item => item.StartedAtUtc,
                        item => item.StartedAtUtc ?? nowUtc)
                    .SetProperty(
                        item => item.CompletedAtUtc,
                        (DateTime?)null)
                    .SetProperty(
                        item => item.CurrentStep,
                        "eligibility")
                    .SetProperty(
                        item => item.AttemptCount,
                        item => item.AttemptCount + 1)
                    .SetProperty(
                        item => item.LockedUntilUtc,
                        lockedUntilUtc)
                    .SetProperty(
                        item => item.LastError,
                        (string?)null)
                    .SetProperty(
                        item => item.EvidenceJson,
                        progressJson),
                cancellationToken);

        return affected == 1;
    }

    private Task UpdateProgressAsync(
        Guid operationId,
        string phaseCode,
        bool isHeartbeat,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var evidenceJson = JsonSerializer.Serialize(
            new
            {
                phaseCode,
                isHeartbeat,
                updatedAtUtc = nowUtc
            },
            JsonOptions);

        return db.RuntimeOperations
            .Where(item =>
                item.Id == operationId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName &&
                item.Status == DomainCertificateRenewalOperation.RunningStatus)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CurrentStep, phaseCode)
                    .SetProperty(item => item.LockedUntilUtc, nowUtc.Add(OperationLease))
                    .SetProperty(item => item.EvidenceJson, evidenceJson),
                cancellationToken);
    }

    private Task SetActivationProgressAsync(
        Guid operationId,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        return db.RuntimeOperations
            .Where(item =>
                item.Id == operationId &&
                item.Operation == DomainCertificateRenewalOperation.OperationName &&
                (item.Status == DomainCertificateRenewalOperation.RunningStatus ||
                 item.Status == DomainCertificateRenewalOperation.AwaitingActivationStatus))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CurrentStep, "activation")
                    .SetProperty(item => item.LockedUntilUtc, nowUtc.Add(OperationLease)),
                cancellationToken);
    }

    private Task MarkAwaitingActivationAsync(
        Guid operationId,
        DomainRenewalCandidateIssueResult candidate,
        bool retainLease,
        bool recoveringIncident,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var resultJson = JsonSerializer.Serialize(
            new
            {
                status = DomainCertificateRenewalOperation.AwaitingActivationStatus,
                candidateCertificateEntityId = candidate.CandidateCertificateEntityId,
                candidateCertificateId = candidate.CandidateCertificateId
            },
            JsonOptions);
        var evidenceJson = JsonSerializer.Serialize(
            new
            {
                phaseCode = "awaiting-activation",
                validation = "succeeded",
                sourceCertificateRemainsAuthoritative = true,
                recoveringIncident,
                updatedAtUtc = nowUtc
            },
            JsonOptions);

        return db.RuntimeOperations
            .Where(item => item.Id == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.Status,
                        DomainCertificateRenewalOperation.AwaitingActivationStatus)
                    .SetProperty(item => item.CurrentStep, "awaiting-activation")
                    .SetProperty(
                        item => item.LockedUntilUtc,
                        retainLease ? nowUtc.Add(OperationLease) : (DateTime?)null)
                    .SetProperty(item => item.CompletedAtUtc, (DateTime?)null)
                    .SetProperty(item => item.LastError, (string?)null)
                    .SetProperty(item => item.ResultJson, resultJson)
                    .SetProperty(item => item.EvidenceJson, evidenceJson),
                cancellationToken);
    }

    private async Task MarkFailedWithDiagnosticsAsync(
        Guid operationId,
        string safeErrorCode,
        DomainEntity domain,
        CertificateEntity source,
        DomainCertificateRenewalPolicyEntity? policy,
        CancellationToken cancellationToken)
    {
        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .Where(item => item.Id == operationId)
            .Select(item => new
            {
                item.CurrentStep,
                item.AttemptCount
            })
            .SingleOrDefaultAsync(cancellationToken);

        await MarkFailedAsync(
            operationId,
            safeErrorCode,
            cancellationToken);

        if (!ShouldCreateRenewalIncident(safeErrorCode))
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var sourceExpiry = source.ExpiresAtUtc;
        var remainingHours = sourceExpiry.HasValue
            ? Math.Floor((AsUtc(sourceExpiry.Value) - AsUtc(nowUtc)).TotalHours)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
            : null;

        await diagnostics.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: ResolveRenewalFailureSeverity(sourceExpiry, nowUtc),
                EventCode: "domains.certificate.renewal_failed",
                Source: nameof(DomainCertificateRenewalOrchestrator),
                Feature: "domains",
                Stage: "automatic-certificate-renewal",
                Message: "MEM could not complete automatic certificate renewal for this Domain.",
                IncidentId: BuildRenewalIncidentId(operationId),
                CreateIncident: true,
                OperationId: operationId,
                Resource: new MemDiagnosticResource(
                    Kind: "domain",
                    Id: domain.Id.ToString(),
                    DisplayName: domain.BaseDomain,
                    WorkspacePath: "/domains/renewal"),
                Expected: new Dictionary<string, string?>
                {
                    ["renewalResult"] = "verified ingress activation and active certificate transition"
                },
                Observed: new Dictionary<string, string?>
                {
                    ["renewalResult"] = "failed",
                    ["errorCode"] = safeErrorCode
                },
                Details: new Dictionary<string, string?>
                {
                    ["domainId"] = domain.Id.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["dnsProvider"] = domain.DnsProvider,
                    ["sourceCertificateId"] = source.CertificateId,
                    ["sourceExpiresAtUtc"] = sourceExpiry.HasValue
                        ? AsUtc(sourceExpiry.Value).ToString("O")
                        : null,
                    ["remainingHours"] = remainingHours,
                    ["operationStep"] = operation?.CurrentStep,
                    ["attemptCount"] = operation?.AttemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["renewalWindowDays"] = policy?.RenewalWindowDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["retryIntervalHours"] = policy?.RetryIntervalHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["errorCode"] = safeErrorCode
                },
                SuggestedAction: RenewalSuggestedAction(safeErrorCode),
                Retryable: true));
    }

    private async Task RecordRenewalRecoveredAsync(
        Guid operationId,
        DomainEntity domain,
        CertificateEntity source,
        DomainRenewalCandidateIssueResult candidate)
    {
        var writeResult = await diagnostics.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: "domains.certificate.renewal_recovered",
                Source: nameof(DomainCertificateRenewalOrchestrator),
                Feature: "domains",
                Stage: "automatic-certificate-renewal",
                Message: "Automatic certificate renewal recovered and produced a validated replacement candidate.",
                IncidentId: BuildRenewalIncidentId(operationId),
                CreateIncident: false,
                OperationId: operationId,
                Resource: new MemDiagnosticResource(
                    Kind: "domain",
                    Id: domain.Id.ToString(),
                    DisplayName: domain.BaseDomain,
                    WorkspacePath: "/domains/renewal"),
                Observed: new Dictionary<string, string?>
                {
                    ["renewalResult"] = DomainCertificateRenewalOperation.AwaitingActivationStatus,
                    ["sourceCertificateRemainsAuthoritative"] = "true"
                },
                Details: new Dictionary<string, string?>
                {
                    [MemDiagnosticIncidentLifecycle.StateDetailKey] =
                        MemDiagnosticIncidentLifecycle.ResolvedState,
                    [MemDiagnosticIncidentLifecycle.ResolutionCodeDetailKey] =
                        MemDiagnosticIncidentLifecycle.SelfRecoveredResolutionCode,
                    ["domainId"] = domain.Id.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["sourceCertificateId"] = source.CertificateId,
                    ["candidateCertificateId"] = candidate.CandidateCertificateId
                },
                Retryable: false));

        await TryResolveSelfRecoveredIncidentAsync(writeResult, operationId);
    }

    private async Task RecordRenewalActivationRecoveredAsync(
        Guid operationId,
        DomainEntity domain,
        CertificateEntity source,
        DomainRenewalCandidateIssueResult candidate)
    {
        var writeResult = await diagnostics.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: "domains.certificate.renewal_recovered",
                Source: nameof(DomainCertificateRenewalOrchestrator),
                Feature: "domains",
                Stage: "automatic-certificate-renewal",
                Message: "Automatic certificate renewal recovered and completed candidate activation.",
                IncidentId: BuildRenewalIncidentId(operationId),
                CreateIncident: false,
                OperationId: operationId,
                Resource: new MemDiagnosticResource(
                    Kind: "domain",
                    Id: domain.Id.ToString(),
                    DisplayName: domain.BaseDomain,
                    WorkspacePath: "/domains/renewal"),
                Observed: new Dictionary<string, string?>
                {
                    ["renewalResult"] = DomainCertificateRenewalOperation.SucceededStatus,
                    ["activePointerTransition"] = "succeeded"
                },
                Details: new Dictionary<string, string?>
                {
                    [MemDiagnosticIncidentLifecycle.StateDetailKey] =
                        MemDiagnosticIncidentLifecycle.ResolvedState,
                    [MemDiagnosticIncidentLifecycle.ResolutionCodeDetailKey] =
                        MemDiagnosticIncidentLifecycle.SelfRecoveredResolutionCode,
                    ["domainId"] = domain.Id.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["sourceCertificateId"] = source.CertificateId,
                    ["candidateCertificateId"] = candidate.CandidateCertificateId
                },
                Retryable: false));

        await TryResolveSelfRecoveredIncidentAsync(writeResult, operationId);
    }

    private async Task TryResolveSelfRecoveredIncidentAsync(
        MemDiagnosticWriteResult? writeResult,
        Guid operationId)
    {
        if (incidentLifecycle is null ||
            writeResult is null ||
            !writeResult.Stored ||
            string.IsNullOrWhiteSpace(writeResult.IncidentId) ||
            !writeResult.TimestampUtc.HasValue)
        {
            return;
        }

        var incidentId = writeResult.IncidentId!;
        var timestampUtc = writeResult.TimestampUtc.Value;

        try
        {
            // The recovery event is already durable. Complete lifecycle convergence even if
            // the original scan cancellation token is now stopping the host.
            await incidentLifecycle.ResolveSelfRecoveredAsync(
                incidentId,
                timestampUtc,
                writeResult.EventId,
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            // Renewal truth remains successful. Leaving the incident open is conservative and
            // the next operator/server lifecycle pass can still resolve it.
            logger.LogWarning(
                "Automatic certificate renewal recovered but Diagnostics lifecycle resolution failed. OperationId={OperationId} FailureType={FailureType}",
                operationId,
                exception.GetType().Name);
        }
    }

    private async Task<string?> ResolveImmediateReadinessBlockerAsync(
        DomainEntity domain,
        DomainCertificateRenewalPolicyEntity? policy,
        CertificateEntity? source,
        CancellationToken cancellationToken)
    {
        if (policy is null ||
            !policy.AutoRenewEnabled ||
            source is null ||
            domain.ActiveCertificateId != source.Id ||
            !source.IsActive ||
            source.IsStaging ||
            string.Equals(source.Status, "Deleted", StringComparison.OrdinalIgnoreCase) ||
            source.ExpiresAtUtc is null)
        {
            return null;
        }

        if (!string.Equals(
                domain.DnsProvider,
                "desec",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (NormalizeEmail(policy.AcmeEmail) is null)
        {
            return "AcmeEmailRequired";
        }

        var credentialConfigured = await db.DomainSecrets
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.DomainId == domain.Id &&
                    item.Category == DomainRenewalSecretNames.DnsProviderCategory &&
                    item.Key == DomainRenewalSecretNames.DesecProviderToken,
                cancellationToken);

        return credentialConfigured
            ? null
            : "RenewalCredentialRequired";
    }

    private async Task RecordRenewalReadinessFailureAsync(
        DomainEntity domain,
        CertificateEntity? source,
        DomainCertificateRenewalPolicyEntity? policy,
        string safeErrorCode,
        CancellationToken cancellationToken)
    {
        if (source?.ExpiresAtUtc is null)
        {
            return;
        }

        var incidentId = BuildRenewalReadinessIncidentId(domain.Id);
        var severity = ResolveRenewalFailureSeverity(
            source.ExpiresAtUtc,
            timeProvider.GetUtcNow().UtcDateTime);

        if (await LatestIncidentEventMatchesAsync(
                incidentId,
                RenewalReadinessFailureEventCode,
                severity,
                safeErrorCode,
                cancellationToken))
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var remainingHours = Math.Floor(
                (AsUtc(source.ExpiresAtUtc.Value) - AsUtc(nowUtc)).TotalHours)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        await diagnostics.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: severity,
                EventCode: RenewalReadinessFailureEventCode,
                Source: nameof(DomainCertificateRenewalOrchestrator),
                Feature: "domains",
                Stage: "automatic-renewal-readiness",
                Message: "Automatic certificate renewal is not ready for this Domain.",
                IncidentId: incidentId,
                CreateIncident: true,
                Resource: new MemDiagnosticResource(
                    Kind: "domain",
                    Id: domain.Id.ToString(),
                    DisplayName: domain.BaseDomain,
                    WorkspacePath: $"/domains/{domain.Id:D}/renewal"),
                Expected: new Dictionary<string, string?>
                {
                    ["renewalReadiness"] = "ready"
                },
                Observed: new Dictionary<string, string?>
                {
                    ["renewalReadiness"] = "blocked",
                    ["errorCode"] = safeErrorCode
                },
                Details: new Dictionary<string, string?>
                {
                    ["domainId"] = domain.Id.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["sourceCertificateId"] = source.CertificateId,
                    ["sourceExpiresAtUtc"] = AsUtc(source.ExpiresAtUtc.Value).ToString("O"),
                    ["remainingHours"] = remainingHours,
                    ["renewalWindowDays"] = policy?.RenewalWindowDays.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["retryIntervalHours"] = policy?.RetryIntervalHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["errorCode"] = safeErrorCode
                },
                SuggestedAction: RenewalSuggestedAction(safeErrorCode),
                Retryable: true));
    }

    private async Task RecordRenewalReadinessRecoveredIfNeededAsync(
        DomainEntity domain,
        CertificateEntity? source,
        CancellationToken cancellationToken)
    {
        if (diagnosticReader is null || incidentLifecycle is null)
        {
            return;
        }

        var incidentId = BuildRenewalReadinessIncidentId(domain.Id);
        var latest = await GetLatestIncidentEventAsync(incidentId, cancellationToken);
        if (latest is null ||
            !string.Equals(
                latest.EventCode,
                RenewalReadinessFailureEventCode,
                StringComparison.Ordinal))
        {
            return;
        }

        var writeResult = await diagnostics.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: MemDiagnosticSeverities.Information,
                EventCode: RenewalReadinessRecoveredEventCode,
                Source: nameof(DomainCertificateRenewalOrchestrator),
                Feature: "domains",
                Stage: "automatic-renewal-readiness",
                Message: "Automatic certificate renewal readiness recovered for this Domain.",
                IncidentId: incidentId,
                CreateIncident: false,
                Resource: new MemDiagnosticResource(
                    Kind: "domain",
                    Id: domain.Id.ToString(),
                    DisplayName: domain.BaseDomain,
                    WorkspacePath: $"/domains/{domain.Id:D}/renewal"),
                Observed: new Dictionary<string, string?>
                {
                    ["renewalReadiness"] = "ready"
                },
                Details: new Dictionary<string, string?>
                {
                    [MemDiagnosticIncidentLifecycle.StateDetailKey] =
                        MemDiagnosticIncidentLifecycle.ResolvedState,
                    [MemDiagnosticIncidentLifecycle.ResolutionCodeDetailKey] =
                        MemDiagnosticIncidentLifecycle.SelfRecoveredResolutionCode,
                    ["domainId"] = domain.Id.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["sourceCertificateId"] = source?.CertificateId
                },
                Retryable: false));

        await TryResolveSelfRecoveredIncidentAsync(writeResult, Guid.Empty);
    }

    private async Task<bool> LatestIncidentEventMatchesAsync(
        string incidentId,
        string eventCode,
        string severity,
        string safeErrorCode,
        CancellationToken cancellationToken)
    {
        if (diagnosticReader is null)
        {
            return false;
        }

        var latest = await GetLatestIncidentEventAsync(incidentId, cancellationToken);
        var latestErrorCode = latest?.Observed is not null &&
            latest.Observed.TryGetValue("errorCode", out var observedErrorCode)
                ? observedErrorCode
                : null;

        return latest is not null &&
            string.Equals(latest.EventCode, eventCode, StringComparison.Ordinal) &&
            string.Equals(latest.Severity, severity, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(latestErrorCode, safeErrorCode, StringComparison.Ordinal);
    }

    private async Task<MemDiagnosticEvent?> GetLatestIncidentEventAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        if (diagnosticReader is null)
        {
            return null;
        }

        try
        {
            var page = await diagnosticReader.QueryAsync(
                new MemDiagnosticQuery(
                    IncidentId: incidentId,
                    PageSize: 5),
                cancellationToken);
            return page.Events
                .Where(item => string.Equals(
                    item.IncidentId,
                    incidentId,
                    StringComparison.Ordinal))
                .OrderByDescending(item => item.TimestampUtc)
                .ThenByDescending(item => item.EventId, StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal const string RenewalReadinessFailureEventCode =
        "domains.certificate.renewal_readiness_failed";
    internal const string RenewalReadinessRecoveredEventCode =
        "domains.certificate.renewal_readiness_recovered";

    internal static string BuildRenewalReadinessIncidentId(Guid domainId) =>
        $"inc_domain_renewal_readiness_{domainId:N}";

    internal static string BuildRenewalIncidentId(Guid operationId) =>
        $"inc_domain_renewal_{operationId:N}";

    internal static bool ShouldCreateRenewalIncident(string? safeErrorCode) =>
        !string.IsNullOrWhiteSpace(safeErrorCode) &&
        !string.Equals(
            safeErrorCode,
            "RenewalCycleNoLongerEligible",
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            safeErrorCode,
            "SourceCertificateChanged",
            StringComparison.OrdinalIgnoreCase);

    internal static string ResolveRenewalFailureSeverity(
        DateTime? sourceExpiresAtUtc,
        DateTime nowUtc)
    {
        if (!sourceExpiresAtUtc.HasValue)
        {
            return MemDiagnosticSeverities.Error;
        }

        var expiry = AsUtc(sourceExpiresAtUtc.Value);
        var now = AsUtc(nowUtc);
        if (expiry <= now)
        {
            return MemDiagnosticSeverities.Critical;
        }

        if (expiry <= now.AddDays(7))
        {
            return MemDiagnosticSeverities.Critical;
        }

        return expiry <= now.AddDays(14)
            ? MemDiagnosticSeverities.Error
            : MemDiagnosticSeverities.Warning;
    }

    private static string RenewalSuggestedAction(string safeErrorCode) =>
        safeErrorCode is "RenewalCredentialUnavailable" or "RenewalCredentialRequired" or "AcmeEmailRequired"
            ? "Open Domains > Renewal, verify the deSEC renewal credential and ACME contact, then allow MEM to retry the server-owned renewal cycle."
            : "Open Diagnostics and Domains > Renewal, review the safe renewal evidence, correct the reported cause, then allow MEM to retry the server-owned renewal cycle.";

    private Task MarkFailedAsync(
        Guid operationId,
        string safeErrorCode,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var resultJson = JsonSerializer.Serialize(
            new
            {
                status = DomainCertificateRenewalOperation.FailedStatus,
                errorCode = safeErrorCode
            },
            JsonOptions);
        var evidenceJson = JsonSerializer.Serialize(
            new
            {
                phaseCode = "failed",
                errorCode = safeErrorCode,
                updatedAtUtc = nowUtc
            },
            JsonOptions);

        return db.RuntimeOperations
            .Where(item => item.Id == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.Status,
                        DomainCertificateRenewalOperation.FailedStatus)
                    .SetProperty(item => item.CurrentStep, "failed")
                    .SetProperty(item => item.CompletedAtUtc, nowUtc)
                    .SetProperty(item => item.LockedUntilUtc, (DateTime?)null)
                    .SetProperty(item => item.LastError, safeErrorCode)
                    .SetProperty(item => item.ResultJson, resultJson)
                    .SetProperty(item => item.EvidenceJson, evidenceJson),
                cancellationToken);
    }

    private Task MarkSupersededAsync(
        Guid operationId,
        string reason,
        CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var resultJson = JsonSerializer.Serialize(
            new
            {
                status = "superseded",
                reason
            },
            JsonOptions);

        return db.RuntimeOperations
            .Where(item => item.Id == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        item => item.Status,
                        DomainCertificateRenewalOperation.SucceededStatus)
                    .SetProperty(item => item.CurrentStep, "superseded")
                    .SetProperty(item => item.CompletedAtUtc, nowUtc)
                    .SetProperty(item => item.LockedUntilUtc, (DateTime?)null)
                    .SetProperty(item => item.LastError, (string?)null)
                    .SetProperty(item => item.ResultJson, resultJson),
                cancellationToken);
    }

    private Task MarkInterruptedAsync(Guid operationId)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var evidenceJson = JsonSerializer.Serialize(
            new
            {
                phaseCode = "interrupted",
                restartRecovery = "fresh-attempt-after-expired-lease",
                updatedAtUtc = nowUtc
            },
            JsonOptions);

        return db.RuntimeOperations
            .Where(item => item.Id == operationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(item => item.CurrentStep, "interrupted")
                    .SetProperty(item => item.LockedUntilUtc, nowUtc)
                    .SetProperty(item => item.EvidenceJson, evidenceJson),
                CancellationToken.None);
    }

    private static bool IsReadyLifecycle(
        DomainEntity domain,
        DomainCertificateRenewalPolicyEntity? policy,
        CertificateEntity? source)
    {
        if (policy is null || !policy.AutoRenewEnabled)
        {
            return false;
        }

        return source is not null &&
            domain.ActiveCertificateId == source.Id &&
            source.IsActive &&
            !source.IsStaging &&
            !string.Equals(source.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
            source.ExpiresAtUtc is not null;
    }

    private static bool IsEligibleLifecycle(
        DomainEntity domain,
        DomainCertificateRenewalPolicyEntity? policy,
        CertificateEntity? source,
        DateTime nowUtc) =>
        IsReadyLifecycle(domain, policy, source) &&
        IsDue(
            source!.ExpiresAtUtc!.Value,
            policy!.RenewalWindowDays,
            nowUtc);

    private static bool RetryBoundaryReached(
        RuntimeOperationEntity operation,
        DomainCertificateRenewalPolicyEntity policy,
        DateTime nowUtc)
    {
        if (operation.CompletedAtUtc is null)
        {
            return true;
        }

        var completed = AsUtc(operation.CompletedAtUtc.Value);
        return nowUtc >= completed.AddHours(Math.Max(1, policy.RetryIntervalHours));
    }

    private static bool OperationWasRecoveringIncident(string? evidenceJson)
    {
        if (string.IsNullOrWhiteSpace(evidenceJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return document.RootElement.TryGetProperty(
                    "recoveringIncident",
                    out var property) &&
                property.ValueKind is JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? NormalizeEmail(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Length > 320 ||
            !MailAddress.TryCreate(candidate, out var parsed) ||
            !string.Equals(parsed.Address, candidate, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parsed.Address;
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private enum ProcessOutcome
    {
        AwaitingActivation,
        Activated,
        Failed,
        Superseded
    }
}
