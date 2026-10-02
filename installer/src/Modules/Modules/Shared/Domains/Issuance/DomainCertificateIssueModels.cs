using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Issuance;

public static class DomainCertificateIssueOperation
{
    public const string OperationName = "domains.certificate.issue";
    public const string ActiveIdempotencyKey = "issue:active";

    public const string QueuedStatus = "queued";
    public const string RunningStatus = "running";
    public const string SucceededStatus = "succeeded";
    public const string FailedStatus = "failed";

    public const string CandidateSecretCategory = "certificate-issuance";

    public static string BuildCompletedIdempotencyKey(string requestId) =>
        $"issue:{requestId.Trim().ToLowerInvariant()}";

    public static string BuildCandidateSecretKey(Guid operationId) =>
        $"issue.{operationId:N}.desec.provider-token";
}

public sealed record DomainCertificateIssueStartRequest(
    string RequestId,
    string Email,
    string ProviderToken,
    bool UseStaging);

public sealed record DomainCertificateIssueInput(
    string RequestId,
    string Email,
    bool UseStaging);

public sealed record DomainCertificateIssueProgressSnapshot(
    string PhaseCode,
    string SafeSummary,
    DateTime TimestampUtc);

public sealed record DomainCertificateIssueProgressState(
    string? PhaseSummary,
    IReadOnlyList<DomainCertificateIssueProgressSnapshot> History,
    string? DiagnosticsIncidentId = null,
    string? DiagnosticsEventId = null);

public sealed record DomainCertificateIssueOperationState(
    Guid OperationId,
    Guid DomainId,
    string RequestId,
    string Status,
    string? PhaseCode,
    string? PhaseSummary,
    bool UseStaging,
    DateTime RequestedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    int AttemptCount,
    IReadOnlyList<DomainCertificateIssueProgressSnapshot> Progress,
    CertificateOperationResult? Result,
    string? CertificateId,
    string? DiagnosticsIncidentId)
{
    public bool IsTerminal =>
        string.Equals(Status, DomainCertificateIssueOperation.SucceededStatus, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Status, DomainCertificateIssueOperation.FailedStatus, StringComparison.OrdinalIgnoreCase);
}

public sealed record DomainCertificateIssueLatestResponse(
    DomainCertificateIssueOperationState? Operation);

public sealed record DomainCertificateIssueActiveOperationState(
    Guid OperationId,
    Guid DomainId,
    string BaseDomain,
    string Status,
    string? PhaseCode,
    string? PhaseSummary,
    bool UseStaging,
    DateTime RequestedAtUtc,
    DateTime? StartedAtUtc);

public sealed record DomainCertificateIssueActiveResponse(
    IReadOnlyList<DomainCertificateIssueActiveOperationState> Operations);

public sealed record DomainCertificateIssueQueueResult(
    bool Accepted,
    string Status,
    string Message,
    DomainCertificateIssueOperationState? Operation);

public sealed record DomainCertificateIssueExecutionRequest(
    Guid OperationId,
    Guid DomainId,
    CertificateIssueRequest CertificateRequest);
