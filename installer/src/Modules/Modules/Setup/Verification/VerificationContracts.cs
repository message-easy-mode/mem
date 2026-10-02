namespace Modules.Setup.Verification;

public sealed record VerificationReportResponse(
    string Status,
    string Message,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<VerificationCheckResult> Checks);

public sealed record VerificationCheckResult(
    string Key,
    string Title,
    string Description,
    string Status,
    string Message,
    IReadOnlyList<VerificationEvidence> Evidence);

public sealed record VerificationEvidence(
    string Key,
    string Value,
    bool Sensitive = false,
    string? Status = null);