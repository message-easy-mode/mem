using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates.Npm;
using Shared.Exceptions;

namespace Modules.Shared.Domains.Renewal;

public sealed class NpmDomainCertificateRenewalIngressActivator(
    NpmCertificateImportProbe npmCertificateImport,
    ILogger<NpmDomainCertificateRenewalIngressActivator> logger)
    : IDomainCertificateRenewalIngressActivator
{
    public async Task<DomainRenewalCandidateActivationResult> ActivateAsync(
        string certificateId,
        int expectedNpmCertificateId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await npmCertificateImport.ImportReplacementAsync(
                certificateId,
                expectedNpmCertificateId,
                cancellationToken);

            if (!result.Succeeded || result.MatchingCertificateId != expectedNpmCertificateId)
            {
                return new DomainRenewalCandidateActivationResult(
                    Succeeded: false,
                    Status: "NpmActivationFailed",
                    ErrorCode: result.ErrorCode ?? "NpmRenewalActivationFailed");
            }

            var consumerCount = result.Evidence
                .Where(item => string.Equals(
                    item.Key,
                    "npm.import.activeConsumers",
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => int.TryParse(item.Value, out var count) ? count : 0)
                .FirstOrDefault();

            return new DomainRenewalCandidateActivationResult(
                Succeeded: true,
                Status: "NpmActivated",
                ErrorCode: null,
                NpmCertificateId: expectedNpmCertificateId,
                ReappliedProxyHostCount: consumerCount);
        }
        catch (MemProblemException problem)
        {
            logger.LogWarning(
                "Automatic certificate renewal could not activate the replacement certificate in NPM. CertificateId={CertificateId} NpmCertificateId={NpmCertificateId} ProblemCode={ProblemCode}",
                certificateId,
                expectedNpmCertificateId,
                problem.Code);

            return new DomainRenewalCandidateActivationResult(
                Succeeded: false,
                Status: "NpmActivationFailed",
                ErrorCode: "NpmRenewalActivationFailed");
        }
        catch (Exception exception) when (exception is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                "Automatic certificate renewal could not activate the replacement certificate in NPM. CertificateId={CertificateId} NpmCertificateId={NpmCertificateId} FailureType={FailureType}",
                certificateId,
                expectedNpmCertificateId,
                exception.GetType().Name);

            return new DomainRenewalCandidateActivationResult(
                Succeeded: false,
                Status: "NpmActivationFailed",
                ErrorCode: "NpmRenewalActivationFailed");
        }
    }
}
