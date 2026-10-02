using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Renewal;

public sealed class DomainCertificateRenewalCandidateValidator(
    CertificateValidationService validationService)
    : IDomainCertificateRenewalCandidateValidator
{
    public async Task<DomainRenewalCandidateActivationResult> ValidateAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        var validation = await validationService.ValidateAsync(
            certificateId,
            cancellationToken);

        return validation.Succeeded
            ? new DomainRenewalCandidateActivationResult(
                Succeeded: true,
                Status: "Validated",
                ErrorCode: null)
            : new DomainRenewalCandidateActivationResult(
                Succeeded: false,
                Status: "CandidateValidationFailed",
                ErrorCode: validation.ErrorCode ?? "RenewalCandidateValidationFailed");
    }
}
