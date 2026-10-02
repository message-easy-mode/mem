using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Renewal;

public interface IDomainCertificateRenewalCandidateIssuer
{
    Task<DomainRenewalCandidateIssueResult?> TryRecoverCandidateAsync(
        DomainRenewalCandidateRequest request,
        CancellationToken cancellationToken);

    Task<DomainRenewalCandidateIssueResult> IssueCandidateAsync(
        DomainRenewalCandidateRequest request,
        string providerToken,
        CertificateIssueProgressCallback progressCallback,
        CancellationToken cancellationToken);
}
