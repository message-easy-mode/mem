using Core.Runtime;
using Infrastructure.Data.Entities;
using Infrastructure.Docker.Models;
using Modules.Integrations.Postgres.Contracts;

namespace Modules.Integrations.Postgres.Mappers;

public static class PostgresMappers
{
    public static PostgresPlanResponse ToPlanResponse(RuntimeServicePlan plan)
    {
        var portPlan = plan.Ports.Single();

        return new PostgresPlanResponse(
            ServiceName: plan.ServiceName,
            ContainerName: plan.ContainerName,
            Image: plan.Image,
            ContainerPort: portPlan.ContainerPort,
            PreferredHostPort: portPlan.PreferredHostPort,
            SelectedHostPort: portPlan.SelectedHostPort,
            IsPreferredPortAvailable: portPlan.IsPreferredPortAvailable,
            HostDataPath: plan.HostDataPath,
            Warnings: plan.Warnings
        );
    }

    public static PostgresStatusResponse ToStatusResponse(
        RuntimeServiceEntity? record,
        DockerContainerInspection? container,
        string fallbackServiceName,
        string fallbackContainerName,
        IReadOnlyList<string> warnings)
    {
        return new PostgresStatusResponse(
            ServiceName: record?.ServiceName ?? fallbackServiceName,
            ContainerName: record?.ContainerName ?? fallbackContainerName,
            PreferredHostPort: record?.PreferredHostPort,
            SelectedHostPort: record?.SelectedHostPort,
            HostDataPath: record?.HostPath,
            Exists: container is not null,
            Running: container?.Running ?? false,
            State: container?.State,
            Container: container,
            Warnings: warnings
        );
    }
}