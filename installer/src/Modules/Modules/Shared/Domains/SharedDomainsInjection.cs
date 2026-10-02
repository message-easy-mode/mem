using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Acme;
using Modules.Shared.Domains.Certificates.Npm;
using Modules.Shared.Domains.Dns;
using Modules.Shared.Domains.Issuance;
using Modules.Shared.Domains.Renewal;

namespace Modules.Shared.Domains;

public static class SharedDomainsInjection
{
    public static IServiceCollection AddSharedDomains(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CertificateStorageOptions>(
            configuration.GetSection("Certificates:Storage"));

        services.Configure<AcmeCertificateOptions>(
            configuration.GetSection("Certificates:Acme"));

        services.AddScoped<DomainRegistryService>();
        services.AddScoped<IDomainCertificateIssueExecutor, DomainCertificateIssueExecutor>();
        services.AddScoped<DomainCertificateIssueOrchestrator>();
        services.AddSingleton<DomainCertificateIssueWakeSignal>();
        services.AddHostedService<DomainCertificateIssueWorker>();
        services.AddScoped<IDomainSecretStore, ProtectedDomainSecretStore>();
        services.AddScoped<DomainCertificateRenewalService>();
        services.AddScoped<DomainCertificateRenewalProjectionService>();
        services.AddSingleton<DomainCertificateRenewalWakeSignal>();
        services.AddScoped<IDomainCertificateRenewalCandidateIssuer, DomainCertificateRenewalCandidateIssuer>();
        services.AddScoped<IDomainCertificateRenewalCandidateValidator, DomainCertificateRenewalCandidateValidator>();
        services.AddScoped<IDomainCertificateRenewalIngressActivator, NpmDomainCertificateRenewalIngressActivator>();
        services.AddScoped<IDomainCertificateRenewalCandidateActivator, DomainCertificateRenewalCandidateActivator>();
        services.AddScoped<DomainCertificateRenewalOrchestrator>();
        services.AddHostedService<DomainCertificateRenewalWorker>();

        services.AddScoped<CertificateService>();
        services.AddScoped<CertificateStorageService>();
        services.AddScoped<CertificateValidationService>();

        services.AddScoped<IDnsTxtQueryClient, DnsTxtQueryClient>();
        services.AddScoped<AuthoritativeDnsChallengeObserver>();
        services.AddScoped<IAuthoritativeDnsChallengeObserver>(sp =>
            sp.GetRequiredService<AuthoritativeDnsChallengeObserver>());
        services.AddScoped<DnsChallengeReadinessService>();
        services.AddScoped<AcmeCertificateIssuer>();
        services.AddScoped<NpmCertificateImportProbe>();
        services.AddScoped<INpmCertificateDeletionService, NpmCertificateDeletionService>();

        services.AddHttpClient<DesecDnsChallengeProvider>(client =>
        {
            client.BaseAddress = new Uri("https://desec.io/api/v1/");
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddScoped<IDnsChallengeProvider>(sp =>
            sp.GetRequiredService<DesecDnsChallengeProvider>());
        services.AddScoped<IDnsZoneAccessProbe>(sp =>
            sp.GetRequiredService<DesecDnsChallengeProvider>());

        return services;
    }
}