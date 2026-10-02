using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Issuance;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Domains;

public sealed class OperatorCertificateIssuanceBoundaryTests
{
    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_domain_owned_issue_failure_is_recorded_without_secret_evidence()
    {
        var writer = new RecordingDiagnosticWriter();
        var domainId = Guid.NewGuid();
        var request = new CertificateIssueRequest(
            Domain: "*.matrixeasyhost.com",
            Zone: "matrixeasyhost.com",
            IsWildcard: true,
            Email: "operator@example.invalid",
            Provider: "desec",
            ProviderToken: "super-secret-desec-token",
            UseStaging: false,
            StorageName: "matrixeasyhost-production");
        var result = new CertificateOperationResult(
            Succeeded: false,
            Status: "Failed",
            Message: "ACME DNS-01 challenge validation failed.",
            ErrorCode: "AcmeChallengeValidationFailed",
            ErrorDetail: "During secondary validation: DNS problem: SERVFAIL looking up TXT.",
            Evidence:
            [
                new("domain", "*.matrixeasyhost.com", Status: "Failed"),
                new("acmeAccount", "registered for operator@example.invalid", Status: "Succeeded"),
                new("dnsChallengeValue", "created, masked", Sensitive: true, Status: "Failed")
            ]);

        var writeResult = await DomainCertificateIssueDiagnostics.RecordFailureAsync(
            writer,
            domainId,
            request,
            result);

        Assert.NotNull(writeResult);
        Assert.Single(writer.Requests);

        var recorded = writer.Requests[0];
        Assert.Equal(MemDiagnosticSeverities.Error, recorded.Severity);
        Assert.Equal("domains.certificate.issue_failed", recorded.EventCode);
        Assert.Equal(nameof(DomainCertificateIssueOrchestrator), recorded.Source);
        Assert.Equal("domains", recorded.Feature);
        Assert.Equal("certificate-issuance", recorded.Stage);
        Assert.True(recorded.CreateIncident);
        Assert.True(recorded.Retryable);
        Assert.Equal(domainId.ToString("D"), recorded.Resource?.Id);
        Assert.Equal($"/domains/{domainId:D}/certificates/new", recorded.Resource?.WorkspacePath);
        Assert.Contains("super-secret-desec-token", recorded.ExactSecrets ?? []);
        Assert.Equal("AcmeChallengeValidationFailed", recorded.Details?["errorCode"]);
        Assert.Contains(
            recorded.Details ?? new Dictionary<string, string?>(),
            pair => pair.Key.Contains("evidence", StringComparison.Ordinal) &&
                    pair.Value?.Contains("matrixeasyhost.com", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            recorded.Details ?? new Dictionary<string, string?>(),
            pair => pair.Key.Contains("acmeAccount", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            recorded.Details ?? new Dictionary<string, string?>(),
            pair => pair.Key.Contains("dnsChallengeValue", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("UnsupportedDnsProvider")]
    [InlineData("AcmeEmailMissing")]
    [InlineData("AcmeEmailInvalid")]
    [InlineData("DesecTokenMissing")]
    [InlineData("DomainNotFound")]
    public void DOMAINS_WORKSPACE_01G_request_validation_failures_do_not_create_incidents(
        string errorCode)
    {
        Assert.False(DomainCertificateIssueDiagnostics.ShouldCreateIncident(errorCode));
    }

    [Fact]
    public void DOMAINS_CERTIFICATE_NPM_IMPORT_CORR_01_production_issuance_imports_into_NPM_and_staging_does_not()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Issuance", "DomainCertificateIssueExecutor.cs"));

        var issueIndex = source.IndexOf("certificateService.IssueAsync", StringComparison.Ordinal);
        var registryIndex = source.IndexOf("registry.SyncStorageAsync", StringComparison.Ordinal);
        var stagingGuardIndex = source.IndexOf("if (request.CertificateRequest.UseStaging)", StringComparison.Ordinal);
        var importIndex = source.IndexOf("npmCertificateImport.ImportAsync", StringComparison.Ordinal);
        var persistedIdIndex = source.IndexOf("npm.certificateId", StringComparison.Ordinal);

        Assert.True(issueIndex >= 0, "Production Domain issuance must still begin with the shared certificate issuer.");
        Assert.True(registryIndex > issueIndex, "The issued certificate must be registered before NPM import can persist its linkage.");
        Assert.True(stagingGuardIndex > registryIndex, "Staging must be registered for operator visibility before the production-only NPM boundary.");
        Assert.True(importIndex > stagingGuardIndex, "Only the production path may invoke NPM certificate import.");
        Assert.True(persistedIdIndex > importIndex, "Successful production issuance must expose the persisted NPM certificate identity in durable result evidence.");

        Assert.Contains("NpmCertificateImportProbe npmCertificateImport", source, StringComparison.Ordinal);
        Assert.Contains("MatchingCertificateId is null or <= 0", source, StringComparison.Ordinal);
        Assert.Contains("NpmCertificateImportFailed", source, StringComparison.Ordinal);
        Assert.Contains("certificate.npm-import", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DOMAINS_WORKSPACE_01G_01F_B_active_issuance_projection_is_bounded_and_read_only()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Operator", "Domains", "OperatorDomainsEndpoints.cs"));

        var start = source.IndexOf(
            "group.MapGet(\"/certificates/issuance/active\"",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "group.MapGet(\"/{domainId:guid}/certificates/issuance/latest\"",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not isolate the fleet active-issuance endpoint.");

        var endpoint = source[start..end];
        Assert.Contains("issuance.GetActiveAsync", endpoint, StringComparison.Ordinal);
        Assert.Contains("new DomainCertificateIssueActiveResponse(active)", endpoint, StringComparison.Ordinal);
        Assert.Contains("MemOperatorPolicies.ReadSafeStatus", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderToken", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", endpoint, StringComparison.Ordinal);
        var projectionProperties = typeof(DomainCertificateIssueActiveOperationState)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        Assert.DoesNotContain("RequestId", projectionProperties);
        Assert.DoesNotContain("Result", projectionProperties);
        Assert.DoesNotContain("Progress", projectionProperties);
        Assert.DoesNotContain("DiagnosticsIncidentId", projectionProperties);
    }

    [Fact]
    public void DOMAINS_WORKSPACE_01E_latest_read_uses_non_null_json_envelope_for_empty_state()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Operator", "Domains", "OperatorDomainsEndpoints.cs"));

        var start = source.IndexOf(
            "group.MapGet(\"/{domainId:guid}/certificates/issuance/latest\"",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "group.MapGet(\"/{domainId:guid}/certificates/issuance/{operationId:guid}\"",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "Could not isolate the durable issuance latest endpoint.");

        var endpoint = source[start..end];
        Assert.Contains("new DomainCertificateIssueLatestResponse(latest)", endpoint, StringComparison.Ordinal);
        Assert.DoesNotContain("Results.Json(await issuance.GetLatestAsync", endpoint, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "installer", "src", "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }

    private sealed class RecordingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: "evt_certificate_issue",
                IncidentId: request.CreateIncident ? "inc_certificate_issue" : null,
                WarningCode: null));
        }
    }
}
