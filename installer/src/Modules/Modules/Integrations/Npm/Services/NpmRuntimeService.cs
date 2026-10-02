using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Mappers;

namespace Modules.Integrations.Npm.Services;

public sealed class NpmRuntimeService(
    IDockerHost dockerHost,
    RuntimePortPlanner portPlanner,
    MemDbContext db,
    global::Shared.ControlPlane.Runtime.MemControlPlaneRuntimeContext? runtimeContext = null,
    global::Shared.ControlPlane.Runtime.IMemManagedServiceAuthorityResolver? authorityResolver = null)
{
    public NpmPlanResponse Plan(NpmPlanRequest request)
    {
        var warnings = new List<string>();
        var reservedPorts = new HashSet<int>();

        var ports = new List<RuntimePortPlan>
        {
            portPlanner.BuildPortPlan(80, request.PreferredHttpPort, warnings, reservedPorts),
            portPlanner.BuildPortPlan(81, request.PreferredAdminPort, warnings, reservedPorts),
            portPlanner.BuildPortPlan(443, request.PreferredHttpsPort, warnings, reservedPorts)
        };

        var plan = new RuntimeServicePlan(
            ServiceName: ManagedServiceNames.Npm,
            ContainerName: ManagedContainerNames.Npm,
            Image: NpmRuntimeRelease.ApprovedImage,
            Ports: ports,
            HostDataPath: request.HostDataPath,
            HostLetsEncryptPath: request.HostLetsEncryptPath,
            Warnings: warnings
        );

        return NpmMappers.ToPlanResponse(plan);
    }

    public async Task<RuntimeActionResponse> DeployAsync(NpmDeployRequest request, CancellationToken ct)
    {
        var existing = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        if (existing is not null)
        {
            return new RuntimeActionResponse(
                Success: true,
                Message: $"Container '{ManagedContainerNames.Npm}' already exists. No deployment action was required.",
                Container: existing
            );
        }

        var existingRecord = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, ct);

        if (existingRecord is not null)
        {
            return new RuntimeActionResponse(
                Success: true,
                Message: $"Runtime service '{ManagedServiceNames.Npm}' already exists in persisted state. No deployment action was required.",
                Container: null
            );
        }

        var plan = Plan(new NpmPlanRequest(
            request.PreferredHttpPort,
            request.PreferredAdminPort,
            request.PreferredHttpsPort,
            request.HostDataPath,
            request.HostLetsEncryptPath));

        var bindMounts = new List<DockerBindMount>();

        string? fullDataPath = null;
        string? fullLetsEncryptPath = null;

        if (!string.IsNullOrWhiteSpace(plan.HostDataPath))
        {
            fullDataPath = Path.GetFullPath(plan.HostDataPath.Trim());
            Directory.CreateDirectory(fullDataPath);

            bindMounts.Add(new DockerBindMount(fullDataPath, "/data"));
        }

        if (!string.IsNullOrWhiteSpace(plan.HostLetsEncryptPath))
        {
            fullLetsEncryptPath = Path.GetFullPath(plan.HostLetsEncryptPath.Trim());
            Directory.CreateDirectory(fullLetsEncryptPath);

            bindMounts.Add(new DockerBindMount(fullLetsEncryptPath, "/etc/letsencrypt"));
        }

        var portBindings = request.ForcePreferredPorts
            ? new Dictionary<string, string>
            {
                ["80/tcp"] = request.PreferredHttpPort.ToString(),
                ["81/tcp"] = request.PreferredAdminPort.ToString(),
                ["443/tcp"] = request.PreferredHttpsPort.ToString()
            }
            : plan.Ports.ToDictionary(
                x => $"{x.ContainerPort}/{x.Protocol}",
                x => x.SelectedHostPort.ToString());

        var spec = new DockerContainerSpec(
            Name: plan.ContainerName,
            Image: plan.Image,
            Labels: ManagedContainerLabels.ForService(plan.ServiceName, runtimeContext),
            PortBindings: portBindings,
            BindMounts: bindMounts,
            NetworkName: ManagedNetworkNames.DeltaboxAio,
            NetworkAliases: [ManagedNetworkAliases.Npm]
        );

        var containerId = await dockerHost.CreateContainerAsync(spec, ct);

        try
        {
            await dockerHost.StartContainerAsync(containerId, ct);
        }
        catch (Exception ex)
        {
            await dockerHost.RemoveContainerAsync(containerId, true, true, ct);

            return new RuntimeActionResponse(
                Success: false,
                Message: $"Failed to start container '{plan.ContainerName}': {ex.Message}",
                Container: null
            );
        }

        var inspection = await dockerHost.InspectByNameAsync(plan.ContainerName, ct);

        var effectiveAdminPort = request.ForcePreferredPorts
            ? request.PreferredAdminPort
            : plan.Ports.Single(x => x.ContainerPort == 81).SelectedHostPort;

        var effectiveHttpPort = request.ForcePreferredPorts
            ? request.PreferredHttpPort
            : plan.Ports.Single(x => x.ContainerPort == 80).SelectedHostPort;

        var effectiveHttpsPort = request.ForcePreferredPorts
            ? request.PreferredHttpsPort
            : plan.Ports.Single(x => x.ContainerPort == 443).SelectedHostPort;

        db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = plan.ServiceName,
            ContainerName = plan.ContainerName,
            Image = plan.Image,
            ContainerPort = 81,
            PreferredHostPort = request.PreferredAdminPort,
            SelectedHostPort = effectiveAdminPort,
            HostPath = fullDataPath,
            SecondaryHostPath = fullLetsEncryptPath,
            ContainerId = inspection?.Id,
            Status = inspection?.Running == true ? "Running" : (inspection?.State ?? "Unknown"),
            CreatedAtUtc = DateTime.UtcNow,
            LastObservedAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        var message = request.ForcePreferredPorts
            ? $"Container '{plan.ContainerName}' deployed successfully using forced preferred ports. HTTP={effectiveHttpPort}, Admin={effectiveAdminPort}, HTTPS={effectiveHttpsPort}."
            : $"Container '{plan.ContainerName}' deployed successfully. HTTP={effectiveHttpPort}, Admin={effectiveAdminPort}, HTTPS={effectiveHttpsPort}.";

        return new RuntimeActionResponse(
            Success: true,
            Message: message,
            Container: inspection
        );
    }

    public async Task<NpmStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var record = await db.RuntimeServices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, ct);

        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        var warnings = new List<string>();

        if (record is not null && container is null)
            warnings.Add("Persisted runtime service exists, but Docker container was not found.");

        if (record is null && container is not null)
            warnings.Add("NPM container is running, but MEM has not recorded it in the runtime database yet. MEM can still use the configured NPM API URL for this setup step.");

        return NpmMappers.ToStatusResponse(
            record,
            container,
            ManagedServiceNames.Npm,
            ManagedContainerNames.Npm,
            warnings
        );
    }

    public async Task<RuntimeActionResponse> StartAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        if (container is null)
        {
            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{ManagedContainerNames.Npm}' was not found.",
                Container: null
            );
        }

        if (container.Running)
        {
            return new RuntimeActionResponse(
                Success: true,
                Message: $"Container '{ManagedContainerNames.Npm}' is already running.",
                Container: container
            );
        }

        await dockerHost.StartContainerAsync(container.Id, ct);

        var updated = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, ct);

        if (record is not null)
        {
            record.Status = updated?.State ?? "Running";
            record.ContainerId = updated?.Id;
            record.LastObservedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.Npm}' started.",
            Container: updated
        );
    }

    public async Task<RuntimeActionResponse> StopAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        if (container is null)
        {
            return new RuntimeActionResponse(false, $"Container '{ManagedContainerNames.Npm}' was not found.", null);
        }

        await dockerHost.StopContainerAsync(container.Id, ct);

        var updated = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, ct);

        if (record is not null)
        {
            record.Status = updated?.State ?? "Stopped";
            record.ContainerId = updated?.Id;
            record.LastObservedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.Npm}' stopped.",
            Container: updated
        );
    }

    public async Task<RuntimeActionResponse> RemoveAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Npm, ct);

        if (container is not null)
        {
            await dockerHost.RemoveContainerAsync(container.Id, true, false, ct);
        }

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, ct);

        if (record is not null)
        {
            db.RuntimeServices.Remove(record);
            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.Npm}' removed.",
            Container: null
        );
    }

    public async Task<string> GetApiBaseUrlAsync(CancellationToken ct)
    {
        var runtime = await db.RuntimeServices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, ct);

        if (runtime is null)
            throw new InvalidOperationException("NPM runtime record was not found.");

        if (runtime.SelectedHostPort <= 0)
            throw new InvalidOperationException("NPM runtime record does not have a valid selected admin port.");

        if (authorityResolver is null || runtimeContext is null)
            throw new InvalidOperationException("The server-owned managed-service authority resolver is unavailable.");

        var authority = authorityResolver.Resolve(
            global::Shared.ControlPlane.Runtime.MemManagedServicePurposes.Administration,
            new global::Shared.ControlPlane.Runtime.MemManagedServiceAuthoritySource(
                ServiceName: ManagedServiceNames.Npm,
                PublishedHostPort: runtime.SelectedHostPort,
                DockerNetworkAlias: ManagedNetworkAliases.Npm,
                HealthPort: 81,
                AdministrationPort: 81,
                IngestionPort: null));

        return new Uri(authority.Authority, "/api").ToString().TrimEnd('/');
    }
}