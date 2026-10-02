using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.PgAdmin.Contracts;
using Modules.Integrations.PgAdmin.Mappers;

namespace Modules.Integrations.PgAdmin.Services;

public sealed class PgAdminRuntimeService(
    IDockerHost dockerHost,
    RuntimePortPlanner portPlanner,
    MemDbContext db,
    global::Shared.ControlPlane.Runtime.MemControlPlaneRuntimeContext? runtimeContext = null)
{
    private const string ImageName = "dpage/pgadmin4:latest";
    private const int ContainerPort = 80;
    private const string PgAdminDataPath = "/var/lib/pgadmin";

    public PgAdminPlanResponse Plan(PgAdminPlanRequest request)
    {
        if (request.PreferredHostPort < 1 || request.PreferredHostPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(request.PreferredHostPort));

        var warnings = new List<string>();
        var reservedPorts = new HashSet<int>();

        var portPlan = portPlanner.BuildPortPlan(
            containerPort: ContainerPort,
            preferredHostPort: request.PreferredHostPort,
            warnings: warnings,
            reservedPorts: reservedPorts);

        var plan = new RuntimeServicePlan(
            ServiceName: ManagedServiceNames.PgAdmin,
            ContainerName: ManagedContainerNames.PgAdmin,
            Image: ImageName,
            Ports: [portPlan],
            HostDataPath: null,
            HostLetsEncryptPath: null,
            Warnings: warnings
        );

        return PgAdminMappers.ToPlanResponse(plan);
    }

    public async Task<RuntimeActionResponse> DeployAsync(PgAdminDeployRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AdminEmail))
            throw new ArgumentException("Admin email is required.", nameof(request.AdminEmail));

        if (string.IsNullOrWhiteSpace(request.AdminPassword))
            throw new ArgumentException("Admin password is required.", nameof(request.AdminPassword));

        var existing = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        if (existing is not null)
        {
            await UpsertRuntimeRecordFromContainerAsync(existing, request.PreferredHostPort, ct);

            return new RuntimeActionResponse(
                Success: true,
                Message: $"Container '{ManagedContainerNames.PgAdmin}' already exists. No deployment action was required.",
                Container: existing
            );
        }

        var plan = Plan(new PgAdminPlanRequest(request.PreferredHostPort));

        var selectedHostPort = request.ForcePreferredPort
            ? request.PreferredHostPort
            : plan.SelectedHostPort;

        var spec = new DockerContainerSpec(
            Name: plan.ContainerName,
            Image: plan.Image,
            Environment: new Dictionary<string, string>
            {
                ["PGADMIN_DEFAULT_EMAIL"] = request.AdminEmail,
                ["PGADMIN_DEFAULT_PASSWORD"] = request.AdminPassword,
                ["PGADMIN_CONFIG_SERVER_MODE"] = "False"
            },
            Labels: ManagedContainerLabels.ForService(plan.ServiceName, runtimeContext),
            PortBindings: new Dictionary<string, string>
            {
                ["80/tcp"] = selectedHostPort.ToString()
            },
            VolumeMounts: new List<DockerVolumeMount>
            {
                new(
                    VolumeName: ManagedVolumeNames.PgAdminData,
                    ContainerPath: PgAdminDataPath
                )
            },
            NetworkName: ManagedNetworkNames.DeltaboxAio,
            NetworkAliases: [ManagedNetworkAliases.PgAdmin]
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

        if (inspection is not null)
        {
            await UpsertRuntimeRecordFromContainerAsync(
                inspection,
                request.PreferredHostPort,
                ct
            );
        }

        var message = request.ForcePreferredPort
            ? $"Container '{plan.ContainerName}' deployed successfully using forced preferred host port {selectedHostPort}."
            : $"Container '{plan.ContainerName}' deployed successfully on host port {selectedHostPort}.";

        return new RuntimeActionResponse(
            Success: true,
            Message: message,
            Container: inspection
        );
    }

    public async Task<PgAdminStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var record = await db.RuntimeServices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.PgAdmin, ct);

        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        var warnings = new List<string>();

        if (record is not null && container is null)
            warnings.Add("Persisted runtime service exists, but Docker container was not found.");

        if (record is null && container is not null)
            warnings.Add("Docker container exists, but no persisted runtime service record was found.");

        return PgAdminMappers.ToStatusResponse(
            record,
            container,
            ManagedServiceNames.PgAdmin,
            ManagedContainerNames.PgAdmin,
            warnings
        );
    }

    public async Task<RuntimeActionResponse> StartAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        if (container is null)
        {
            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{ManagedContainerNames.PgAdmin}' was not found.",
                Container: null
            );
        }

        await dockerHost.StartContainerAsync(container.Id, ct);

        var updated = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        if (updated is not null)
        {
            await UpsertRuntimeRecordFromContainerAsync(
                updated,
                preferredHostPortFallback: GetPublishedPortOrFallback(updated, 80, 0),
                ct
            );
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.PgAdmin}' started.",
            Container: updated
        );
    }

    public async Task<RuntimeActionResponse> StopAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        if (container is null)
        {
            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{ManagedContainerNames.PgAdmin}' was not found.",
                Container: null
            );
        }

        await dockerHost.StopContainerAsync(container.Id, ct);

        var updated = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        if (updated is not null)
        {
            await UpsertRuntimeRecordFromContainerAsync(
                updated,
                preferredHostPortFallback: GetPublishedPortOrFallback(updated, 80, 0),
                ct
            );
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.PgAdmin}' stopped.",
            Container: updated
        );
    }

    public async Task<RuntimeActionResponse> RemoveAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.PgAdmin, ct);

        if (container is not null)
        {
            await dockerHost.RemoveContainerAsync(container.Id, true, false, ct);
        }

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.PgAdmin, ct);

        if (record is not null)
        {
            db.RuntimeServices.Remove(record);
            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.PgAdmin}' removed.",
            Container: null
        );
    }

    private async Task UpsertRuntimeRecordFromContainerAsync(
        DockerContainerInspection inspection,
        int preferredHostPortFallback,
        CancellationToken ct)
    {
        var selectedHostPort = GetPublishedPortOrFallback(
            inspection,
            80,
            preferredHostPortFallback
        );

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.PgAdmin, ct);

        if (record is null)
        {
            db.RuntimeServices.Add(new RuntimeServiceEntity
            {
                Id = Guid.NewGuid(),
                ServiceName = ManagedServiceNames.PgAdmin,
                ContainerName = ManagedContainerNames.PgAdmin,
                Image = ImageName,
                ContainerPort = ContainerPort,
                PreferredHostPort = preferredHostPortFallback,
                SelectedHostPort = selectedHostPort,
                HostPath = null,
                SecondaryHostPath = ManagedVolumeNames.PgAdminData,
                ContainerId = inspection.Id,
                Status = inspection.Running ? "Running" : (inspection.State ?? "Unknown"),
                CreatedAtUtc = DateTime.UtcNow,
                LastObservedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            record.Image = inspection.Image;
            record.ContainerId = inspection.Id;
            record.Status = inspection.Running ? "Running" : (inspection.State ?? "Unknown");
            record.SelectedHostPort = selectedHostPort;
            record.SecondaryHostPath = ManagedVolumeNames.PgAdminData;
            record.LastObservedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private static int GetPublishedPortOrFallback(
    DockerContainerInspection inspection,
    int privatePort,
    int fallback)
    {
        var publicPort = inspection.Ports
            .FirstOrDefault(x => x.PrivatePort == privatePort)?
            .PublicPort;

        return publicPort.HasValue ? (int)publicPort.Value : fallback;
    }
}