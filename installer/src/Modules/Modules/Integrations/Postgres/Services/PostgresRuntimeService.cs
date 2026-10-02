using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Postgres.Contracts;
using Modules.Integrations.Postgres.Mappers;
using Modules.Shared.RuntimeImages;

namespace Modules.Integrations.Postgres.Services;

public sealed class PostgresRuntimeService(
    IDockerHost dockerHost,
    RuntimePortPlanner portPlanner,
    MemDbContext db,
    IApprovedPostgresRuntimeProvider approvedPostgresRuntimeProvider,
    global::Shared.ControlPlane.Runtime.MemControlPlaneRuntimeContext? runtimeContext = null)
{
    private const int ContainerPort = 5432;
    private const string DefaultUsername = "postgres";
    private const string DefaultPassword = "postgres";
    private const string DefaultDatabase = "deltabox";

    public PostgresPlanResponse Plan(PostgresPlanRequest request)
    {
        ValidatePlanRequest(request);

        var warnings = new List<string>();
        var reservedPorts = new HashSet<int>();

        var portPlan = portPlanner.BuildPortPlan(
            containerPort: ContainerPort,
            preferredHostPort: request.PreferredHostPort,
            warnings: warnings,
            reservedPorts: reservedPorts);

        var plan = new RuntimeServicePlan(
            ServiceName: ManagedServiceNames.Postgres,
            ContainerName: ManagedContainerNames.Postgres,
            Image: approvedPostgresRuntimeProvider.GetPolicy().ApprovedReference,
            Ports: [portPlan],
            HostDataPath: request.HostDataPath,
            HostLetsEncryptPath: null,
            Warnings: warnings
        );

        return PostgresMappers.ToPlanResponse(plan);
    }

    public async Task<RuntimeActionResponse> DeployAsync(
        PostgresDeployRequest request,
        CancellationToken ct)
    {
        ValidateDeployRequest(request);

        var existingContainer = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        if (existingContainer is not null)
        {
            await UpsertRuntimeRecordFromContainerAsync(
                existingContainer,
                request.PreferredHostPort,
                request.HostDataPath,
                ct);

            return new RuntimeActionResponse(
                Success: true,
                Message: $"Container '{ManagedContainerNames.Postgres}' already exists. No deployment action was required.",
                Container: existingContainer
            );
        }

        await RemoveStaleRuntimeRecordIfPresentAsync(ct);

        var approvedPostgresRuntime = await approvedPostgresRuntimeProvider
            .ResolveForInstallationAsync(ct);

        var plan = Plan(new PostgresPlanRequest(
            PreferredHostPort: request.PreferredHostPort,
            HostDataPath: request.HostDataPath));

        var bindMounts = new List<DockerBindMount>();
        string? fullHostPath = null;

        if (!string.IsNullOrWhiteSpace(plan.HostDataPath))
        {
            fullHostPath = Path.GetFullPath(plan.HostDataPath.Trim());
            Directory.CreateDirectory(fullHostPath);

            bindMounts.Add(new DockerBindMount(
                HostPath: fullHostPath,
                ContainerPath: "/var/lib/postgresql/data"
            ));
        }

        var spec = new DockerContainerSpec(
            Name: plan.ContainerName,
            Image: approvedPostgresRuntime.ResolvedImageId,
            Environment: new Dictionary<string, string>
            {
                ["POSTGRES_USER"] = DefaultUsername,
                ["POSTGRES_PASSWORD"] = DefaultPassword,
                ["POSTGRES_DB"] = DefaultDatabase
            },
            Labels: ManagedContainerLabels.ForService(plan.ServiceName, runtimeContext),
            PortBindings: new Dictionary<string, string>
            {
                [$"{plan.ContainerPort}/tcp"] = plan.SelectedHostPort.ToString()
            },
            BindMounts: bindMounts,
            NetworkName: ManagedNetworkNames.DeltaboxAio,
            NetworkAliases: [ManagedNetworkAliases.Postgres]
        );

        var containerId = await dockerHost.CreateContainerAsync(spec, ct);

        try
        {
            await dockerHost.StartContainerAsync(containerId, ct);
        }
        catch (Exception ex)
        {
            await SafeRemoveContainerAsync(containerId, ct);

            return new RuntimeActionResponse(
                Success: false,
                Message: $"Failed to start container '{plan.ContainerName}': {ex.Message}",
                Container: null
            );
        }

        var inspection = await dockerHost.InspectByNameAsync(plan.ContainerName, ct);

        if (inspection is null)
        {
            await SafeRemoveContainerAsync(containerId, ct);

            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{plan.ContainerName}' was created and start was attempted, but post-start inspection returned null.",
                Container: null
            );
        }

        if (!inspection.Running)
        {
            await SaveRuntimeRecordAsync(
                inspection,
                request.PreferredHostPort,
                plan.SelectedHostPort,
                fullHostPath,
                ct);

            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{plan.ContainerName}' exists but is not running. Current state: {inspection.State ?? "Unknown"}.",
                Container: inspection
            );
        }

        await SaveRuntimeRecordAsync(
            inspection,
            request.PreferredHostPort,
            plan.SelectedHostPort,
            fullHostPath,
            ct);

        var message = plan.IsPreferredPortAvailable
            ? $"Container '{plan.ContainerName}' deployed successfully on host port {plan.SelectedHostPort}."
            : $"Container '{plan.ContainerName}' deployed successfully using fallback host port {plan.SelectedHostPort}.";

        return new RuntimeActionResponse(
            Success: true,
            Message: message,
            Container: inspection
        );
    }

    public async Task<RuntimeActionResponse> StartAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        if (container is null)
        {
            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{ManagedContainerNames.Postgres}' was not found.",
                Container: null
            );
        }

        if (container.Running)
        {
            return new RuntimeActionResponse(
                Success: true,
                Message: $"Container '{ManagedContainerNames.Postgres}' is already running.",
                Container: container
            );
        }

        await dockerHost.StartContainerAsync(container.Id, ct);

        var updated = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Postgres, ct);

        if (record is not null)
        {
            record.Status = updated?.State ?? "Running";
            record.ContainerId = updated?.Id;
            record.LastObservedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.Postgres}' started.",
            Container: updated
        );
    }

    public async Task<PostgresStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var record = await db.RuntimeServices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Postgres, ct);

        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        var warnings = new List<string>();

        if (record is not null && container is null)
            warnings.Add("Persisted runtime service exists, but Docker container was not found.");

        if (record is null && container is not null)
            warnings.Add("NPM container is running, but MEM has not recorded it in the runtime database yet. MEM can still use the configured NPM API URL for this setup step.");

        if (record is not null && container is not null)
        {
            var actualHostPort = container.Ports
                .FirstOrDefault(x => x.PrivatePort == ContainerPort)?.PublicPort;

            if (actualHostPort.HasValue && actualHostPort.Value != (uint)record.SelectedHostPort)
            {
                warnings.Add(
                    $"Persisted selected host port is {record.SelectedHostPort}, but Docker reports {actualHostPort.Value}.");
            }
        }

        return PostgresMappers.ToStatusResponse(
            record,
            container,
            ManagedServiceNames.Postgres,
            ManagedContainerNames.Postgres,
            warnings
        );
    }

    public async Task<RuntimeActionResponse> StopAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        if (container is null)
        {
            return new RuntimeActionResponse(
                Success: false,
                Message: $"Container '{ManagedContainerNames.Postgres}' was not found.",
                Container: null
            );
        }

        await dockerHost.StopContainerAsync(container.Id, ct);

        var updated = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Postgres, ct);

        if (record is not null)
        {
            record.Status = updated?.State ?? "Stopped";
            record.ContainerId = updated?.Id;
            record.LastObservedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.Postgres}' stopped.",
            Container: updated
        );
    }

    public async Task<RuntimeActionResponse> RemoveAsync(CancellationToken ct)
    {
        var container = await dockerHost.InspectByNameAsync(ManagedContainerNames.Postgres, ct);

        if (container is not null)
        {
            await dockerHost.RemoveContainerAsync(container.Id, true, false, ct);
        }

        var record = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Postgres, ct);

        if (record is not null)
        {
            db.RuntimeServices.Remove(record);
            await db.SaveChangesAsync(ct);
        }

        return new RuntimeActionResponse(
            Success: true,
            Message: $"Container '{ManagedContainerNames.Postgres}' removed.",
            Container: null
        );
    }

    private static void ValidatePlanRequest(PostgresPlanRequest request)
    {
        if (request.PreferredHostPort < 1 || request.PreferredHostPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(request.PreferredHostPort));
    }

    private static void ValidateDeployRequest(PostgresDeployRequest request)
    {
        if (request.PreferredHostPort < 1 || request.PreferredHostPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(request.PreferredHostPort));
    }

    private async Task RemoveStaleRuntimeRecordIfPresentAsync(CancellationToken ct)
    {
        var existingRecord = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Postgres, ct);

        if (existingRecord is null)
            return;

        db.RuntimeServices.Remove(existingRecord);
        await db.SaveChangesAsync(ct);
    }

    private async Task SaveRuntimeRecordAsync(
        DockerContainerInspection inspection,
        int preferredHostPort,
        int selectedHostPort,
        string? hostPath,
        CancellationToken ct)
    {
        var existingRecord = await db.RuntimeServices
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Postgres, ct);

        if (existingRecord is null)
        {
            db.RuntimeServices.Add(new RuntimeServiceEntity
            {
                Id = Guid.NewGuid(),
                ServiceName = ManagedServiceNames.Postgres,
                ContainerName = ManagedContainerNames.Postgres,
                Image = inspection.Image,
                ContainerPort = ContainerPort,
                PreferredHostPort = preferredHostPort,
                SelectedHostPort = selectedHostPort,
                HostPath = hostPath,
                SecondaryHostPath = null,
                ContainerId = inspection.Id,
                Status = inspection.Running ? "Running" : (inspection.State ?? "Unknown"),
                CreatedAtUtc = DateTime.UtcNow,
                LastObservedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existingRecord.ContainerName = ManagedContainerNames.Postgres;
            existingRecord.Image = inspection.Image;
            existingRecord.ContainerPort = ContainerPort;
            existingRecord.PreferredHostPort = preferredHostPort;
            existingRecord.SelectedHostPort = selectedHostPort;
            existingRecord.HostPath = hostPath;
            existingRecord.SecondaryHostPath = null;
            existingRecord.ContainerId = inspection.Id;
            existingRecord.Status = inspection.Running ? "Running" : (inspection.State ?? "Unknown");
            existingRecord.LastObservedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task UpsertRuntimeRecordFromContainerAsync(
        DockerContainerInspection inspection,
        int preferredHostPort,
        string? hostPath,
        CancellationToken ct)
    {
        var selectedHostPort = inspection.Ports
            .FirstOrDefault(x => x.PrivatePort == ContainerPort)?.PublicPort;

        await SaveRuntimeRecordAsync(
            inspection,
            preferredHostPort,
            selectedHostPort is null or 0 ? preferredHostPort : (int)selectedHostPort,
            hostPath,
            ct);
    }

    private async Task SafeRemoveContainerAsync(string containerId, CancellationToken ct)
    {
        try
        {
            await dockerHost.RemoveContainerAsync(containerId, true, true, ct);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}