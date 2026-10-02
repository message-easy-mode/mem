namespace Modules.Shared.Domains.Renewal;

public static class DomainCertificateRenewalOperation
{
    public const string OperationName = "domains.certificate.renewal";

    public const string QueuedStatus = "queued";
    public const string RunningStatus = "running";
    public const string AwaitingActivationStatus = "awaiting-activation";
    public const string SucceededStatus = "succeeded";
    public const string FailedStatus = "failed";

    public static string BuildIdempotencyKey(
        Guid domainId,
        Guid sourceCertificateEntityId,
        DateTime sourceExpiresAtUtc)
    {
        var expires = sourceExpiresAtUtc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(sourceExpiresAtUtc, DateTimeKind.Utc)
            : sourceExpiresAtUtc.ToUniversalTime();

        return $"renewal:{domainId:N}:{sourceCertificateEntityId:N}:{expires:yyyyMMddHHmmss}";
    }
}

public sealed record DomainRenewalCandidateRequest(
    Guid OperationId,
    Guid DomainId,
    string BaseDomain,
    string Zone,
    string DnsProvider,
    string AcmeEmail,
    Guid SourceCertificateEntityId,
    string SourceCertificateId,
    string CommonName,
    bool IsWildcard,
    DateTime SourceCertificateExpiresAtUtc);

public sealed record DomainRenewalCandidateIssueResult(
    bool Succeeded,
    string Status,
    string? ErrorCode,
    Guid? CandidateCertificateEntityId,
    string? CandidateCertificateId);

public sealed record DomainCertificateRenewalScanResult(
    int DomainsEvaluated,
    int RenewalCyclesDue,
    int OperationsStarted,
    int AwaitingActivation,
    int Failed);
