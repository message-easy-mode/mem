using System.Text.RegularExpressions;
using Core.Runtime;
using Core.RuntimeDefinition;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Portainer.Contracts;

namespace Modules.Integrations.Portainer.Services;

public sealed partial class PortainerRuntimeService(
    IDockerHost dockerHost,
    RuntimePortPlanner portPlanner,
    MemDbContext db,
    PortainerRuntimeOptions options,
    PortainerRuntimeImageProvider imageProvider,
    global::Shared.ControlPlane.Runtime.MemControlPlaneRuntimeContext? runtimeContext = null)
    : IPortainerRuntimeStatusReader
{
    private const int HttpsContainerPort = 9443;
    private const int LegacyHttpContainerPort = 9000;
    private const string DockerSocketPath = "/var/run/docker.sock";

    public async Task<PortainerInstallResult> EnsureInstalledForSetupAsync(
        PortainerInstallRequest request,
        CancellationToken cancellationToken)
    {
        PortainerRuntimeOptionsValidator.ThrowIfInvalid(options);

        var record = await FindRecordAsync(cancellationToken);
        var container = await dockerHost.InspectByNameAsync(
            options.ContainerName,
            cancellationToken);

        if (container is not null)
        {
            if (record is null)
            {
                if (!request.UseExistingIfDetected)
                {
                    throw new PortainerOperationException(
                        "portainer_unmanaged_container",
                        "An existing Portainer container is not proven MEM-managed and will not be changed.");
                }

                var observed = await BuildStatusAsync(
                    record,
                    container,
                    cancellationToken);
                return new PortainerInstallResult(
                    Status: "observed-unmanaged",
                    Message:
                        "An existing Portainer container was detected and left unchanged. MEM will not adopt, replace, or upgrade it automatically.",
                    PulledImage: false,
                    CreatedContainer: false,
                    ReusedExisting: true,
                    Runtime: observed);
            }

            EnsureManagedOwnership(record, container);
            if (!container.Running)
            {
                await dockerHost.StartContainerAsync(
                    container.Id,
                    cancellationToken);
                container = await dockerHost.InspectByNameAsync(
                    options.ContainerName,
                    cancellationToken);
                if (container?.Running != true)
                {
                    throw new PortainerOperationException(
                        "portainer_start_verification_failed",
                        "Portainer did not reach a running state after Docker accepted the start request.");
                }

                await UpdateRecordAsync(container, cancellationToken);
            }

            var managed = await BuildStatusAsync(
                record,
                container,
                cancellationToken);
            var message = managed.UsesApprovedRuntime
                ? "The existing MEM-managed Portainer runtime is running on the approved immutable image."
                : "The existing MEM-managed Portainer runtime was retained without an automatic upgrade. Use a future explicit upgrade workflow to replace it.";

            return new PortainerInstallResult(
                Status: "existing-managed",
                Message: message,
                PulledImage: false,
                CreatedContainer: false,
                ReusedExisting: true,
                Runtime: managed);
        }

        if (record is not null)
        {
            throw new PortainerOperationException(
                "portainer_runtime_record_conflict",
                "A persisted Portainer runtime record exists, but its managed container was not found. MEM will not recreate it automatically.");
        }

        var runtimeImage = await imageProvider.PrepareForInstallationAsync(
            cancellationToken);
        var preferredPort = request.PreferredUiHostPort ??
                            options.PreferredHttpsHostPort;
        if (preferredPort is < 1 or > 65535)
        {
            throw new PortainerOperationException(
                "portainer_host_port_invalid",
                "The preferred Portainer HTTPS host port is invalid.");
        }

        var warnings = new List<string>();
        var portPlan = portPlanner.BuildPortPlan(
            HttpsContainerPort,
            preferredPort,
            warnings,
            []);
        var selectedHostPort = request.ForcePreferredPort
            ? portPlan.PreferredHostPort
            : portPlan.SelectedHostPort;

        var spec = new DockerContainerSpec(
            Name: options.ContainerName,
            Image: runtimeImage.ResolvedImageId,
            Labels: ManagedContainerLabels.ForService(
                ManagedServiceNames.Portainer,
                runtimeContext),
            PortBindings: new Dictionary<string, string>
            {
                [$"{HttpsContainerPort}/tcp"] = selectedHostPort.ToString()
            },
            BindMounts:
            [
                new DockerBindMount(
                    DockerSocketPath,
                    DockerSocketPath)
            ],
            VolumeMounts:
            [
                new DockerVolumeMount(
                    options.DataVolumeName,
                    "/data")
            ],
            RestartPolicy: new DockerRestartPolicy(
                DockerRestartPolicyName.Always));

        var containerId = await dockerHost.CreateContainerAsync(
            spec,
            cancellationToken);
        try
        {
            await dockerHost.StartContainerAsync(
                containerId,
                cancellationToken);
        }
        catch
        {
            await dockerHost.RemoveContainerAsync(
                containerId,
                force: true,
                removeVolumes: false,
                cancellationToken);
            throw;
        }

        var created = await dockerHost.InspectByNameAsync(
            options.ContainerName,
            cancellationToken);
        if (created?.Running != true ||
            FindUiHostPort(created) != selectedHostPort)
        {
            await dockerHost.RemoveContainerAsync(
                containerId,
                force: true,
                removeVolumes: false,
                cancellationToken);
            throw new PortainerOperationException(
                "portainer_start_verification_failed",
                "Docker created Portainer, but the approved HTTPS runtime did not reach the expected running state.");
        }

        var now = DateTime.UtcNow;
        db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = ManagedServiceNames.Portainer,
            ContainerName = options.ContainerName,
            Image = runtimeImage.ResolvedImageId,
            ContainerPort = HttpsContainerPort,
            PreferredHostPort = preferredPort,
            SelectedHostPort = selectedHostPort,
            HostPath = null,
            SecondaryHostPath = null,
            ContainerId = created.Id,
            Status = created.State,
            CreatedAtUtc = now,
            LastObservedAtUtc = now
        });
        await db.SaveChangesAsync(cancellationToken);

        var status = await BuildStatusAsync(
            await FindRecordAsync(cancellationToken),
            created,
            cancellationToken);
        return new PortainerInstallResult(
            Status: "installed",
            Message:
                $"Portainer {options.ExpectedVersion} was installed from the approved immutable local image and started on HTTPS host port {selectedHostPort}.",
            PulledImage: runtimeImage.PulledDuringPreparation,
            CreatedContainer: true,
            ReusedExisting: false,
            Runtime: status);
    }

    public async Task<PortainerRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(cancellationToken);
        var container = await dockerHost.InspectByNameAsync(
            options.ContainerName,
            cancellationToken);
        return await BuildStatusAsync(record, container, cancellationToken);
    }

    public async Task<PortainerRuntimeStatus> StartManagedAsync(
        CancellationToken cancellationToken)
    {
        PortainerRuntimeOptionsValidator.ThrowIfInvalid(options);
        var (record, container) = await RequireManagedContainerAsync(
            cancellationToken);
        if (!container.Running)
        {
            await dockerHost.StartContainerAsync(
                container.Id,
                cancellationToken);
            container = await dockerHost.InspectByNameAsync(
                options.ContainerName,
                cancellationToken) ??
                throw new PortainerOperationException(
                    "portainer_start_verification_failed",
                    "Portainer disappeared while its start request was being verified.");
            if (!container.Running || FindUiHostPort(container) is null)
            {
                throw new PortainerOperationException(
                    "portainer_start_verification_failed",
                    "Portainer did not reach its expected running HTTPS UI state.");
            }

            await UpdateRecordAsync(container, cancellationToken);
        }

        return await BuildStatusAsync(record, container, cancellationToken);
    }

    public async Task<PortainerRuntimeStatus> StopManagedAsync(
        CancellationToken cancellationToken)
    {
        PortainerRuntimeOptionsValidator.ThrowIfInvalid(options);
        var (record, container) = await RequireManagedContainerAsync(
            cancellationToken);
        if (container.Running)
        {
            await dockerHost.StopContainerAsync(
                container.Id,
                cancellationToken);
            container = await dockerHost.InspectByNameAsync(
                options.ContainerName,
                cancellationToken) ??
                throw new PortainerOperationException(
                    "portainer_stop_verification_failed",
                    "Portainer disappeared while its stop request was being verified.");
            if (container.Running)
            {
                throw new PortainerOperationException(
                    "portainer_stop_verification_failed",
                    "Portainer remained running after Docker accepted the stop request.");
            }

            await UpdateRecordAsync(container, cancellationToken);
        }

        return await BuildStatusAsync(record, container, cancellationToken);
    }

    private async Task<PortainerRuntimeStatus> BuildStatusAsync(
        RuntimeServiceEntity? record,
        DockerContainerInspection? container,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        PortainerResolvedRuntimeImage? approvedImage = null;
        try
        {
            approvedImage = await imageProvider.ResolveForOperationAsync(
                cancellationToken);
        }
        catch (PortainerOperationException exception)
        {
            warnings.Add(exception.Code);
        }

        var ownershipState = ClassifyOwnership(record, container);
        var managed = ownershipState == "managed";
        if (ownershipState == "unmanaged")
        {
            warnings.Add("portainer_unmanaged_container");
        }
        else if (ownershipState == "runtime-missing")
        {
            warnings.Add("portainer_managed_runtime_missing");
        }
        else if (ownershipState == "identity-mismatch")
        {
            warnings.Add("portainer_container_identity_mismatch");
        }

        var usesApprovedRuntime = managed &&
                                  approvedImage is not null &&
                                  string.Equals(
                                      record!.Image,
                                      approvedImage.ResolvedImageId,
                                      StringComparison.OrdinalIgnoreCase) &&
                                  string.Equals(
                                      container!.Image,
                                      approvedImage.ResolvedImageId,
                                      StringComparison.OrdinalIgnoreCase);
        if (managed && !usesApprovedRuntime)
        {
            warnings.Add("portainer_approved_upgrade_required");
        }

        var observedImage = container?.Image;
        var observedVersion = TryReadVersion(observedImage);
        var upgradeAvailable = container is not null &&
                               (observedVersion is not null
                                   ? !string.Equals(
                                       observedVersion,
                                       options.ExpectedVersion,
                                       StringComparison.Ordinal)
                                   : managed && !usesApprovedRuntime);
        var uiPort = container is null ? null : FindUiHostPort(container);
        var ready = container?.Running == true && uiPort is not null;

        return new PortainerRuntimeStatus(
            ServiceName: ManagedServiceNames.Portainer,
            ContainerName: options.ContainerName,
            ApprovedVersion: options.ExpectedVersion,
            ApprovedImageReference: options.ApprovedImageReference,
            Exists: container is not null,
            Running: container?.Running == true,
            Ready: ready,
            Managed: managed,
            OwnershipState: ownershipState,
            UsesApprovedRuntime: usesApprovedRuntime,
            UpgradeAvailable: upgradeAvailable,
            ObservedImageReference: observedImage,
            ObservedVersion: observedVersion,
            UiHostPort: uiPort,
            DataRetained: record is not null,
            PublishesPublicIngress: false,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    private async Task<(RuntimeServiceEntity Record, DockerContainerInspection Container)>
        RequireManagedContainerAsync(CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(cancellationToken);
        var container = await dockerHost.InspectByNameAsync(
            options.ContainerName,
            cancellationToken);
        if (container is null)
        {
            throw new PortainerOperationException(
                "portainer_managed_runtime_not_found",
                "A MEM-managed Portainer runtime was not found.");
        }

        EnsureManagedOwnership(record, container);
        return (record!, container);
    }

    private static void EnsureManagedOwnership(
        RuntimeServiceEntity? record,
        DockerContainerInspection container)
    {
        if (record is null ||
            !string.Equals(
                record.ContainerName,
                ManagedContainerNames.Portainer,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PortainerOperationException(
                "portainer_unmanaged_container",
                "The detected Portainer container is not proven MEM-managed and will not be changed.");
        }

        if (!string.IsNullOrWhiteSpace(record.ContainerId) &&
            !string.Equals(
                record.ContainerId,
                container.Id,
                StringComparison.Ordinal))
        {
            throw new PortainerOperationException(
                "portainer_container_identity_mismatch",
                "The detected Portainer container does not match the persisted MEM runtime identity and will not be changed.");
        }
    }

    private static string ClassifyOwnership(
        RuntimeServiceEntity? record,
        DockerContainerInspection? container)
    {
        if (record is null && container is null)
        {
            return "absent";
        }

        if (record is null)
        {
            return "unmanaged";
        }

        if (container is null)
        {
            return "runtime-missing";
        }

        if (!string.IsNullOrWhiteSpace(record.ContainerId) &&
            !string.Equals(
                record.ContainerId,
                container.Id,
                StringComparison.Ordinal))
        {
            return "identity-mismatch";
        }

        return "managed";
    }

    private async Task<RuntimeServiceEntity?> FindRecordAsync(
        CancellationToken cancellationToken) =>
        await db.RuntimeServices
            .FirstOrDefaultAsync(
                x => x.ServiceName == ManagedServiceNames.Portainer,
                cancellationToken);

    private async Task UpdateRecordAsync(
        DockerContainerInspection inspection,
        CancellationToken cancellationToken)
    {
        var record = await FindRecordAsync(cancellationToken);
        if (record is null)
        {
            return;
        }

        record.ContainerId = inspection.Id;
        record.Status = inspection.State;
        record.LastObservedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static int? FindUiHostPort(DockerContainerInspection inspection)
    {
        var https = inspection.Ports.FirstOrDefault(port =>
            port.PrivatePort == HttpsContainerPort &&
            port.PublicPort > 0);
        if (https is not null)
        {
            return checked((int)https.PublicPort);
        }

        var legacyHttp = inspection.Ports.FirstOrDefault(port =>
            port.PrivatePort == LegacyHttpContainerPort &&
            port.PublicPort > 0);
        return legacyHttp is null
            ? null
            : checked((int)legacyHttp.PublicPort);
    }

    private static string? TryReadVersion(string? imageReference)
    {
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            return null;
        }

        var match = ExactObservedVersionRegex().Match(imageReference.Trim());
        return match.Success ? match.Groups["version"].Value : null;
    }

    [GeneratedRegex(
        @"^portainer/portainer-ce:(?<version>[0-9]+\.[0-9]+\.[0-9]+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExactObservedVersionRegex();
}
