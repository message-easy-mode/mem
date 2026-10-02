using Core.Runtime;
using Infrastructure.Data.Entities;
using Infrastructure.Docker.Models;
using Modules.Integrations.Npm.Contracts;

namespace Modules.Integrations.Npm.Mappers;

public static class NpmMappers
{
    public static NpmPlanResponse ToPlanResponse(RuntimeServicePlan plan)
    {
        return new NpmPlanResponse(
            ServiceName: plan.ServiceName,
            ContainerName: plan.ContainerName,
            Image: plan.Image,
            Ports: plan.Ports
                .Select(x => new NpmPortPlanResponse(
                    ContainerPort: x.ContainerPort,
                    PreferredHostPort: x.PreferredHostPort,
                    SelectedHostPort: x.SelectedHostPort,
                    IsPreferredPortAvailable: x.IsPreferredPortAvailable,
                    Protocol: x.Protocol,
                    Warnings: x.Warnings
                ))
                .ToList(),
            HostDataPath: plan.HostDataPath,
            HostLetsEncryptPath: plan.HostLetsEncryptPath,
            Warnings: plan.Warnings
        );
    }

    public static NpmStatusResponse ToStatusResponse(
        RuntimeServiceEntity? record,
        DockerContainerInspection? container,
        string fallbackServiceName,
        string fallbackContainerName,
        IReadOnlyList<string> warnings)
    {
        uint? FindPort(uint privatePort) =>
            container?.Ports.FirstOrDefault(x => x.PrivatePort == privatePort)?.PublicPort;

        var httpHostPort = FindPort(80);
        var adminHostPort = FindPort(81);
        var httpsHostPort = FindPort(443);

        return new NpmStatusResponse(
            ServiceName: record?.ServiceName ?? fallbackServiceName,
            ContainerName: record?.ContainerName ?? fallbackContainerName,
            HostDataPath: record?.HostPath,
            HostLetsEncryptPath: record?.SecondaryHostPath,
            HttpHostPort: httpHostPort is 0 or null ? null : (int?)httpHostPort,
            AdminHostPort: adminHostPort is 0 or null ? null : (int?)adminHostPort,
            HttpsHostPort: httpsHostPort is 0 or null ? null : (int?)httpsHostPort,
            Exists: container is not null,
            Running: container?.Running ?? false,
            State: container?.State,
            Container: container,
            Warnings: warnings
        );
    }
}