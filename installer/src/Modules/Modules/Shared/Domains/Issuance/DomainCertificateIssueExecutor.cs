using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;
using Shared.Exceptions;

namespace Modules.Shared.Domains.Issuance;

public sealed class DomainCertificateIssueExecutor(
    CertificateService certificateService,
    DomainRegistryService registry,
    NpmCertificateImportProbe npmCertificateImport) : IDomainCertificateIssueExecutor
{
    public async Task<CertificateOperationResult> ExecuteAsync(
        DomainCertificateIssueExecutionRequest request,
        CertificateIssueProgressCallback progressCallback,
        CancellationToken cancellationToken)
    {
        var result = await certificateService.IssueAsync(
            request.CertificateRequest,
            cancellationToken,
            progressCallback);

        if (!result.Succeeded)
        {
            return result;
        }

        await registry.SyncStorageAsync(cancellationToken);

        if (request.CertificateRequest.UseStaging)
        {
            return result;
        }

        var certificateId = EvidenceValue(result, "certificateId");
        if (string.IsNullOrWhiteSpace(certificateId))
        {
            return result with
            {
                Succeeded = false,
                Status = "Failed",
                Message = "Certificate was issued and stored, but the generated certificate id could not be resolved for NPM import.",
                ErrorCode = "CertificateIdMissing",
                ErrorDetail = "The certificate issue result did not include certificateId evidence."
            };
        }

        await progressCallback(
            new CertificateIssueProgress(
                "certificate.npm-import",
                "Importing the production TLS certificate into Nginx Proxy Manager."),
            cancellationToken);

        try
        {
            var import = await npmCertificateImport.ImportAsync(
                certificateId,
                cancellationToken);
            var evidence = result.Evidence
                .Concat(PrefixEvidence("npm.import", import.Evidence))
                .ToList();

            if (!import.Succeeded || import.MatchingCertificateId is null or <= 0)
            {
                return result with
                {
                    Succeeded = false,
                    Status = "Failed",
                    Message = "Certificate was issued and stored, but Nginx Proxy Manager import did not complete.",
                    ErrorCode = import.ErrorCode ?? "NpmCertificateImportFailed",
                    ErrorDetail = import.ErrorDetail ?? import.Message,
                    Evidence = evidence
                };
            }

            evidence.Add(new CertificateOperationEvidence(
                Key: "npm.certificateId",
                Value: import.MatchingCertificateId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Status: "Succeeded"));

            return result with
            {
                Status = "Succeeded",
                Message = "Certificate issued, stored, validated, and imported into Nginx Proxy Manager.",
                ErrorCode = null,
                ErrorDetail = null,
                Evidence = evidence
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MemProblemException exception)
        {
            return result with
            {
                Succeeded = false,
                Status = "Failed",
                Message = "Certificate was issued and stored, but Nginx Proxy Manager import failed.",
                ErrorCode = "NpmCertificateImportFailed",
                ErrorDetail = exception.SafeDetail,
                Evidence = result.Evidence.Concat([
                    new CertificateOperationEvidence(
                        Key: "npm.import.problemCode",
                        Value: exception.Code,
                        Status: "Failed")
                ]).ToList()
            };
        }
    }

    private static string? EvidenceValue(
        CertificateOperationResult result,
        string key) =>
        result.Evidence
            .FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static IReadOnlyList<CertificateOperationEvidence> PrefixEvidence(
        string prefix,
        IReadOnlyList<CertificateOperationEvidence> evidence) =>
        evidence
            .Select(item => item with { Key = $"{prefix}.{item.Key}" })
            .ToList();
}
