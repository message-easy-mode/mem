using Modules.Shared.Domains.Certificates;

namespace Modules.Setup.Domains.Planning;

public sealed record SetupDomainPlanRequest(
    string BaseDomain,
    string AcmeEmail,
    string DnsProvider,
    string ProviderToken,
    bool UseStaging);

public sealed record SetupDomainPlanResponse(
    Guid? InstallationId,
    bool Configured,
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorCode,
    string? ErrorDetail,
    string Domain,
    string Zone,
    string AcmeEmail,
    string DnsProvider,
    bool UseStaging,
    bool ProviderAccessConfirmed,
    bool ProviderCredentialStored,
    DateTime? ValidatedAtUtc,
    IReadOnlyList<CertificateOperationEvidence> Evidence);
