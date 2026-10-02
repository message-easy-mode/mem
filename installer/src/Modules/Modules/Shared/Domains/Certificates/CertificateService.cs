using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates.Acme;

namespace Modules.Shared.Domains.Certificates;

public sealed class CertificateService
{
    private readonly AcmeCertificateIssuer _issuer;
    private readonly CertificateStorageService _storage;
    private readonly CertificateValidationService _validation;
    private readonly ILogger<CertificateService> _logger;

    public CertificateService(
        AcmeCertificateIssuer issuer,
        CertificateStorageService storage,
        CertificateValidationService validation,
        ILogger<CertificateService> logger)
    {
        _issuer = issuer;
        _storage = storage;
        _validation = validation;
        _logger = logger;
    }

    public async Task<CertificateOperationResult> IssueAsync(
        CertificateIssueRequest request,
        CancellationToken cancellationToken,
        CertificateIssueProgressCallback? progressCallback = null)
    {
        try
        {
            _logger.LogInformation(
                "Starting ACME certificate issuance. Domain={Domain}, Zone={Zone}, Provider={Provider}, StorageName={StorageName}, IsStaging={IsStaging}",
                request.Domain,
                request.Zone,
                request.Provider,
                request.StorageName,
                request.UseStaging);

            var issueResult = await _issuer.IssueAsync(
                request,
                cancellationToken,
                progressCallback);

            if (!issueResult.Succeeded ||
                string.IsNullOrWhiteSpace(issueResult.CertificatePem) ||
                string.IsNullOrWhiteSpace(issueResult.PrivateKeyPem))
            {
                return issueResult.ToOperationResult();
            }

            await ReportProgressAsync(
                progressCallback,
                "certificate.store",
                "Storing the issued TLS certificate and private key securely.",
                cancellationToken);

            var metadata = await _storage.StoreAsync(
                request,
                issueResult.CertificatePem,
                issueResult.PrivateKeyPem,
                expiresAtUtc: issueResult.ExpiresAtUtc,
                thumbprint: issueResult.Thumbprint,
                status: "Succeeded",
                cancellationToken);

            await ReportProgressAsync(
                progressCallback,
                "certificate.validate",
                "Validating the stored TLS certificate before platform registration.",
                cancellationToken);

            var validation = await _validation.ValidateAsync(
                metadata.CertificateId,
                cancellationToken);

            var evidence = issueResult.Evidence
                .Concat(new[]
                {
                    new CertificateOperationEvidence(
                        Key: "certificateId",
                        Value: metadata.CertificateId,
                        Status: "Succeeded"),

                    new CertificateOperationEvidence(
                        Key: "fullchainPath",
                        Value: metadata.FullchainPath,
                        Status: "Succeeded"),

                    new CertificateOperationEvidence(
                        Key: "privateKeyPath",
                        Value: "stored, sensitive",
                        Sensitive: true,
                        Status: "Succeeded"),

                    new CertificateOperationEvidence(
                        Key: "metadataPath",
                        Value: "metadata.json written",
                        Status: "Succeeded")
                })
                .Concat(PrefixEvidence("validation", validation.Evidence))
                .ToList();

            return new CertificateOperationResult(
                Succeeded: validation.Succeeded,
                Status: validation.Succeeded ? "Succeeded" : "Warning",
                Message: validation.Succeeded
                    ? "Certificate issued, stored, and validated."
                    : "Certificate issued and stored, but validation reported warnings.",
                ErrorCode: validation.Succeeded ? null : validation.ErrorCode,
                ErrorDetail: validation.Succeeded ? null : validation.ErrorDetail,
                Evidence: evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Certificate issuance failed for domain {Domain}",
                request.Domain);

            return new CertificateOperationResult(
                Succeeded: false,
                Status: "Failed",
                Message: "Certificate issuance failed.",
                ErrorCode: "CertificateIssueFailed",
                ErrorDetail: ex.Message,
                Evidence:
                [
                    new("domain", request.Domain),
                    new("zone", request.Zone),
                    new("provider", request.Provider),
                    new("useStaging", request.UseStaging.ToString())
                ]);
        }
    }

    private static Task ReportProgressAsync(
        CertificateIssueProgressCallback? callback,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        callback is null
            ? Task.CompletedTask
            : callback(
                new CertificateIssueProgress(phaseCode, safeSummary),
                cancellationToken);

    private static IReadOnlyList<CertificateOperationEvidence> PrefixEvidence(
        string prefix,
        IReadOnlyList<CertificateOperationEvidence> evidence)
    {
        return evidence
            .Select(item => item with
            {
                Key = $"{prefix}.{item.Key}"
            })
            .ToList();
    }
}