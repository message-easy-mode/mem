using System.Text.Json;
using Infrastructure.Data.Entities;
using Modules.Operator.Domains;
using Modules.Shared.Domains.Renewal;

namespace Api.IntegrationTests.Domains;

public sealed class OperatorDomainRenewalContractTests
{
    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01A_safe_read_contract_exposes_status_but_never_secret_material()
    {
        var state = new DomainCertificateRenewalState(
            DomainId: Guid.NewGuid(),
            BaseDomain: "deltabox.dev",
            DnsProvider: "desec",
            DnsZone: "deltabox.dev",
            PolicyConfigured: true,
            AutoRenewEnabled: true,
            AcmeEmail: "ops@deltabox.dev",
            RenewalWindowDays: 30,
            RetryIntervalHours: 24,
            CredentialConfigured: true,
            CredentialUpdatedAtUtc: new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc),
            HasActiveProductionCertificate: true,
            ActiveCertificateId: "cert-production",
            ActiveCertificateExpiresAtUtc: new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc),
            ReadinessStatus: DomainRenewalReadinessStatuses.Ready,
            ReadinessMessage: "Automatic renewal is configured.",
            PolicyUpdatedAtUtc: new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc));

        var response = OperatorDomainsEndpoints.ToRenewalResponse(state);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("credentialConfigured", json, StringComparison.Ordinal);
        Assert.Contains("credentialUpdatedAtUtc", json, StringComparison.Ordinal);
        Assert.DoesNotContain("providerToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("protectedValue", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(OperatorDomainRenewalResponse).GetProperty("ProviderToken"));
        Assert.Null(typeof(OperatorDomainRenewalResponse).GetProperty("ProtectedValue"));
    }

    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01A_routes_lock_owner_step_up_and_staging_non_persistence_contract()
    {
        var root = FindRepositoryRoot();
        var endpointSource = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Operator", "Domains", "OperatorDomainsEndpoints.cs"));
        var issuanceSource = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Issuance", "DomainCertificateIssueOrchestrator.cs"));

        Assert.Contains("group.MapGet(\"/renewal\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapPut(\"/{domainId:guid}/renewal/credential\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("group.MapDelete(\"/{domainId:guid}/renewal/credential\"", endpointSource, StringComparison.Ordinal);
        Assert.Contains("RequireRecentStepUpAsync", endpointSource, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(MemOperatorPolicies.ManagePlatform)", endpointSource, StringComparison.Ordinal);
        Assert.Contains("if (!request.UseStaging", endpointSource, StringComparison.Ordinal);

        Assert.Contains("if (input.UseStaging)", issuanceSource, StringComparison.Ordinal);
        Assert.Contains("ConfigureFromSuccessfulProductionIssuanceAsync", issuanceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01D_routes_expose_history_manual_run_and_wake_the_server_owned_worker_after_mutations()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Operator", "Domains", "OperatorDomainsEndpoints.cs"));

        Assert.Contains("group.MapGet(\"/{domainId:guid}/renewal/history\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{domainId:guid}/renewal/run\"", source, StringComparison.Ordinal);
        Assert.Contains("QueueManualRenewalAsync", source, StringComparison.Ordinal);
        Assert.Contains("DomainCertificateRenewalWakeSignal wakeSignal", source, StringComparison.Ordinal);
        Assert.True(
            source.Split("wakeSignal.Signal();", StringSplitOptions.None).Length - 1 >= 4,
            "Renew now, policy changes, credential rotation and credential removal must wake the server-owned renewal worker.");
    }


    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01D_CORR_06_operator_domain_utc_timestamps_serialize_with_explicit_utc_designators()
    {
        var expiry = new DateTime(2026, 11, 27, 0, 52, 11, DateTimeKind.Unspecified);
        var window = new DateTime(2026, 10, 28, 0, 52, 11, DateTimeKind.Unspecified);
        var state = new DomainCertificateRenewalState(
            DomainId: Guid.NewGuid(),
            BaseDomain: "deltabox.dev",
            DnsProvider: "desec",
            DnsZone: "deltabox.dev",
            PolicyConfigured: true,
            AutoRenewEnabled: false,
            AcmeEmail: null,
            RenewalWindowDays: 30,
            RetryIntervalHours: 24,
            CredentialConfigured: false,
            CredentialUpdatedAtUtc: null,
            HasActiveProductionCertificate: true,
            ActiveCertificateId: "cert-production",
            ActiveCertificateExpiresAtUtc: expiry,
            ReadinessStatus: DomainRenewalReadinessStatuses.RenewalCredentialRequired,
            ReadinessMessage: "Renewal credential required.",
            PolicyUpdatedAtUtc: new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Unspecified));
        var projection = new DomainCertificateRenewalProjection(
            State: state,
            OperationalStatus: DomainRenewalOperationalStatuses.Unready,
            CertificateExpired: false,
            DaysRemaining: 76,
            NextEligibleRenewalAtUtc: window,
            NextAutomaticAttemptAtUtc: null,
            LastAttemptAtUtc: null,
            LastSuccessfulRenewalAtUtc: null,
            LatestOperationId: null,
            LatestOperationStatus: null,
            LatestOperationStep: null,
            LatestOperationAttemptCount: 0,
            LatestRequestedBy: null,
            LatestErrorCode: null,
            DiagnosticsIncidentId: null,
            DiagnosticsHref: null,
            ManualRenewAvailable: false);

        var response = OperatorDomainsEndpoints.ToRenewalResponse(projection);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(DateTimeKind.Utc, response.ActiveCertificateExpiresAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, response.NextEligibleRenewalAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, response.PolicyUpdatedAtUtc!.Value.Kind);
        Assert.Contains("\"activeCertificateExpiresAtUtc\":\"2026-11-27T00:52:11Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"nextEligibleRenewalAtUtc\":\"2026-10-28T00:52:11Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01D_CORR_06_certificate_read_timestamps_are_normalized_after_sqlite_roundtrip()
    {
        var domain = new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = "deltabox.dev",
            DisplayName = "deltabox.dev",
            Purpose = "platform-main",
            IsMainPlatformDomain = true,
            DnsProvider = "desec",
            DnsZone = "deltabox.dev",
            Status = "Active",
            CreatedAtUtc = new DateTime(2026, 8, 29, 0, 0, 0, DateTimeKind.Unspecified),
            UpdatedAtUtc = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Unspecified)
        };
        var certificate = new CertificateEntity
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Domain = domain,
            CertificateId = "cert-production",
            CommonName = "*.deltabox.dev",
            Provider = "letsencrypt",
            IsWildcard = true,
            IsStaging = false,
            IsMainPlatformCertificate = true,
            IsActive = true,
            Status = "Valid",
            CreatedAtUtc = new DateTime(2026, 8, 29, 0, 50, 0, DateTimeKind.Unspecified),
            ExpiresAtUtc = new DateTime(2026, 11, 27, 0, 52, 11, DateTimeKind.Unspecified),
            LastValidatedAtUtc = new DateTime(2026, 9, 12, 1, 15, 0, DateTimeKind.Unspecified),
            LastImportedToNpmAtUtc = new DateTime(2026, 8, 29, 0, 55, 0, DateTimeKind.Unspecified)
        };
        domain.ActiveCertificateId = certificate.Id;
        domain.ActiveCertificate = certificate;
        domain.Certificates.Add(certificate);

        var response = OperatorDomainsEndpoints.ToCertificateRead(certificate);
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(DateTimeKind.Utc, response.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, response.ExpiresAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, response.LastValidatedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, response.LastImportedToNpmAtUtc!.Value.Kind);
        Assert.Contains("\"expiresAtUtc\":\"2026-11-27T00:52:11Z\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DOMAINS_CERTIFICATE_RENEWAL_01D_CORR_06_domain_summary_detail_and_certificate_mappers_normalize_utc_contracts()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Operator", "Domains", "OperatorDomainsEndpoints.cs"));

        Assert.Contains("ActiveCertificateExpiresAtUtc = NormalizeUtc(item.ActiveCertificateExpiresAtUtc)", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeUtc(state.ActiveCertificateExpiresAtUtc)", source, StringComparison.Ordinal);
        Assert.True(
            source.Split("NormalizeUtc(certificate.ExpiresAtUtc)", StringSplitOptions.None).Length - 1 >= 2,
            "Both certificate read and Domain-detail certificate projections must emit explicit UTC timestamps.");
        Assert.Contains("NormalizeUtc(domain.CreatedAtUtc)", source, StringComparison.Ordinal);
        Assert.Contains("NormalizeUtc(domain.UpdatedAtUtc)", source, StringComparison.Ordinal);
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

        throw new InvalidOperationException("Repository root could not be found from the test output directory.");
    }
}
