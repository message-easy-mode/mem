namespace Api.IntegrationTests.Setup;

public sealed class SetupDomainMutationBoundarySourceContractTests
{
    [Fact]
    public void STARTUP_INSTALL_REL_01D_setup_domain_surface_is_validation_only_before_review()
    {
        var root = FindRepositoryRoot();
        var setupDomains = Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "Domains");

        var certificateEndpoints = File.ReadAllText(Path.Combine(
            setupDomains,
            "Certificates",
            "SetupCertificateEndpoints.cs"));
        var planEndpoints = File.ReadAllText(Path.Combine(
            setupDomains,
            "Planning",
            "SetupDomainPlanEndpoints.cs"));
        var planService = File.ReadAllText(Path.Combine(
            setupDomains,
            "Planning",
            "SetupDomainPlanService.cs"));
        var domainPage = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "src",
            "Web",
            "src",
            "features",
            "setup",
            "domains",
            "pages",
            "setup-domain-page.tsx"));

        var installPlanEndpoints = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "InstallPlans",
            "InstallPlanEndpoints.cs"));
        var messages = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "src",
            "Web",
            "src",
            "app",
            "i18n",
            "messages.ts"));

        Assert.DoesNotContain("MapPost(\"/issue\"", certificateEndpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("test-challenge", certificateEndpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("/npm/import", certificateEndpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("/npm/test-proxy-host", certificateEndpoints, StringComparison.Ordinal);

        Assert.Contains("MapPost(\"/validate\"", planEndpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("config/public-access", installPlanEndpoints, StringComparison.Ordinal);
        Assert.Contains("IDnsZoneAccessProbe", planService, StringComparison.Ordinal);
        Assert.DoesNotContain("IssueAsync(", planService, StringComparison.Ordinal);
        Assert.DoesNotContain("IDnsChallengeProvider", planService, StringComparison.Ordinal);
        Assert.DoesNotContain("CertificateService", planService, StringComparison.Ordinal);

        Assert.Contains("t(\"setup.domain.validationOnly\")", domainPage, StringComparison.Ordinal);
        Assert.Contains("t(\"setup.domain.validatePlan\")", domainPage, StringComparison.Ordinal);
        Assert.Contains("t(\"setup.domain.mutationBoundary\")", domainPage, StringComparison.Ordinal);
        Assert.Contains("\"setup.domain.validationOnly\": \"Validation only\"", messages, StringComparison.Ordinal);
        Assert.Contains(
            "\"setup.domain.mutationBoundary\": \"External DNS and certificate changes begin only after you accept the Review plan and start platform installation.\"",
            messages,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Check domain and create certificate", domainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Creating certificate", domainPage, StringComparison.Ordinal);
        Assert.DoesNotContain("AbortController", domainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01D_installation_plan_and_postgres_commands_do_not_contain_secret_values()
    {
        var root = FindRepositoryRoot();
        var setupModels = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "InstallPlans",
            "SetupModels.cs"));
        var postgres = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "InstallRuns",
            "InstallStepExecutor.Postgres.cs"));

        Assert.DoesNotContain("ProviderToken", setupModels, StringComparison.Ordinal);
        Assert.DoesNotContain("POSTGRES_PASSWORD=postgres", postgres, StringComparison.Ordinal);
        Assert.DoesNotContain("POSTGRES_PASSWORD=", postgres, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"POSTGRES_PASSWORD\"]", postgres, StringComparison.Ordinal);
        Assert.Contains("[\"POSTGRES_PASSWORD_FILE\"]", postgres, StringComparison.Ordinal);
        Assert.Contains("PostgresPasswordFilePath", postgres, StringComparison.Ordinal);
        Assert.Contains("CopyFileToContainerAsync", postgres, StringComparison.Ordinal);
        Assert.Contains("ResolveProtectedAsync", postgres, StringComparison.Ordinal);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01D_desec_validation_probe_is_read_only()
    {
        var root = FindRepositoryRoot();
        var provider = File.ReadAllText(Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Shared",
            "Domains",
            "Dns",
            "DesecDnsChallengeProvider.cs"));

        var probeStart = provider.IndexOf("Task<DnsZoneAccessProbeResult> ProbeAsync", StringComparison.Ordinal);
        var probeEnd = provider.IndexOf(
            "public async Task<DnsChallengeResult> DeleteTxtChallengeAsync",
            probeStart,
            StringComparison.Ordinal);

        Assert.True(probeStart >= 0, "The deSEC read-only access probe was not found.");
        Assert.True(probeEnd > probeStart, "The probe method boundary was not found.");

        var probeSource = provider[probeStart..probeEnd];
        Assert.Contains("HttpMethod.Get", probeSource, StringComparison.Ordinal);
        Assert.Contains("SendAsync", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PostAsync", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PutAsync", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteAsync", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpMethod.Post", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpMethod.Put", probeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpMethod.Delete", probeSource, StringComparison.Ordinal);
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
