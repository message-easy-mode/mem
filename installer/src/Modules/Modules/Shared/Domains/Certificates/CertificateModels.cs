namespace Modules.Shared.Domains.Certificates;

public sealed record CertificateIssueRequest(
    string Domain,
    string Zone,
    bool IsWildcard,
    string Email,
    string Provider,
    string ProviderToken,
    bool UseStaging,
    string StorageName);


public enum CertificateIssueProgressTransition
{
    None = 0,
    Recovering = 1,
    Recovered = 2
}

public sealed record CertificateIssueProgress(
    string PhaseCode,
    string SafeSummary,
    bool IsHeartbeat = false,
    CertificateIssueProgressTransition Transition = CertificateIssueProgressTransition.None);

public delegate Task CertificateIssueProgressCallback(
    CertificateIssueProgress progress,
    CancellationToken cancellationToken);

public sealed record CertificateOperationResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorCode,
    string? ErrorDetail,
    IReadOnlyList<CertificateOperationEvidence> Evidence);

public sealed record CertificateOperationEvidence(
    string Key,
    string Value,
    bool Sensitive = false,
    string? Status = null);

public sealed record StoredCertificateMetadata
{
    public string CertificateId { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Zone { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public bool IsWildcard { get; init; }
    public bool IsStaging { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public string FullchainPath { get; init; } = string.Empty;
    public string PrivateKeyPath { get; init; } = string.Empty;
    public string? Thumbprint { get; init; }
    public string Status { get; init; } = string.Empty;

    // Operator/domain-management metadata. These default safely when reading older metadata.json files.
    public bool IsMainPlatformCertificate { get; init; }
    public bool IsInUse { get; init; }
    public string? Purpose { get; init; }
}

public sealed record DomainActiveCertificateSelectionResult(
    bool Succeeded,
    string Status,
    string Message,
    string? CertificateId,
    string? BaseDomain);

public sealed record DeleteCertificateResponse(
    bool Succeeded,
    string Status,
    string Message,
    string? CertificateId,
    bool MetadataDeleted,
    bool FilesDeleted,
    bool NpmCertificateDeleted,
    IReadOnlyList<string> Warnings);
