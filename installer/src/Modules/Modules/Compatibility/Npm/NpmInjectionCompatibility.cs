using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Modules.Npm;

[Obsolete("Use Modules.Integrations.Npm.NpmInjection.AddNpmModule or AddIntegrationsApplication instead. This compatibility shim can be removed after host startup is updated.")]
public static class NpmInjection
{
    public static IServiceCollection AddNpmModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return Modules.Integrations.Npm.NpmInjection.AddNpmModule(services, configuration);
    }
}
