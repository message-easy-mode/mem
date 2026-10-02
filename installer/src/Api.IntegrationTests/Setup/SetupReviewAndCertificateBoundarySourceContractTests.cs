namespace Api.IntegrationTests.Setup;

public sealed class SetupReviewAndCertificateBoundarySourceContractTests
{
    [Fact]
    public void STARTUP_INSTALL_REL_01E_review_is_the_frozen_consent_boundary_before_mutation()
    {
        var root = FindRepositoryRoot();
        var reviewService = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "Review", "SetupReviewService.cs"));
        var installPlanService = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "InstallPlans", "InstallPlanService.cs"));
        var reviewPage = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Web", "src", "features", "setup", "review", "pages", "setup-review-page.tsx"));
        var messages = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Web", "src", "app", "i18n", "messages.ts"));

        Assert.Contains("FrozenConfigJson = frozenJson", reviewService, StringComparison.Ordinal);
        Assert.Contains("SetupReviewPlanFingerprint.Compute", reviewService, StringComparison.Ordinal);
        Assert.Contains("SetupReviewPlanFingerprint.Matches", installPlanService, StringComparison.Ordinal);
        Assert.DoesNotContain("entity.FrozenConfigJson = entity.ConfigJson", installPlanService, StringComparison.Ordinal);
        Assert.Contains("t(\"setup.review.noMutationTitle\")", reviewPage, StringComparison.Ordinal);
        Assert.Contains(
            "\"setup.review.noMutationTitle\": \"No external mutation occurs when Review is accepted.\"",
            messages,
            StringComparison.Ordinal);
        Assert.DoesNotContain("v0.1.1 platform dependencies", reviewPage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01J_CORR_04_certificate_mutation_uses_protected_credentials_and_bounded_authoritative_DNS_recovery_after_review()
    {
        var root = FindRepositoryRoot();
        var provisioner = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "InstallRuns", "InstallPlatformCertificateProvisioner.cs"));
        var issuer = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Acme", "AcmeCertificateIssuer.cs"));
        var observer = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Dns", "AuthoritativeDnsChallengeObserver.cs"));
        var readiness = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Dns", "DnsChallengeReadinessService.cs"));
        var publicObserver = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Dns", "PublicDnsChallengeObserver.cs"));
        var options = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "CertificateOptions.cs"));
        var injection = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "SharedDomainsInjection.cs"));
        var progressReporter = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "InstallRuns", "InstallProgressReporter.cs"));
        var progressModels = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "InstallRuns", "InstallProgressModels.cs"));

        Assert.Contains("ResolveProtectedAsync", provisioner, StringComparison.Ordinal);
        Assert.Contains("IssuePlatformCertificateAsync", provisioner, StringComparison.Ordinal);
        Assert.Contains("mutationBoundary", provisioner, StringComparison.Ordinal);
        Assert.Contains("post-review-installation", provisioner, StringComparison.Ordinal);
        Assert.Contains("WaitForReadyAsync", issuer, StringComparison.Ordinal);
        Assert.Contains("certificate.dns-authoritative", readiness, StringComparison.Ordinal);
        Assert.Contains("certificate.dns-stability", readiness, StringComparison.Ordinal);
        Assert.DoesNotContain("certificate.dns-public", readiness, StringComparison.Ordinal);
        Assert.Contains("DnsVisibilityStabilitySeconds { get; init; } = 300", options, StringComparison.Ordinal);
        Assert.Contains("DnsVisibilityStabilityTimeoutSeconds { get; init; } = 420", options, StringComparison.Ordinal);
        Assert.Contains("AcmeDnsValidationMaxAttempts { get; init; } = 2", options, StringComparison.Ordinal);
        Assert.Contains("PublicDnsResolvers { get; init; } = []", options, StringComparison.Ordinal);
        Assert.Contains("PublicDnsObservationNotConfigured", publicObserver, StringComparison.Ordinal);
        Assert.DoesNotContain("1.1.1.1", publicObserver, StringComparison.Ordinal);
        Assert.DoesNotContain("8.8.8.8", publicObserver, StringComparison.Ordinal);
        Assert.DoesNotContain("9.9.9.9", publicObserver, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<PublicDnsChallengeObserver>", injection, StringComparison.Ordinal);
        Assert.Contains("IsRecoverableSecondaryDnsValidationMiss", issuer, StringComparison.Ordinal);
        Assert.Contains("certificate.acme-dns-recovery", issuer, StringComparison.Ordinal);
        Assert.Contains("certificate.acme-validation-retry", issuer, StringComparison.Ordinal);
        Assert.Contains("CertificateIssueProgressTransition.Recovering", issuer, StringComparison.Ordinal);
        Assert.Contains("CertificateIssueProgressTransition.Recovered", issuer, StringComparison.Ordinal);
        Assert.Contains("SetPhaseRecoveryStatusAsync", provisioner, StringComparison.Ordinal);
        Assert.Contains("InstallProgressStatuses.Recovering", progressReporter, StringComparison.Ordinal);
        Assert.Contains("InstallProgressStatuses.Recovered", progressReporter, StringComparison.Ordinal);
        Assert.Contains("public const string Recovering = \"Recovering\"", progressModels, StringComparison.Ordinal);
        Assert.Contains("public const string Recovered = \"Recovered\"", progressModels, StringComparison.Ordinal);
        Assert.Contains("AcmeSecondaryDnsValidationNotConverged", issuer, StringComparison.Ordinal);
        Assert.Contains("validationAttempt < maxValidationAttempts", issuer, StringComparison.Ordinal);
        Assert.Contains("AcmeChallengeValidationTimeoutSeconds", issuer, StringComparison.Ordinal);
        Assert.Contains("AcmeOrderFinalizationTimeoutSeconds", issuer, StringComparison.Ordinal);
        Assert.DoesNotContain("while (true)", issuer, StringComparison.Ordinal);
        Assert.Contains("finally", issuer, StringComparison.Ordinal);
        Assert.Contains("DeleteTxtChallengeAsync", issuer, StringComparison.Ordinal);
        var upsertIndex = issuer.IndexOf("UpsertTxtChallengeAsync", StringComparison.Ordinal);
        var cleanupIndex = issuer.IndexOf("DeleteTxtChallengeAsync", StringComparison.Ordinal);
        Assert.True(upsertIndex >= 0 && cleanupIndex > upsertIndex, "DNS challenge cleanup must occur only after direct upsert; do not clear the RRset before publishing the new value.");
        Assert.Contains("ns1.desec.io", observer, StringComparison.Ordinal);
        Assert.Contains("ns2.desec.org", observer, StringComparison.Ordinal);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01E_CORR_05_certificate_import_uses_runtime_aware_NPM_authority()
    {
        var root = FindRepositoryRoot();
        var importProbe = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Npm", "NpmCertificateImportProbe.cs"));
        var resolver = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Integrations", "Npm", "Services", "NpmApiBaseUrlResolver.cs"));

        Assert.Contains("NpmApiBaseUrlResolver", importProbe, StringComparison.Ordinal);
        Assert.Contains("ResolveNpmApiBaseUrlAsync", importProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("_options.Value.BaseUrl", importProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("NPM API base URL is not configured", importProbe, StringComparison.Ordinal);
        Assert.Contains("ManagedNetworkAliases.Npm", resolver, StringComparison.Ordinal);
        Assert.Contains("MemManagedServiceRouteKinds.DockerNetwork", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_02_existing_certificate_replacement_reapplies_NPM_consumers()
    {
        var root = FindRepositoryRoot();
        var importProbe = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Npm", "NpmCertificateImportProbe.cs"));
        var proxyService = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Integrations", "Npm", "Services", "NpmProxyHostService.cs"));

        Assert.Contains("ReapplyCertificateConsumersAsync", importProbe, StringComparison.Ordinal);
        Assert.Contains("npm.import.activation", importProbe, StringComparison.Ordinal);
        Assert.Contains("BuildCertificateConsumerRefreshPlan", proxyService, StringComparison.Ordinal);
        Assert.Contains("BuildExactUpdateRequest", proxyService, StringComparison.Ordinal);
        Assert.Contains("UpdateProxyHostAsync", proxyService, StringComparison.Ordinal);
        Assert.Contains("SnapshotMatches", proxyService, StringComparison.Ordinal);
    }


    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_03_certificate_import_hardens_NPM_log_boundary_before_key_upload()
    {
        var root = FindRepositoryRoot();
        var importProbe = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Npm", "NpmCertificateImportProbe.cs"));
        var apiClient = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Integrations", "Npm", "Services", "NpmApiClient.cs"));
        var boundary = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Integrations", "Npm", "Services", "NpmCertificateLogSecretBoundaryService.cs"));

        var hardenIndex = importProbe.IndexOf("EnsureHardenedAsync", StringComparison.Ordinal);
        var uploadIndex = importProbe.IndexOf("UploadCustomCertificateFilesAsync", StringComparison.Ordinal);

        Assert.True(hardenIndex >= 0 && uploadIndex > hardenIndex,
            "The NPM logging secret boundary must be verified before any certificate private key is uploaded.");
        Assert.Contains("No certificate private key was sent to NPM", boundary, StringComparison.Ordinal);
        Assert.Contains("Writing Custom Certificate: id=", boundary, StringComparison.Ordinal);
        Assert.Contains("HttpCompletionOption.ResponseHeadersRead", apiClient, StringComparison.Ordinal);
        Assert.Contains("response content was withheld", apiClient, StringComparison.Ordinal);
        Assert.DoesNotContain("custom certificate file upload failed ({(int)response.StatusCode}): {body}", apiClient, StringComparison.Ordinal);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_01_hardening_restart_is_server_owned_and_import_failures_create_incidents()
    {
        var root = FindRepositoryRoot();
        var importProbe = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Npm", "NpmCertificateImportProbe.cs"));
        var boundary = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Integrations", "Npm", "Services", "NpmCertificateLogSecretBoundaryService.cs"));

        Assert.Contains("new CancellationTokenSource(MutationTimeout)", boundary, StringComparison.Ordinal);
        Assert.Contains("TryRecoverRunningAsync", boundary, StringComparison.Ordinal);
        Assert.Contains("hardened-inactive", boundary, StringComparison.Ordinal);
        Assert.Contains("npm-certificate-log-boundary-v1", boundary, StringComparison.Ordinal);
        Assert.Contains("cancellationToken.ThrowIfCancellationRequested()", boundary, StringComparison.Ordinal);
        Assert.Contains("code: \"npm.certificate.import_failed\"", importProbe, StringComparison.Ordinal);
        Assert.Contains("createIncident: true", importProbe, StringComparison.Ordinal);
        Assert.Contains("feature: \"domains\"", importProbe, StringComparison.Ordinal);
        Assert.Contains("stage: \"npm-certificate-import\"", importProbe, StringComparison.Ordinal);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_02_first_post_hardening_import_is_server_owned_and_waits_for_certificate_API()
    {
        var root = FindRepositoryRoot();
        var importProbe = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Shared", "Domains", "Certificates", "Npm", "NpmCertificateImportProbe.cs"));

        var boundaryIndex = importProbe.IndexOf("EnsureHardenedAsync", StringComparison.Ordinal);
        var lifetimeIndex = importProbe.IndexOf("new CancellationTokenSource(ImportMutationTimeout)", StringComparison.Ordinal);
        var readinessIndex = importProbe.IndexOf("WaitForCertificateApiReadyAsync(importToken)", StringComparison.Ordinal);
        var uploadIndex = importProbe.IndexOf("UploadCustomCertificateFilesAsync", StringComparison.Ordinal);

        Assert.True(boundaryIndex >= 0 && lifetimeIndex > boundaryIndex,
            "Certificate import must switch to a bounded server-owned lifetime after the secret boundary is established.");
        Assert.True(readinessIndex > lifetimeIndex && uploadIndex > readinessIndex,
            "After a hardening restart, full NPM certificate API readiness must be proven before any private key upload.");
        Assert.Contains("CertificateApiReachable", importProbe, StringComparison.Ordinal);
        Assert.Contains("ApiAuthenticated", importProbe, StringComparison.Ordinal);
        Assert.Contains("npm.readiness.afterHardeningRestart", importProbe, StringComparison.Ordinal);
        Assert.Contains("PersistNpmLinkAsync(certificateId, existing.id, importToken)", importProbe, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistNpmLinkAsync(certificateId, existing.id, cancellationToken)", importProbe, StringComparison.Ordinal);
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
}
