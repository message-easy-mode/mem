using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Infrastructure.Docker;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;

using Modules.Integrations.Npm;
using Modules.Integrations.Postgres.Services;
using Modules.Integrations.Portainer.Contracts;
using Modules.Integrations.Portainer.Services;
using Modules.Integrations.Seq.Services;
using Modules.Integrations.PgAdmin.Services;
using Modules.Integrations.Docker.Services;
using Modules.Shared.RuntimeImages;

namespace Modules.Integrations;

public static class IntegrationsInjection
{
    public static IServiceCollection AddIntegrations(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<DockerDebugService>();

        services.AddScoped<Core.Runtime.PortCheckService>();
        services.AddScoped<Core.Runtime.RuntimePortPlanner>();

        services.AddNpmModule(configuration);

        services.AddScoped<PostgresRuntimeService>();

        var portainerOptions = new PortainerRuntimeOptions();
        configuration
            .GetSection(PortainerRuntimeOptions.SectionName)
            .Bind(portainerOptions);
        PortainerRuntimeOptionsValidator.ThrowIfInvalid(portainerOptions);
        services.AddSingleton(portainerOptions);
        services.AddSingleton<IPortainerHostArchitectureReader, PortainerHostArchitectureReader>();
        services.AddScoped<PortainerRuntimeImageProvider>();
        services.AddScoped<PortainerRuntimeService>();
        services.AddScoped<IPortainerRuntimeStatusReader>(serviceProvider =>
            serviceProvider.GetRequiredService<PortainerRuntimeService>());

        var seqOptions = new SeqDiagnosticsOptions();
        configuration
            .GetSection(SeqDiagnosticsOptions.SectionName)
            .Bind(seqOptions);
        services.AddSingleton(seqOptions);
        services.AddSingleton(serviceProvider =>
            SeqRuntimeContextProfile.Create(
                serviceProvider.GetRequiredService<MemControlPlaneRuntimeContext>(),
                serviceProvider.GetRequiredService<SeqDiagnosticsOptions>()));
        services.TryAddSingleton(new SeqLoggingRuntimeState(
            seqOptions.SinkEnabled,
            WarningCode: null));
        services.AddSingleton<SeqSecretResolver>();
        services.AddSingleton<SeqSecretFileWriter>();
        services.AddSingleton<SeqBootstrapStateStore>();
        services.AddSingleton<SeqBootstrapReviewStore>();
        services.AddSingleton<ISeqBootstrapStateStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SeqBootstrapStateStore>());
        services.AddSingleton(serviceProvider =>
            new SeqEffectiveConfigurationProvider(
                serviceProvider.GetRequiredService<SeqDiagnosticsOptions>(),
                serviceProvider.GetRequiredService<ISeqBootstrapStateStore>(),
                serviceProvider.GetRequiredService<MemControlPlaneRuntimeContext>()));
        services.AddSingleton<SeqDeliveryStateStore>();
        services.AddSingleton<ISeqDeliveryStateStore>(serviceProvider =>
            serviceProvider.GetRequiredService<SeqDeliveryStateStore>());
        services.AddSingleton(serviceProvider =>
            SeqDeliveryProcessIdentity.FromRuntimeContext(
                serviceProvider.GetRequiredService<MemControlPlaneRuntimeContext>()));
        services.AddSingleton<SeqHealthState>();
        services.AddSingleton<ISeqHealthReader>(serviceProvider =>
            serviceProvider.GetRequiredService<SeqHealthState>());
        services.AddHostedService<SeqHealthProbeService>();
        services.AddScoped<SeqRuntimeImageResolver>();
        services.AddScoped<SeqBootstrapStorageInspector>();
        services.TryAddScoped<DockerIsolatedStandardInputRunner>();
        services.AddScoped<IDockerIsolatedStandardInputRunner,
            ExclusiveControlPlaneDockerIsolatedStandardInputRunner>();
        services.AddScoped<SeqPasswordHashService>();
        services.AddScoped<ISeqAdministrationClientFactory, SeqApiAdministrationClientFactory>();
        services.AddScoped<ISeqConnectionProvisioner, SeqConnectionProvisioner>();
        services.AddScoped<ISeqRuntimeHealthVerifier, SeqRuntimeHealthVerifier>();
        services.AddScoped<SeqRuntimeService>();
        services.AddScoped<ISeqRuntimeStatusReader>(serviceProvider =>
            serviceProvider.GetRequiredService<SeqRuntimeService>());
        services.TryAddScoped<IRuntimeImageInspector, DockerRuntimeImageInspector>();
        services.AddHttpClient("seq-probe");
        services.AddHttpClient("seq-ingestion-verification", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
        });

        services.AddScoped<PgAdminRuntimeService>();

        services.AddSingleton<IMemManagedServiceAuthorityResolver,
            MemManagedServiceAuthorityResolver>();
        services.TryAddScoped<DockerHost>();
        services.AddScoped<IControlPlaneContainerInventory, DockerControlPlaneContainerInventory>();
        services.AddScoped<IControlPlaneContainerInspector, DockerControlPlaneContainerInspector>();
        services.AddScoped<IControlPlaneExposureInspector, ControlPlaneExposureService>();
        services.AddScoped<IControlPlaneDockerOwnershipGuard,
            ControlPlaneDockerOwnershipService>();
        // This is intentionally the final IDockerHost registration. HostAgent
        // may register the raw adapter earlier, but all application mutations
        // must pass through the exclusive Control Plane ownership boundary.
        services.AddScoped<IDockerHost, ExclusiveControlPlaneDockerHost>();

        services.AddHttpClient("pgadmin-probe", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }
}
