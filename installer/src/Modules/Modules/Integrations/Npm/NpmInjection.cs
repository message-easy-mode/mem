using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace Modules.Integrations.Npm;

public static class NpmInjection
{
    public static IServiceCollection AddNpmModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NpmApiOptions>(
            configuration.GetSection("Npm"));

        services.AddMemoryCache();

        services.AddScoped<INpmTokenProvider, NpmTokenProvider>();

        services.AddScoped<NpmApiBaseUrlResolver>();
        services.AddScoped<NpmReadinessService>();

        services.AddScoped<NpmRuntimeService>();
        services.AddScoped<NpmProxyHostService>();
        services.AddScoped<NpmCertificateLogSecretBoundaryService>();

        services.AddHttpClient<NpmApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient("npm-probe", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}