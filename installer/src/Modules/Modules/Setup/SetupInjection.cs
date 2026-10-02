using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Shared.Domains;
using Modules.Setup.HostChecks;
using Modules.Setup.HostChecks.Checks;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Lifecycle;
using Modules.Setup.Start;
using Modules.Setup.Verification;
using Modules.Setup.Domains.Certificates;
using Modules.Setup.Domains.Planning;
using Modules.Setup.Secrets;
using Modules.Setup.Review;
using Modules.Setup.SupportReports;

namespace Modules.Setup;

public static class SetupInjection
{
    public static IServiceCollection AddSetupApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSetupStart();
        services.AddScoped<FirstTimeSetupLockService>();
        services.AddScoped<FirstTimeSetupAuthorityService>();
        services.AddScoped<IInstallationSecretStore, ProtectedInstallationSecretStore>();
        services.AddScoped<InstallationCredentialService>();
        services.AddScoped<NpmAdminCredentialService>();
        services.AddScoped<Modules.Integrations.Npm.Contracts.INpmApiCredentialProvider, ProtectedNpmApiCredentialProvider>();
        services.AddHostChecks();
        services.AddInstallPlan();
        services.AddScoped<SetupReviewService>();
        services.AddInstallRun(configuration);
        services.AddSetupDomains(configuration);
        services.AddSetupVerification();
        services.AddScoped<SetupInstallationSupportReportSizeLimiter>();
        services.AddScoped<SetupInstallationSupportReportFormatter>();
        services.AddScoped<SetupInstallationSupportReportService>();

        return services;
    }

    private static IServiceCollection AddSetupStart(this IServiceCollection services)
    {
        services.AddScoped<SetupStartService>();
        return services;
    }

    private static IServiceCollection AddHostChecks(this IServiceCollection services)
    {
        services.AddSingleton<HostCheckRunStore>();
        services.AddScoped<HostChecksService>();

        services.AddScoped<ICommandRunner, CommandRunner>();
        services.AddScoped<IStandardInputCommandRunner, StandardInputCommandRunner>();
        services.AddScoped<ISetupDockerRuntimeProbe, DockerDotNetSetupDockerRuntimeProbe>();

        services.AddScoped<IHostCheck, UbuntuVersionCheck>();
        services.AddScoped<IHostCheck, ArchitectureCheck>();
        services.AddScoped<IHostCheck, CpuCheck>();
        services.AddScoped<IHostCheck, MemoryCheck>();
        services.AddScoped<IHostCheck, DiskSpaceCheck>();

        services.AddScoped<IHostCheck, DockerReachableCheck>();
        services.AddScoped<IHostCheck, DockerComposePluginCheck>();
        services.AddScoped<IHostCheck, DockerDataRootCheck>();
        services.AddScoped<IHostCheck, DockerDiskUsageCheck>();

        services.AddScoped<IHostCheck, PortAvailabilityCheck>();

        services.AddScoped<IHostCheck, ExistingMemContainersCheck>();
        services.AddScoped<IHostCheck, ExistingDockerVolumesCheck>();
        services.AddScoped<IHostCheck, ExistingDockerNetworksCheck>();

        return services;
    }

    private static IServiceCollection AddInstallPlan(this IServiceCollection services)
    {
        services.AddScoped<InstallPlanService>();
        return services;
    }

    private static IServiceCollection AddInstallRun(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MemCliHostCommandOptions>(
            configuration.GetSection(MemCliHostCommandOptions.SectionName));
        services.AddScoped<MemCliHostCommandInstaller>();
        services.AddSingleton<InstallProgressStore>();
        services.AddScoped<InstallProgressReporter>();
        services.AddScoped<InstallStepExecutor>();
        services.AddScoped<InstallStepGraphReconciler>();
        services.AddScoped<NpmInitialAdminBootstrapService>();
        services.AddScoped<INpmInitialAdminBootstrapService>(sp =>
            sp.GetRequiredService<NpmInitialAdminBootstrapService>());
        services.AddScoped<InstallPlatformCertificateProvisioner>();
        services.AddScoped<InstallRunner>();
        services.AddScoped<IInstallRunner>(sp => sp.GetRequiredService<InstallRunner>());
        services.AddScoped<InstallationRunRecoveryService>();
        services.AddHttpClient<InstallNpmAdminProbe>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        services.AddSingleton<InstallRunCoordinator>();
        services.AddSingleton<IInstallRunCoordinator>(sp =>
            sp.GetRequiredService<InstallRunCoordinator>());
        services.AddHostedService<InstallRunCoordinator>(sp =>
            sp.GetRequiredService<InstallRunCoordinator>());
        services.AddHostedService<InstallationRecoveryHostedService>();

        return services;
    }

    private static IServiceCollection AddSetupDomains(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSharedDomains(configuration);
        services.AddScoped<SetupPlatformCertificateService>();
        services.AddScoped<ISetupPlatformCertificateService>(sp =>
            sp.GetRequiredService<SetupPlatformCertificateService>());
        services.AddScoped<SetupDomainPlanService>();
        return services;
    }

    private static IServiceCollection AddSetupVerification(this IServiceCollection services)
    {
        services.AddScoped<VerificationReportService>();
        services.AddScoped<SetupHandoffService>();
        return services;
    }
}
