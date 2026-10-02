using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Renewal;

public sealed class DomainCertificateRenewalCandidateIssuer(
    MemDbContext db,
    CertificateService certificateService,
    CertificateStorageService certificateStorage,
    CertificateValidationService validationService,
    DomainRegistryService registry,
    ILogger<DomainCertificateRenewalCandidateIssuer> logger)
    : IDomainCertificateRenewalCandidateIssuer
{
    public async Task<DomainRenewalCandidateIssueResult?> TryRecoverCandidateAsync(
        DomainRenewalCandidateRequest request,
        CancellationToken cancellationToken)
    {
        // Never let storage reconciliation from a stale renewal cycle move Domain
        // ownership or the main-platform role. Verify the source before touching
        // registry state, then verify it again after targeted reconciliation.
        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return SourceCertificateChanged();
        }

        // A previous Control Plane process may have stored the certificate before it
        // could persist the operation result. Reconcile only this operation's
        // deterministic candidate rather than running a global storage sync, because
        // 01B must not change the active certificate pointer as a side effect.
        var candidate = await FindCandidateAsync(request, cancellationToken);
        candidate ??= await ReconcileStoredCandidateAsync(request, cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        var validation = await validationService.ValidateAsync(
            candidate.CertificateId,
            cancellationToken);
        if (!validation.Succeeded)
        {
            logger.LogWarning(
                "Automatic renewal found an existing candidate for operation {OperationId}, but validation did not succeed. DomainId={DomainId} CandidateCertificateId={CandidateCertificateId} FailureClass={FailureClass}",
                request.OperationId,
                request.DomainId,
                candidate.CertificateId,
                validation.ErrorCode ?? "CertificateValidationFailed");
            return null;
        }

        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return new DomainRenewalCandidateIssueResult(
                Succeeded: false,
                Status: "SourceCertificateChanged",
                ErrorCode: "SourceCertificateChanged",
                CandidateCertificateEntityId: candidate.Id,
                CandidateCertificateId: candidate.CertificateId);
        }

        return new DomainRenewalCandidateIssueResult(
            Succeeded: true,
            Status: "Recovered",
            ErrorCode: null,
            CandidateCertificateEntityId: candidate.Id,
            CandidateCertificateId: candidate.CertificateId);
    }

    public async Task<DomainRenewalCandidateIssueResult> IssueCandidateAsync(
        DomainRenewalCandidateRequest request,
        string providerToken,
        CertificateIssueProgressCallback progressCallback,
        CancellationToken cancellationToken)
    {
        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return SourceCertificateChanged();
        }

        var result = await certificateService.IssueAsync(
            new CertificateIssueRequest(
                Domain: request.CommonName,
                Zone: request.Zone,
                IsWildcard: request.IsWildcard,
                Email: request.AcmeEmail,
                Provider: request.DnsProvider,
                ProviderToken: providerToken,
                UseStaging: false,
                StorageName: $"renewal-{request.OperationId:N}"),
            cancellationToken,
            progressCallback);

        if (!result.Succeeded)
        {
            return new DomainRenewalCandidateIssueResult(
                Succeeded: false,
                Status: "Failed",
                ErrorCode: result.ErrorCode ?? "CertificateIssueFailed",
                CandidateCertificateEntityId: null,
                CandidateCertificateId: null);
        }

        // The PEM now exists on disk. Before importing its metadata into the Domain
        // registry, re-check that this renewal cycle still owns the source.
        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return SourceCertificateChanged();
        }

        var issuedCertificateId = result.Evidence
            .FirstOrDefault(item =>
                string.Equals(item.Key, "certificateId", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        CertificateEntity? candidate = null;
        if (!string.IsNullOrWhiteSpace(issuedCertificateId))
        {
            candidate = await RegisterStoredCandidateAsync(
                request,
                issuedCertificateId,
                cancellationToken);
        }

        candidate ??= await ReconcileStoredCandidateAsync(request, cancellationToken);
        if (candidate is null)
        {
            return new DomainRenewalCandidateIssueResult(
                Succeeded: false,
                Status: "CandidateRegistrationFailed",
                ErrorCode: "RenewalCandidateRegistrationFailed",
                CandidateCertificateEntityId: null,
                CandidateCertificateId: null);
        }

        var validation = await validationService.ValidateAsync(
            candidate.CertificateId,
            cancellationToken);
        if (!validation.Succeeded)
        {
            return new DomainRenewalCandidateIssueResult(
                Succeeded: false,
                Status: "CandidateValidationFailed",
                ErrorCode: validation.ErrorCode ?? "RenewalCandidateValidationFailed",
                CandidateCertificateEntityId: candidate.Id,
                CandidateCertificateId: candidate.CertificateId);
        }

        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return new DomainRenewalCandidateIssueResult(
                Succeeded: false,
                Status: "SourceCertificateChanged",
                ErrorCode: "SourceCertificateChanged",
                CandidateCertificateEntityId: candidate.Id,
                CandidateCertificateId: candidate.CertificateId);
        }

        return new DomainRenewalCandidateIssueResult(
            Succeeded: true,
            Status: "Issued",
            ErrorCode: null,
            CandidateCertificateEntityId: candidate.Id,
            CandidateCertificateId: candidate.CertificateId);
    }

    private async Task<CertificateEntity?> ReconcileStoredCandidateAsync(
        DomainRenewalCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var prefix = CandidatePrefix(request.OperationId);
        var metadata = (await certificateStorage.ListAsync(cancellationToken))
            .Where(item =>
                item.CertificateId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                !item.IsStaging &&
                string.Equals(
                    NormalizeDnsName(item.Zone),
                    NormalizeDnsName(request.Zone),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    NormalizeDnsName(item.Domain),
                    NormalizeDnsName(request.CommonName),
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefault();

        if (metadata is null)
        {
            return null;
        }

        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return null;
        }

        await registry.RegisterCertificateAsync(metadata, cancellationToken);
        return await FindCandidateAsync(request, cancellationToken);
    }

    private async Task<CertificateEntity?> RegisterStoredCandidateAsync(
        DomainRenewalCandidateRequest request,
        string certificateId,
        CancellationToken cancellationToken)
    {
        var metadata = await certificateStorage.GetMetadataAsync(
            certificateId,
            cancellationToken);
        if (metadata is null ||
            metadata.IsStaging ||
            !certificateId.StartsWith(
                CandidatePrefix(request.OperationId),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                NormalizeDnsName(metadata.Zone),
                NormalizeDnsName(request.Zone),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                NormalizeDnsName(metadata.Domain),
                NormalizeDnsName(request.CommonName),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!await SourceCertificateIsStillAuthoritativeAsync(request, cancellationToken))
        {
            return null;
        }

        await registry.RegisterCertificateAsync(metadata, cancellationToken);
        return await FindCandidateAsync(request, cancellationToken);
    }

    private Task<CertificateEntity?> FindCandidateAsync(
        DomainRenewalCandidateRequest request,
        CancellationToken cancellationToken)
    {
        var prefix = CandidatePrefix(request.OperationId);

        return db.Certificates
            .AsNoTracking()
            .Where(item =>
                item.DomainId == request.DomainId &&
                item.Id != request.SourceCertificateEntityId &&
                item.CertificateId.StartsWith(prefix) &&
                !item.IsStaging &&
                item.Status != "Deleted")
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private Task<bool> SourceCertificateIsStillAuthoritativeAsync(
        DomainRenewalCandidateRequest request,
        CancellationToken cancellationToken) =>
        db.Domains
            .AsNoTracking()
            .AnyAsync(
                domain =>
                    domain.Id == request.DomainId &&
                    domain.ActiveCertificateId == request.SourceCertificateEntityId &&
                    domain.ActiveCertificate != null &&
                    domain.IsMainPlatformDomain == domain.ActiveCertificate.IsMainPlatformCertificate,
                cancellationToken);

    private static string CandidatePrefix(Guid operationId) =>
        $"cert_renewal-{operationId:N}_";

    private static string NormalizeDnsName(string value) =>
        value.Trim().TrimEnd('.').ToLowerInvariant();

    private static DomainRenewalCandidateIssueResult SourceCertificateChanged() =>
        new(
            Succeeded: false,
            Status: "SourceCertificateChanged",
            ErrorCode: "SourceCertificateChanged",
            CandidateCertificateEntityId: null,
            CandidateCertificateId: null);
}
