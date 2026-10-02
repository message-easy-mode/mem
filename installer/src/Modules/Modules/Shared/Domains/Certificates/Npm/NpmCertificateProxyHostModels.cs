namespace Modules.Shared.Domains.Certificates.Npm;

public sealed record NpmCertificateTestProxyHostRequest(
    string Domain,
    string ForwardHost,
    int ForwardPort,
    string ForwardScheme = "http");

public sealed record NpmCertificateTestProxyHostResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorCode,
    string? ErrorDetail,
    int? NpmCertificateId,
    int? ProxyHostId,
    IReadOnlyList<CertificateOperationEvidence> Evidence);