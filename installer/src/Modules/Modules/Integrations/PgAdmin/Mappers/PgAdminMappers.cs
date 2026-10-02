using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker.Models;
using Modules.Integrations.PgAdmin.Contracts;

namespace Modules.Integrations.PgAdmin.Mappers;

public static class PgAdminMappers
{
    public static PgAdminPlanResponse ToPlanResponse(RuntimeServicePlan plan)
    {
        var portPlan = plan.Ports.Single();

        return new PgAdminPlanResponse(
            ServiceName: plan.ServiceName,
            ContainerName: plan.ContainerName,
            Image: plan.Image,
            ContainerPort: portPlan.ContainerPort,
            PreferredHostPort: portPlan.PreferredHostPort,
            SelectedHostPort: portPlan.SelectedHostPort,
            IsPreferredPortAvailable: portPlan.IsPreferredPortAvailable,
            VolumeName: ManagedVolumeNames.PgAdminData,
            Warnings: plan.Warnings
        );
    }

    public static PgAdminStatusResponse ToStatusResponse(
        RuntimeServiceEntity? record,
        DockerContainerInspection? container,
        string fallbackServiceName,
        string fallbackContainerName,
        IReadOnlyList<string> warnings)
    {
        var uiHostPort = container?.Ports
            .FirstOrDefault(x => x.PrivatePort == 80)?.PublicPort;

        return new PgAdminStatusResponse(
            ServiceName: record?.ServiceName ?? fallbackServiceName,
            ContainerName: record?.ContainerName ?? fallbackContainerName,
            VolumeName: ManagedVolumeNames.PgAdminData,
            UiHostPort: uiHostPort is 0 or null ? null : (int?)uiHostPort,
            Exists: container is not null,
            Running: container?.Running ?? false,
            State: container?.State,
            Container: container,
            Warnings: warnings
        );
    }
}