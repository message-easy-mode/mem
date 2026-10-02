namespace Modules.Shared.Domains.Renewal;

public static class DomainRenewalOperationalStatuses
{
    public const string Ready = "ready";
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Running = "running";
    public const string AwaitingActivation = "awaiting-activation";
    public const string Failed = "failed";
    public const string Disabled = "disabled";
    public const string Unready = "unready";
}

public sealed record DomainCertificateRenewalProjection(
    DomainCertificateRenewalState State,
    string OperationalStatus,
    bool CertificateExpired,
    int? DaysRemaining,
    DateTime? NextEligibleRenewalAtUtc,
    DateTime? NextAutomaticAttemptAtUtc,
    DateTime? LastAttemptAtUtc,
    DateTime? LastSuccessfulRenewalAtUtc,
    Guid? LatestOperationId,
    string? LatestOperationStatus,
    string? LatestOperationStep,
    int LatestOperationAttemptCount,
    string? LatestRequestedBy,
    string? LatestErrorCode,
    string? DiagnosticsIncidentId,
    string? DiagnosticsHref,
    bool ManualRenewAvailable);

public sealed record DomainCertificateRenewalHistoryItem(
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

public sealed record DomainCertificateRenewalQueueResult(
    bool Accepted,
    string Status,
    string Message,
    Guid? OperationId);
