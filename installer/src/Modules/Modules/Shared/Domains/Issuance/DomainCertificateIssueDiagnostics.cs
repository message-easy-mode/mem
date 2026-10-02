using Modules.Shared.Domains.Certificates;
using Shared.Diagnostics;

namespace Modules.Shared.Domains.Issuance;

public static class DomainCertificateIssueDiagnostics
{
    public static bool ShouldCreateIncident(string? errorCode) =>
        errorCode is not (
            "UnsupportedDnsProvider" or
            "AcmeEmailMissing" or
            "AcmeEmailInvalid" or
            "DesecTokenMissing" or
            "DomainNotFound");

    public static Task<MemDiagnosticWriteResult?> RecordFailureAsync(
        IMemDiagnosticEventWriter diagnostics,
        Guid domainId,
        CertificateIssueRequest request,
        CertificateOperationResult result)
    {
        var createIncident = ShouldCreateIncident(result.ErrorCode);
        var exactSecret = request.ProviderToken ?? string.Empty;
        var details = new Dictionary<string, string?>
        {
            ["domain"] = request.Domain,
            ["zone"] = request.Zone,
            ["provider"] = request.Provider,
            ["certificateEnvironment"] = request.UseStaging ? "staging" : "production",
            ["errorCode"] = result.ErrorCode,
            ["errorDetail"] = Redact(result.ErrorDetail, exactSecret),
            ["operationStatus"] = result.Status,
            ["evidenceCount"] = result.Evidence.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var evidenceIndex = 0;
        foreach (var item in result.Evidence)
        {
            if (item.Sensitive ||
                string.Equals(item.Key, "acmeAccount", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            evidenceIndex++;
            var safeValue = Redact(item.Value, exactSecret);
            details[$"evidence.{evidenceIndex:D2}.{item.Key}"] =
                string.IsNullOrWhiteSpace(item.Status)
                    ? safeValue
                    : $"{item.Status}: {safeValue}";
        }

        return diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: createIncident
                ? MemDiagnosticSeverities.Error
                : MemDiagnosticSeverities.Warning,
            EventCode: "domains.certificate.issue_failed",
            Source: nameof(DomainCertificateIssueOrchestrator),
            Feature: "domains",
            Stage: "certificate-issuance",
            Message: Redact(result.Message, exactSecret) ?? "Certificate issuance failed.",
            CreateIncident: createIncident,
            Resource: new MemDiagnosticResource(
                Kind: "domain",
                Id: domainId.ToString("D"),
                DisplayName: request.Domain,
                WorkspacePath: $"/domains/{domainId:D}/certificates/new"),
            Observed: new Dictionary<string, string?>
            {
                ["result"] = "failed"
            },
            Details: details,
            SuggestedAction: createIncident
                ? "Open Diagnostics, review the recorded certificate/DNS evidence, correct the reported cause, then retry issuance from the owning Domain."
                : "Correct the certificate request fields and retry issuance from the owning Domain.",
            Retryable: createIncident,
            ExactSecrets: string.IsNullOrWhiteSpace(request.ProviderToken)
                ? null
                : [request.ProviderToken]));
    }

    private static string? Redact(string? value, string exactSecret)
    {
        if (value is null || string.IsNullOrEmpty(exactSecret))
        {
            return value;
        }

        return value.Replace(exactSecret, "[redacted]", StringComparison.Ordinal);
    }
}
