namespace Modules.Shared.Domains.Renewal;

public static class DomainRenewalReadinessStatuses
{
    public const string Ready = "Ready";
    public const string Disabled = "Disabled";
    public const string RenewalCredentialRequired = "RenewalCredentialRequired";
    public const string AcmeEmailRequired = "AcmeEmailRequired";
    public const string ProductionCertificateRequired = "ProductionCertificateRequired";
    public const string UnsupportedProvider = "UnsupportedProvider";
}

public sealed record DomainCertificateRenewalState(
    Guid DomainId,
    string BaseDomain,
    string DnsProvider,
    string? DnsZone,
    bool PolicyConfigured,
    bool AutoRenewEnabled,
    string? AcmeEmail,
    int RenewalWindowDays,
    int RetryIntervalHours,
    bool CredentialConfigured,
    DateTime? CredentialUpdatedAtUtc,
    bool HasActiveProductionCertificate,
    string? ActiveCertificateId,
    DateTime? ActiveCertificateExpiresAtUtc,
    string ReadinessStatus,
    string ReadinessMessage,
    DateTime? PolicyUpdatedAtUtc);

public sealed record DomainRenewalCredentialMutationResult(
    bool Succeeded,
    string Status,
    string Message,
    DomainCertificateRenewalState? State);
