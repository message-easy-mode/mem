using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Issuance;

public interface IDomainCertificateIssueExecutor
{
    Task<CertificateOperationResult> ExecuteAsync(
        DomainCertificateIssueExecutionRequest request,
        CertificateIssueProgressCallback progressCallback,
        CancellationToken cancellationToken);
}
