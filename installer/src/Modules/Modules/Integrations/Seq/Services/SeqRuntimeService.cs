using Microsoft.AspNetCore.Http;
using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Mappers;

namespace Modules.Integrations.Seq.Services;

public interface ISeqRuntimeStatusReader
{
    Task<SeqStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}

public sealed class SeqRuntimeService(
    IDockerHost dockerHost,
    RuntimePortPlanner portPlanner,
    MemDbContext db,
    SeqDiagnosticsOptions options,
    SeqSecretResolver secrets,
    SeqRuntimeImageResolver imageResolver,
    SeqEffectiveConfigurationProvider? effectiveConfigurationProvider = null,
    IHostEnvironment? environment = null,
    global::Shared.ControlPlane.Runtime.MemControlPlaneRuntimeContext? runtimeContext = null,
    SeqRuntimeContextProfile? runtimeProfile = null) : ISeqRuntimeStatusReader
{
    private SeqRuntimeContextProfile RuntimeProfile =>
        runtimeProfile ??
        (runtimeContext is null
            ? SeqRuntimeContextProfile.CreateLegacy(options)
            : SeqRuntimeContextProfile.Create(runtimeContext, options));

    public SeqPlanResponse Plan(SeqPlanRequest request)
    {
        var effectiveOptions = GetEffectiveOptions();
        var profile = RuntimeProfile;
        var preferredHostPort = request.PreferredHostPort ??
                                ResolvePreferredHostPort(effectiveOptions, profile);
        if (preferredHostPort is < 1 or > 65535)
        {
            throw new SeqOperationException(
                "seq_host_port_invalid",
                StatusCodes.Status400BadRequest,
                "The preferred Seq host port is invalid.");
        }

        var warnings = new List<string>
        {
            "Seq remains optional. MEM's local recorder and Diagnostics workspace stay authoritative.",
            "MEM does not publish Seq through NPM. Protect the host port with a firewall, VPN, or SSH tunnel."
        };
        var reservedPorts = new HashSet<int>();
        var portPlan = portPlanner.BuildPortPlan(
            containerPort: 80,
            preferredHostPort: preferredHostPort,
            warnings: warnings,
            reservedPorts: reservedPorts);

        var plan = new RuntimeServicePlan(
            ServiceName: ManagedServiceNames.Seq,
            ContainerName: profile.ContainerName,
            Image: effectiveOptions.ApprovedImageReference,
            Ports: [portPlan],
            HostDataPath: ResolveHostDataPath(profile),
            HostLetsEncryptPath: null,
            Warnings: warnings);

        return SeqMappers.ToPlanResponse(
            plan,
            effectiveOptions.ApprovedImageReference,
            effectiveOptions.ExpectedVersion);
    }

    public async Task<RuntimeActionResponse> DeployAsync(
        SeqDeployRequest request,
        CancellationToken ct)
    {
        var effectiveOptions = GetEffectiveOptions();
        SeqDiagnosticsOptionsValidator.ThrowIfInvalidForManagement(effectiveOptions);

        var adminPasswordHash = secrets.ResolveAdminPasswordHash(effectiveOptions);
        if (!adminPasswordHash.Available)
        {
            throw new SeqOperationException(
                "seq_admin_password_hash_unavailable",
                StatusCodes.Status409Conflict,
                "The Seq administrator password hash is not available from the configured secret source.");
        }

        var profile = RuntimeProfile;
        var existing = await dockerHost.InspectByNameAsync(
            profile.ContainerName,
            ct);
        var persistedRecord = await FindPersistedRuntimeRecordAsync(
            tracking: true,
            ct);
        var activeRecord = RecordMatchesProfile(persistedRecord, profile)
            ? persistedRecord
            : null;
        var inactiveContextRecord = profile.ContextScoped &&
                                    persistedRecord is not null &&
                                    activeRecord is null
            ? persistedRecord
            : null;

        if (existing is not null)
        {
            EnsureManagedOwnership(activeRecord, existing);
            return Success(
                $"Container '{profile.ContainerName}' already exists and remains MEM-managed. No deployment action was required.",
                existing);
        }

        if (activeRecord is not null)
        {
            throw new SeqOperationException(
                "seq_runtime_record_conflict",
                StatusCodes.Status409Conflict,
                "A persisted Seq runtime record already exists for this Control Plane context but its managed container was not found.");
        }

        if (persistedRecord is not null && !profile.ContextScoped)
        {
            throw new SeqOperationException(
                "seq_runtime_record_conflict",
                StatusCodes.Status409Conflict,
                "A persisted Seq runtime record already exists but does not match the configured managed runtime identity.");
        }

        // Development contexts have separate state boundaries, but an upgraded
        // repository-local database can still contain the single legacy Seq
        // runtime row from before context scoping. Keep that row untouched until
        // the new active-context container is created and verified, then reuse
        // the row for the active context. The foreign Docker runtime is never
        // started, stopped, relabelled, removed, or otherwise adopted.

        var runtimeImage = await imageResolver.ResolveForOperationAsync(ct);
        var plan = Plan(new SeqPlanRequest(request.PreferredHostPort));
        var fullHostPath = ResolveHostDataPath(profile);
        Directory.CreateDirectory(fullHostPath);

        var selectedHostPort = request.ForcePreferredPort
            ? plan.PreferredHostPort
            : plan.SelectedHostPort;

        var environment = new Dictionary<string, string>
        {
            ["ACCEPT_EULA"] = "Y",
            ["SEQ_FIRSTRUN_ADMINPASSWORDHASH"] = adminPasswordHash.Value!,
            ["SEQ_API_INGESTIONREQUIREAUTHENTICATION"] = "true"
        };
        if (SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                effectiveOptions.UiUrl,
                out var uiUri) &&
            uiUri is not null)
        {
            environment["SEQ_API_CANONICALURI"] = uiUri.ToString();
        }

        var spec = new DockerContainerSpec(
            Name: plan.ContainerName,
            Image: runtimeImage.ResolvedImageId,
            Environment: environment,
            Labels: ManagedContainerLabels.ForService(plan.ServiceName, runtimeContext),
            PortBindings: new Dictionary<string, string>
            {
                ["80/tcp"] = selectedHostPort.ToString()
            },
            BindMounts:
            [
                new DockerBindMount(
                    HostPath: fullHostPath,
                    ContainerPath: "/data")
            ],
            NetworkName: ManagedNetworkNames.DeltaboxAio,
            NetworkAliases: [profile.DockerNetworkAlias]);

        var containerId = await dockerHost.CreateContainerAsync(spec, ct);
        try
        {
            await dockerHost.StartContainerAsync(containerId, ct);
        }
        catch
        {
            await dockerHost.RemoveContainerAsync(
                containerId,
                force: true,
                removeVolumes: false,
                ct);
            throw;
        }

        var inspection = await dockerHost.InspectByNameAsync(
            plan.ContainerName,
            ct);
        if (inspection?.Running != true)
        {
            await dockerHost.RemoveContainerAsync(
                containerId,
                force: true,
                removeVolumes: false,
                ct);
            throw new SeqOperationException(
                "seq_start_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "Docker created Seq but the container did not reach a running state.");
        }

        var now = DateTime.UtcNow;
        if (inactiveContextRecord is null)
        {
            db.RuntimeServices.Add(new RuntimeServiceEntity
            {
                Id = Guid.NewGuid(),
                ServiceName = plan.ServiceName,
                ContainerName = plan.ContainerName,
                Image = runtimeImage.ResolvedImageId,
                ContainerPort = plan.ContainerPort,
                PreferredHostPort = plan.PreferredHostPort,
                SelectedHostPort = selectedHostPort,
                HostPath = fullHostPath,
                SecondaryHostPath = null,
                ContainerId = inspection.Id,
                Status = inspection.State,
                CreatedAtUtc = now,
                LastObservedAtUtc = now
            });
        }
        else
        {
            ApplyRuntimeRecord(
                inactiveContextRecord,
                plan,
                runtimeImage.ResolvedImageId,
                selectedHostPort,
                fullHostPath,
                inspection,
                now);
        }

        await db.SaveChangesAsync(ct);

        return Success(
            $"Container '{plan.ContainerName}' deployed and started on host port {selectedHostPort}.",
            inspection);
    }

    public async Task<SeqStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        var effectiveOptions = GetEffectiveOptions();
        var profile = RuntimeProfile;
        var persistedRecord = await FindPersistedRuntimeRecordAsync(
            tracking: false,
            ct);
        var record = SelectRuntimeRecordForProfile(persistedRecord, profile);
        var container = await dockerHost.InspectByNameAsync(
            profile.ContainerName,
            ct);
        var warnings = new List<string>();

        if (record is not null && container is null)
        {
            warnings.Add("Persisted Seq runtime state exists for this Control Plane context, but its managed Docker container was not found.");
        }

        if (record is null && container is not null)
        {
            warnings.Add("A Seq container was detected for this Control Plane context without a persisted MEM runtime record. Lifecycle actions remain blocked.");
        }

        return SeqMappers.ToStatusResponse(
            record,
            container,
            ManagedServiceNames.Seq,
            profile.ContainerName,
            effectiveOptions.ExpectedVersion,
            effectiveOptions.ApprovedImageReference,
            warnings,
            runtimeContext);
    }

    public async Task<RuntimeActionResponse> StartAsync(CancellationToken ct)
    {
        var container = await RequireManagedContainerAsync(ct);
        if (container.Running)
        {
            return Success(
                $"Container '{RuntimeProfile.ContainerName}' is already running.",
                container);
        }

        await dockerHost.StartContainerAsync(container.Id, ct);
        var updated = await dockerHost.InspectByNameAsync(
            RuntimeProfile.ContainerName,
            ct);
        if (updated?.Running != true)
        {
            throw new SeqOperationException(
                "seq_start_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "Seq did not reach a running state after Docker accepted the start request.");
        }

        await UpdateRecordAsync(updated, ct);
        return Success(
            $"Container '{RuntimeProfile.ContainerName}' started.",
            updated);
    }

    public async Task<RuntimeActionResponse> StopAsync(CancellationToken ct)
    {
        var container = await RequireManagedContainerAsync(ct);
        if (!container.Running)
        {
            return Success(
                $"Container '{RuntimeProfile.ContainerName}' is already stopped.",
                container);
        }

        await dockerHost.StopContainerAsync(container.Id, ct);
        var updated = await dockerHost.InspectByNameAsync(
            RuntimeProfile.ContainerName,
            ct);
        if (updated?.Running == true)
        {
            throw new SeqOperationException(
                "seq_stop_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "Seq remained running after Docker accepted the stop request.");
        }

        if (updated is not null)
        {
            await UpdateRecordAsync(updated, ct);
        }

        return Success(
            $"Container '{RuntimeProfile.ContainerName}' stopped.",
            updated);
    }

    public async Task<RuntimeActionResponse> RestartAsync(CancellationToken ct)
    {
        var container = await RequireManagedContainerAsync(ct);
        if (container.Running)
        {
            await dockerHost.StopContainerAsync(container.Id, ct);
            var stopped = await dockerHost.InspectByNameAsync(
                RuntimeProfile.ContainerName,
                ct);
            if (stopped?.Running == true)
            {
                throw new SeqOperationException(
                    "seq_stop_verification_failed",
                    StatusCodes.Status503ServiceUnavailable,
                    "Seq remained running after Docker accepted the restart stop request.");
            }
        }

        await dockerHost.StartContainerAsync(container.Id, ct);
        var updated = await dockerHost.InspectByNameAsync(
            RuntimeProfile.ContainerName,
            ct);
        if (updated?.Running != true)
        {
            throw new SeqOperationException(
                "seq_restart_verification_failed",
                StatusCodes.Status503ServiceUnavailable,
                "Seq did not reach a running state after Docker accepted the restart request.");
        }

        await UpdateRecordAsync(updated, ct);
        return Success(
            $"Container '{RuntimeProfile.ContainerName}' restarted.",
            updated);
    }

    public async Task<RuntimeActionResponse> RemoveAsync(CancellationToken ct)
    {
        var profile = RuntimeProfile;
        var persistedRecord = await FindPersistedRuntimeRecordAsync(
            tracking: true,
            ct);
        var record = SelectRuntimeRecordForProfile(persistedRecord, profile);
        var container = await dockerHost.InspectByNameAsync(
            profile.ContainerName,
            ct);

        if (container is not null)
        {
            EnsureManagedOwnership(record, container);
            await dockerHost.RemoveContainerAsync(
                container.Id,
                force: true,
                removeVolumes: false,
                ct);
        }

        if (record is not null)
        {
            db.RuntimeServices.Remove(record);
            await db.SaveChangesAsync(ct);
        }

        return Success(
            $"Managed Seq runtime '{profile.ContainerName}' removed. The configured data directory was retained.",
            null);
    }

    private async Task<DockerContainerInspection> RequireManagedContainerAsync(
        CancellationToken ct)
    {
        var profile = RuntimeProfile;
        var persistedRecord = await FindPersistedRuntimeRecordAsync(
            tracking: false,
            ct);
        var record = SelectRuntimeRecordForProfile(persistedRecord, profile);
        var container = await dockerHost.InspectByNameAsync(
            profile.ContainerName,
            ct);

        if (container is null)
        {
            throw new SeqOperationException(
                "seq_managed_runtime_not_found",
                StatusCodes.Status404NotFound,
                "A MEM-managed Seq runtime was not found.");
        }

        EnsureManagedOwnership(record, container);
        return container;
    }

    private string ResolveHostDataPath(SeqRuntimeContextProfile profile) =>
        SeqFileSystemSafety.ResolveDirectory(
            profile.HostDataPath,
            environment?.ContentRootPath ?? Directory.GetCurrentDirectory(),
            "seq_data_path_invalid");

    private int ResolvePreferredHostPort(
        SeqDiagnosticsOptions effectiveOptions,
        SeqRuntimeContextProfile profile) =>
        effectiveConfigurationProvider is not null &&
        effectiveOptions.PreferredHostPort != options.PreferredHostPort
            ? effectiveOptions.PreferredHostPort
            : profile.PreferredHostPort;

    private SeqDiagnosticsOptions GetEffectiveOptions() =>
        effectiveConfigurationProvider?.CreateEffectiveOptions() ?? options;

    private void EnsureManagedOwnership(
        RuntimeServiceEntity? record,
        DockerContainerInspection container)
    {
        if (record is null ||
            !string.Equals(
                record.ContainerName,
                RuntimeProfile.ContainerName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SeqOperationException(
                "seq_unmanaged_container",
                StatusCodes.Status409Conflict,
                "The detected Seq container is not proven MEM-managed and will not be changed.");
        }

        if (container.Labels.TryGetValue(
                ManagedContainerLabels.CanonicalServiceKey,
                out var serviceLabel) &&
            !string.Equals(
                serviceLabel,
                ManagedServiceNames.Seq,
                StringComparison.Ordinal))
        {
            throw new SeqOperationException(
                "seq_container_ownership_label_mismatch",
                StatusCodes.Status409Conflict,
                "The detected Seq container carries a conflicting MEM service ownership label and will not be changed.");
        }

        if (runtimeContext is not null &&
            container.Labels.TryGetValue(
                ManagedContainerLabels.ControlPlaneInstanceKey,
                out var ownerLabel) &&
            Guid.TryParse(ownerLabel, out var ownerId) &&
            ownerId != runtimeContext.ControlPlaneInstanceId)
        {
            throw new SeqOperationException(
                "seq_control_plane_ownership_mismatch",
                StatusCodes.Status409Conflict,
                "The detected Seq container belongs to a different MEM Control Plane instance and will not be changed.");
        }

        if (!string.IsNullOrWhiteSpace(record.ContainerId) &&
            !string.Equals(
                record.ContainerId,
                container.Id,
                StringComparison.Ordinal))
        {
            throw new SeqOperationException(
                "seq_container_identity_mismatch",
                StatusCodes.Status409Conflict,
                "The detected Seq container does not match the persisted MEM runtime identity and will not be changed.");
        }
    }

    private async Task UpdateRecordAsync(
        DockerContainerInspection inspection,
        CancellationToken ct)
    {
        var profile = RuntimeProfile;
        var persistedRecord = await FindPersistedRuntimeRecordAsync(
            tracking: true,
            ct);
        var record = SelectRuntimeRecordForProfile(persistedRecord, profile);
        if (record is null)
        {
            return;
        }

        record.Status = inspection.State;
        record.ContainerId = inspection.Id;
        record.LastObservedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<RuntimeServiceEntity?> FindPersistedRuntimeRecordAsync(
        bool tracking,
        CancellationToken cancellationToken)
    {
        IQueryable<RuntimeServiceEntity> query = db.RuntimeServices;
        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(
            item => item.ServiceName == ManagedServiceNames.Seq,
            cancellationToken);
    }

    private static bool RecordMatchesProfile(
        RuntimeServiceEntity? record,
        SeqRuntimeContextProfile profile) =>
        record is not null &&
        string.Equals(
            record.ContainerName,
            profile.ContainerName,
            StringComparison.OrdinalIgnoreCase);

    private static RuntimeServiceEntity? SelectRuntimeRecordForProfile(
        RuntimeServiceEntity? record,
        SeqRuntimeContextProfile profile) =>
        record is null || RecordMatchesProfile(record, profile) || !profile.ContextScoped
            ? record
            : null;

    private static void ApplyRuntimeRecord(
        RuntimeServiceEntity record,
        SeqPlanResponse plan,
        string image,
        int selectedHostPort,
        string fullHostPath,
        DockerContainerInspection inspection,
        DateTime observedAtUtc)
    {
        record.ServiceName = plan.ServiceName;
        record.ContainerName = plan.ContainerName;
        record.Image = image;
        record.ContainerPort = plan.ContainerPort;
        record.PreferredHostPort = plan.PreferredHostPort;
        record.SelectedHostPort = selectedHostPort;
        record.HostPath = fullHostPath;
        record.SecondaryHostPath = null;
        record.ContainerId = inspection.Id;
        record.Status = inspection.State;
        record.CreatedAtUtc = observedAtUtc;
        record.LastObservedAtUtc = observedAtUtc;
    }

    private static RuntimeActionResponse Success(
        string message,
        DockerContainerInspection? container) =>
        new(
            Success: true,
            Message: message,
            Container: container);
}
