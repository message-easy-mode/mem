namespace Modules.Operator.Domains;

public sealed record OperatorDomainSummaryResponse(
    Guid Id,
    string BaseDomain,
    string DisplayName,
    string Purpose,
    bool IsMainPlatformDomain,
    string DnsProvider,
    string? DnsZone,
    string Status,
    Guid? ActiveCertificateEntityId,
    string? ActiveCertificateId,
    string? ActiveCertificateCommonName,
    bool? ActiveCertificateIsStaging,
    DateTime? ActiveCertificateExpiresAtUtc,
    int CertificateCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record OperatorDomainDetailResponse(
    Guid Id,
    string BaseDomain,
    string DisplayName,
    string Purpose,
    bool IsMainPlatformDomain,
    string DnsProvider,
    string? DnsZone,
    string Status,
    string? Notes,
    Guid? ActiveCertificateEntityId,
    IReadOnlyList<OperatorCertificateSummaryResponse> Certificates,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record OperatorCertificateSummaryResponse(
    Guid Id,
    Guid DomainId,
    string CertificateId,
    string CommonName,
    string Provider,
    bool IsWildcard,
    bool IsStaging,
    bool IsMainPlatformCertificate,
    bool IsActive,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    string? Thumbprint,
    int? NpmCertificateId,
    bool ImportedToNpm,
    DateTime? LastValidatedAtUtc,
    DateTime? LastImportedToNpmAtUtc,
    string? LastError);

public sealed record OperatorCertificateReadResponse(
    Guid Id,
    Guid DomainId,
    string DomainBaseDomain,
    string DomainDisplayName,
    bool DomainIsMainPlatformDomain,
    Guid? DomainActiveCertificateEntityId,
    string DomainDnsProvider,
    string? DomainDnsZone,
    string CertificateId,
    string CommonName,
    string Domain,
    string Zone,
    string Provider,
    bool IsWildcard,
    bool IsStaging,
    bool IsMainPlatformCertificate,
    bool IsActive,
    bool IsInUse,
    string? Purpose,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    string? Thumbprint,
    int? NpmCertificateId,
    bool ImportedToNpm,
    DateTime? LastValidatedAtUtc,
    DateTime? LastImportedToNpmAtUtc,
    string? LastError);

public sealed record CreateOperatorDomainRequest(
    string BaseDomain,
    string? DisplayName,
    string? Purpose,
    string DnsProvider,
    string? DnsZone,
    string? Notes);

public sealed record UpdateOperatorDomainRequest(
    string? DisplayName,
    string? Purpose,
    string? Status,
    string? Notes);

public sealed record DeleteOperatorDomainResponse(
    bool Succeeded,
    string Status,
    string Message,
    Guid DomainId,
    IReadOnlyList<string> Warnings);
public sealed record OperatorDomainRenewalResponse(
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
    DateTime? PolicyUpdatedAtUtc,
    string OperationalStatus = "unready",
    bool CertificateExpired = false,
    int? DaysRemaining = null,
    DateTime? NextEligibleRenewalAtUtc = null,
    DateTime? NextAutomaticAttemptAtUtc = null,
    DateTime? LastAttemptAtUtc = null,
    DateTime? LastSuccessfulRenewalAtUtc = null,
    Guid? LatestOperationId = null,
    string? LatestOperationStatus = null,
    string? LatestOperationStep = null,
    int LatestOperationAttemptCount = 0,
    string? LatestRequestedBy = null,
    string? LatestErrorCode = null,
    string? DiagnosticsIncidentId = null,
    string? DiagnosticsHref = null,
    bool ManualRenewAvailable = false);

public sealed record OperatorDomainRenewalHistoryResponse(
    Guid OperationId,
    string Status,
    string? Step,
    string RequestedBy,
    DateTime RequestedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int AttemptCount,
    string? ErrorCode,
    string? DiagnosticsIncidentId,
    string? DiagnosticsHref);

public sealed record OperatorDomainRenewalRunResponse(
    bool Accepted,
    string Status,
    string Message,
    Guid? OperationId,
    OperatorDomainRenewalResponse? Renewal);

public sealed record UpdateOperatorDomainRenewalPolicyRequest(
    bool AutoRenewEnabled,
    string? AcmeEmail);

public sealed record UpdateOperatorDomainRenewalCredentialRequest(
    string ProviderToken,
    string? AcmeEmail);

public sealed record OperatorDomainRenewalCredentialMutationResponse(
    bool Succeeded,
    string Status,
    string Message,
    OperatorDomainRenewalResponse? Renewal);
