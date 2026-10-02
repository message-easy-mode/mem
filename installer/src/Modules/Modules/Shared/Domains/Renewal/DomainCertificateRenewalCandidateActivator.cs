using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Renewal;

public sealed class DomainCertificateRenewalCandidateActivator(
    MemDbContext db,
    IDomainCertificateRenewalCandidateValidator candidateValidator,
    IDomainCertificateRenewalIngressActivator ingressActivator,
    CertificateStorageService certificateStorage,
    TimeProvider timeProvider,
    ILogger<DomainCertificateRenewalCandidateActivator> logger)
    : IDomainCertificateRenewalCandidateActivator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<DomainRenewalCandidateActivationResult> ActivateAsync(
        DomainRenewalCandidateActivationRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadSnapshotAsync(request, cancellationToken);
        if (snapshot is null)
        {
            return Failed("RenewalActivationStateMissing");
        }

        var (domain, source, candidate, operation) = snapshot.Value;

        if (domain.ActiveCertificateId == candidate.Id &&
            string.Equals(
                operation.Status,
                DomainCertificateRenewalOperation.SucceededStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            return new DomainRenewalCandidateActivationResult(
                Succeeded: true,
                Status: "AlreadyActivated",
                ErrorCode: null,
                NpmCertificateId: candidate.NpmCertificateId);
        }

        var contractError = ValidateActivationContract(
            domain,
            source,
            candidate,
            operation,
            request,
            timeProvider.GetUtcNow().UtcDateTime);
        if (contractError is not null)
        {
            return Failed(contractError);
        }

        var validation = await candidateValidator.ValidateAsync(
            candidate.CertificateId,
            cancellationToken);
        if (!validation.Succeeded)
        {
            return validation;
        }

        var npmCertificateId = source.NpmCertificateId;
        var npmInUse = source.ImportedToNpm || npmCertificateId.HasValue;
        var reappliedProxyHostCount = 0;

        if (npmInUse)
        {
            if (npmCertificateId is not > 0)
            {
                return Failed("NpmSourceCertificateLinkInvalid");
            }

            var ingressResult = await ingressActivator.ActivateAsync(
                candidate.CertificateId,
                npmCertificateId.Value,
                cancellationToken);
            if (!ingressResult.Succeeded)
            {
                return ingressResult;
            }

            if (ingressResult.NpmCertificateId != npmCertificateId.Value)
            {
                return Failed("NpmCertificateIdentityChanged");
            }

            reappliedProxyHostCount = ingressResult.ReappliedProxyHostCount;
        }

        // External NPM mutation, when required, has completed. Reload authoritative
        // database state before crossing the MEM pointer boundary in case an operator
        // changed the Domain while the ingress activation was in flight.
        db.ChangeTracker.Clear();
        var reloaded = await LoadTrackedStateAsync(request, cancellationToken);
        if (reloaded is null)
        {
            return Failed("RenewalActivationStateMissing");
        }

        var currentDomain = reloaded.Value.Domain;
        var currentSource = reloaded.Value.Source;
        var currentCandidate = reloaded.Value.Candidate;
        var currentOperation = reloaded.Value.Operation;

        if (currentDomain.ActiveCertificateId == currentCandidate.Id &&
            string.Equals(
                currentOperation.Status,
                DomainCertificateRenewalOperation.SucceededStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            return new DomainRenewalCandidateActivationResult(
                Succeeded: true,
                Status: "AlreadyActivated",
                ErrorCode: null,
                NpmCertificateId: currentCandidate.NpmCertificateId,
                ReappliedProxyHostCount: reappliedProxyHostCount);
        }

        contractError = ValidateActivationContract(
            currentDomain,
            currentSource,
            currentCandidate,
            currentOperation,
            request,
            timeProvider.GetUtcNow().UtcDateTime);
        if (contractError is not null)
        {
            // NPM is an external mutation boundary. Once replacement PEM has been
            // uploaded and its consumers verified, do not silently classify later
            // MEM state drift as a harmless supersession: public ingress may now be
            // using the candidate while another actor changed Domain state. Keep the
            // current MEM pointer untouched and surface an incident-worthy failure.
            return npmInUse
                ? Failed("NpmActivationStateChangedAfterIngress")
                : Failed(contractError);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        currentSource.IsActive = false;
        if (npmInUse)
        {
            // The stable NPM certificate identity now contains the candidate PEM. Transfer
            // the live NPM linkage in the same database transaction as the Domain pointer
            // so MEM never durably claims that two certificate records own the same live
            // NPM identity during the normal successful path.
            currentSource.ImportedToNpm = false;
            currentSource.NpmCertificateId = null;
            currentCandidate.ImportedToNpm = true;
            currentCandidate.NpmCertificateId = npmCertificateId;
            currentCandidate.LastImportedToNpmAtUtc = nowUtc;
            currentCandidate.LastError = null;
        }

        currentCandidate.IsActive = true;
        currentDomain.ActiveCertificateId = currentCandidate.Id;
        currentDomain.Status = "Active";
        currentDomain.UpdatedAtUtc = nowUtc;

        if (currentDomain.IsMainPlatformDomain)
        {
            currentSource.IsMainPlatformCertificate = false;
            currentCandidate.IsMainPlatformCertificate = true;
        }
        else
        {
            currentCandidate.IsMainPlatformCertificate = false;
        }

        currentOperation.Status = DomainCertificateRenewalOperation.SucceededStatus;
        currentOperation.CurrentStep = "activation-complete";
        currentOperation.CompletedAtUtc = nowUtc;
        currentOperation.LockedUntilUtc = null;
        currentOperation.LastError = null;
        currentOperation.ResultJson = JsonSerializer.Serialize(
            new
            {
                status = DomainCertificateRenewalOperation.SucceededStatus,
                sourceCertificateEntityId = currentSource.Id,
                sourceCertificateId = currentSource.CertificateId,
                candidateCertificateEntityId = currentCandidate.Id,
                candidateCertificateId = currentCandidate.CertificateId,
                npmCertificateId = npmInUse ? npmCertificateId : null,
                reappliedProxyHostCount,
                mainPlatformRoleTransferred = currentDomain.IsMainPlatformDomain
            },
            JsonOptions);
        currentOperation.EvidenceJson = JsonSerializer.Serialize(
            new
            {
                phaseCode = "activation-complete",
                candidateValidation = "succeeded",
                ingressActivation = npmInUse ? "verified" : "not-required",
                activePointerTransition = "succeeded",
                previousCertificateRetained = true,
                mainPlatformContinuity = currentDomain.IsMainPlatformDomain
                    ? "preserved"
                    : "not-applicable",
                updatedAtUtc = nowUtc
            },
            JsonOptions);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await PersistStorageProjectionBestEffortAsync(
            currentDomain,
            currentSource,
            currentCandidate,
            cancellationToken);

        logger.LogInformation(
            "Automatic certificate renewal activated replacement certificate. OperationId={OperationId} DomainId={DomainId} SourceCertificateId={SourceCertificateId} CandidateCertificateId={CandidateCertificateId} NpmCertificateId={NpmCertificateId} ReappliedProxyHostCount={ReappliedProxyHostCount} MainPlatformDomain={MainPlatformDomain}",
            request.OperationId,
            request.DomainId,
            currentSource.CertificateId,
            currentCandidate.CertificateId,
            npmInUse ? npmCertificateId : null,
            reappliedProxyHostCount,
            currentDomain.IsMainPlatformDomain);

        return new DomainRenewalCandidateActivationResult(
            Succeeded: true,
            Status: "Activated",
            ErrorCode: null,
            NpmCertificateId: npmInUse ? npmCertificateId : null,
            ReappliedProxyHostCount: reappliedProxyHostCount);
    }

    internal static string? ValidateActivationContract(
        DomainEntity domain,
        CertificateEntity source,
        CertificateEntity candidate,
        RuntimeOperationEntity operation,
        DomainRenewalCandidateActivationRequest request,
        DateTime nowUtc)
    {
        if (operation.Id != request.OperationId ||
            operation.DomainId != request.DomainId ||
            !string.Equals(
                operation.Operation,
                DomainCertificateRenewalOperation.OperationName,
                StringComparison.Ordinal))
        {
            return "RenewalOperationOwnershipMismatch";
        }

        if (!string.Equals(
                operation.Status,
                DomainCertificateRenewalOperation.AwaitingActivationStatus,
                StringComparison.OrdinalIgnoreCase))
        {
            return "RenewalOperationNotAwaitingActivation";
        }

        if (domain.Id != request.DomainId ||
            source.Id != request.SourceCertificateEntityId ||
            candidate.Id != request.CandidateCertificateEntityId ||
            source.DomainId != domain.Id ||
            candidate.DomainId != domain.Id ||
            !string.Equals(source.CertificateId, request.SourceCertificateId, StringComparison.Ordinal) ||
            !string.Equals(candidate.CertificateId, request.CandidateCertificateId, StringComparison.Ordinal))
        {
            return "RenewalCandidateOwnershipMismatch";
        }

        var expectedCandidatePrefix = $"cert_renewal-{operation.Id:N}_";
        if (!candidate.CertificateId.StartsWith(
                expectedCandidatePrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return "RenewalCandidateCycleMismatch";
        }

        if (domain.ActiveCertificateId != source.Id)
        {
            return "SourceCertificateChanged";
        }

        if (domain.IsMainPlatformDomain != source.IsMainPlatformCertificate)
        {
            return "MainPlatformCertificateRoleInconsistent";
        }

        if (candidate.IsMainPlatformCertificate)
        {
            return "RenewalCandidateRoleInvalid";
        }

        if (candidate.IsStaging)
        {
            return "RenewalCandidateStagingRejected";
        }

        if (!candidate.IsActive ||
            string.Equals(candidate.Status, "Deleted", StringComparison.OrdinalIgnoreCase) ||
            candidate.ExpiresAtUtc is null ||
            AsUtc(candidate.ExpiresAtUtc.Value) <= AsUtc(nowUtc))
        {
            return "RenewalCandidateInvalid";
        }

        var validStatus =
            string.Equals(candidate.Status, "Valid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.Status, "Active", StringComparison.OrdinalIgnoreCase);
        if (!validStatus)
        {
            return "RenewalCandidateInvalid";
        }

        if (!string.Equals(
                NormalizeDnsName(candidate.CommonName),
                NormalizeDnsName(source.CommonName),
                StringComparison.OrdinalIgnoreCase) ||
            candidate.IsWildcard != source.IsWildcard)
        {
            return "RenewalCandidateDomainMismatch";
        }

        return null;
    }

    private async Task<(DomainEntity Domain, CertificateEntity Source, CertificateEntity Candidate, RuntimeOperationEntity Operation)?>
        LoadSnapshotAsync(
            DomainRenewalCandidateActivationRequest request,
            CancellationToken cancellationToken)
    {
        var domain = await db.Domains
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.DomainId, cancellationToken);
        var source = await db.Certificates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.SourceCertificateEntityId, cancellationToken);
        var candidate = await db.Certificates
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.CandidateCertificateEntityId, cancellationToken);
        var operation = await db.RuntimeOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.OperationId, cancellationToken);

        return domain is null || source is null || candidate is null || operation is null
            ? null
            : (domain, source, candidate, operation);
    }

    private async Task<(DomainEntity Domain, CertificateEntity Source, CertificateEntity Candidate, RuntimeOperationEntity Operation)?>
        LoadTrackedStateAsync(
            DomainRenewalCandidateActivationRequest request,
            CancellationToken cancellationToken)
    {
        var domain = await db.Domains
            .SingleOrDefaultAsync(item => item.Id == request.DomainId, cancellationToken);
        var source = await db.Certificates
            .SingleOrDefaultAsync(item => item.Id == request.SourceCertificateEntityId, cancellationToken);
        var candidate = await db.Certificates
            .SingleOrDefaultAsync(item => item.Id == request.CandidateCertificateEntityId, cancellationToken);
        var operation = await db.RuntimeOperations
            .SingleOrDefaultAsync(item => item.Id == request.OperationId, cancellationToken);

        return domain is null || source is null || candidate is null || operation is null
            ? null
            : (domain, source, candidate, operation);
    }

    private async Task PersistStorageProjectionBestEffortAsync(
        DomainEntity domain,
        CertificateEntity source,
        CertificateEntity candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            var sourceMetadata = await certificateStorage.GetMetadataAsync(
                source.CertificateId,
                cancellationToken);
            if (sourceMetadata is not null)
            {
                await certificateStorage.SaveMetadataAsync(
                    sourceMetadata with
                    {
                        IsMainPlatformCertificate = false,
                        IsInUse = false
                    },
                    cancellationToken);
            }

            var candidateMetadata = await certificateStorage.GetMetadataAsync(
                candidate.CertificateId,
                cancellationToken);
            if (candidateMetadata is not null)
            {
                await certificateStorage.SaveMetadataAsync(
                    candidateMetadata with
                    {
                        IsMainPlatformCertificate = domain.IsMainPlatformDomain,
                        IsInUse = true,
                        Purpose = domain.IsMainPlatformDomain
                            ? "platform-main"
                            : candidateMetadata.Purpose
                    },
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            // The database transition is authoritative and was committed atomically with the
            // operation. A stale metadata.json projection must not turn a successful public
            // ingress activation into a false renewal failure.
            logger.LogWarning(
                "Automatic certificate renewal activated successfully but certificate storage metadata projection could not be refreshed. DomainId={DomainId} CandidateCertificateId={CandidateCertificateId} FailureType={FailureType}",
                domain.Id,
                candidate.CertificateId,
                exception.GetType().Name);
        }
    }

    private static DomainRenewalCandidateActivationResult Failed(string errorCode) =>
        new(
            Succeeded: false,
            Status: "ActivationFailed",
            ErrorCode: errorCode);

    private static string NormalizeDnsName(string value) =>
        value.Trim().TrimEnd('.').ToLowerInvariant();

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
}
