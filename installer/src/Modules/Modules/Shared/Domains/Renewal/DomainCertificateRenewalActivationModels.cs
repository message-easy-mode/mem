namespace Modules.Shared.Domains.Renewal;

public sealed record DomainRenewalCandidateActivationRequest(
    Guid OperationId,
    Guid DomainId,
    Guid SourceCertificateEntityId,
    string SourceCertificateId,
    Guid CandidateCertificateEntityId,
    string CandidateCertificateId);

public sealed record DomainRenewalCandidateActivationResult(
    bool Succeeded,
    string Status,
    string? ErrorCode,
    int? NpmCertificateId = null,
    int ReappliedProxyHostCount = 0);

public interface IDomainCertificateRenewalCandidateActivator
{
    Task<DomainRenewalCandidateActivationResult> ActivateAsync(
        DomainRenewalCandidateActivationRequest request,
        CancellationToken cancellationToken);
}

public interface IDomainCertificateRenewalCandidateValidator
{
    Task<DomainRenewalCandidateActivationResult> ValidateAsync(
        string certificateId,
        CancellationToken cancellationToken);
}

public interface IDomainCertificateRenewalIngressActivator
{
    Task<DomainRenewalCandidateActivationResult> ActivateAsync(
        string certificateId,
        int expectedNpmCertificateId,
        CancellationToken cancellationToken);
}
