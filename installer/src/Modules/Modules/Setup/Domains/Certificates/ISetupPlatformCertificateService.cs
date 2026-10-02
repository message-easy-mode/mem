using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;

namespace Modules.Setup.Domains.Certificates;

public interface ISetupPlatformCertificateService
{
    Task<CertificateOperationResult> IssuePlatformCertificateAsync(
        CertificateIssueRequest request,
        CancellationToken cancellationToken,
        CertificateIssueProgressCallback? progressCallback = null);

    Task<NpmCertificateProbeResult> EnsureNpmImportAsync(
        string certificateId,
        CancellationToken cancellationToken);
}
