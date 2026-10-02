namespace Modules.Shared.Domains.Certificates.Acme;

public sealed record AcmeIssueResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorCode,
    string? ErrorDetail,
    string? CertificatePem,
    string? PrivateKeyPem,
    DateTimeOffset? ExpiresAtUtc,
    string? Thumbprint,
    IReadOnlyList<CertificateOperationEvidence> Evidence)
{
    public CertificateOperationResult ToOperationResult()
    {
        return new CertificateOperationResult(
            Succeeded,
            Status,
            Message,
            ErrorCode,
            ErrorDetail,
            Evidence);
    }

    public static AcmeIssueResult Failed(
        string message,
        string errorCode,
        string? errorDetail,
        IReadOnlyList<CertificateOperationEvidence> evidence)
    {
        return new AcmeIssueResult(
            Succeeded: false,
            Status: "Failed",
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: errorDetail,
            CertificatePem: null,
            PrivateKeyPem: null,
            ExpiresAtUtc: null,
            Thumbprint: null,
            Evidence: evidence);
    }
}