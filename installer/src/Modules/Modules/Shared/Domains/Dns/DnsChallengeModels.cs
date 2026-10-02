using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Dns;

public sealed record DnsChallengeRequest(
    string Zone,
    string RecordName,
    string TxtValue,
    string ProviderToken);

public sealed record DnsChallengeResult(
    bool Succeeded,
    string Message,
    string? ErrorCode,
    IReadOnlyList<CertificateOperationEvidence> Evidence);

public sealed record DnsChallengeObservationResult(
    bool Visible,
    string Message,
    IReadOnlyList<CertificateOperationEvidence> Evidence,
    string? ErrorCode = null,
    int VisibleCount = 0,
    int ResponsiveCount = 0,
    int UnavailableCount = 0);

public sealed record DnsChallengeReadinessResult(
    bool Ready,
    string Message,
    string? ErrorCode,
    IReadOnlyList<CertificateOperationEvidence> Evidence);