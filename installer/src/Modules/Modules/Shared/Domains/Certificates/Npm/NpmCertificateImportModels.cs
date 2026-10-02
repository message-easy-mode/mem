namespace Modules.Shared.Domains.Certificates.Npm;

public sealed record NpmCertificateProbeResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorCode,
    string? ErrorDetail,
    int? MatchingCertificateId,
    IReadOnlyList<CertificateOperationEvidence> Evidence);