using System.Net;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Npm.Services;
using Shared.ControlPlane.Runtime;
using Modules.Shared.Docker;

namespace HostAgent.Runtime.Migrations.Staging.Retirement;

/// <summary>
/// Deletes only exact, freshly inspected Docker IDs and the derived disposable workspace.
/// There is deliberately no name fallback, route mutation or production deletion. The only
/// volume retirement permitted is the image-created Postgres data volume attached to the exact
/// recorded staging Postgres container, after proving no other container consumes it.
/// </summary>
public sealed class MigrationStagingRetirementRuntime(
    DockerClient docker, MemDbContext db, MigrationStagingRetirementHistory history,
    NpmProxyHostService npm, MemControlPlaneRuntimeContext runtimeContext, IControlPlaneDockerOwnershipGuard ownershipGuard) : IMigrationStagingRetirementRuntime
{
    public async Task<MigrationStagingRetirementInspection> InspectAsync(string stagingId, string candidateArtifactId, CancellationToken ct)
    {
        await RequireExclusiveOwnershipAsync(ct);
        var (_, plan) = await history.ReadAsync(stagingId, candidateArtifactId, ct);
        var containers = await docker.Containers.ListContainersAsync(new ContainersListParameters { All = true }, ct);
        var networks = await docker.Networks.ListNetworksAsync(new NetworksListParameters(), ct);
        var expected = plan.Containers.Where(x => x.Id is not null).Select(x => x.Id!).ToHashSet(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            stagingId
        };
        foreach (var (role, id) in plan.Containers)
        {
            targets.Add($"mem-restore-staging-{role}-{stagingId}");
            if (id is not null) targets.Add(id);
        }
        if (containers.Any(x => (Label(x.Labels, "mem.restore-staging.id") == stagingId ||
                                  x.Names?.Any(n => targets.Contains(n.TrimStart('/'))) == true) && !expected.Contains(x.ID)) ||
            networks.Any(x => (Label(x.Labels, "mem.restore-staging.id") == stagingId || x.Name == $"mem-restore-staging-{stagingId}") && x.ID != plan.NetworkId))
            throw new MigrationStagingRetirementException("ownership-changed");
        if (await db.RuntimeServiceInstances.AsNoTracking().AnyAsync(x =>
            (x.ContainerId != null && expected.Contains(x.ContainerId)) ||
            (x.ContainerName != null && targets.Contains(x.ContainerName)), ct))
            throw new MigrationStagingRetirementException("production-resource");

        var observedContainers = 0;
        var selectedVolumeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (role, id) in plan.Containers)
        {
            if (id is null) continue;
            var current = await InspectContainerAsync(id, ct);
            if (current is null) continue;
            observedContainers++;
            if (!string.IsNullOrWhiteSpace(current.Name)) targets.Add(current.Name.TrimStart('/'));
            if (current.ID != id) throw new MigrationStagingRetirementException("ownership-changed");
            ValidateLabels(current.Config?.Labels, stagingId, candidateArtifactId, role, runtimeContext.ControlPlaneInstanceId);
            ValidateContainerTopology(current, plan.NetworkId, role);
            foreach (var mount in current.Mounts ?? [])
            {
                if (mount.Type == "volume" && !string.IsNullOrWhiteSpace(mount.Name))
                    selectedVolumeNames.Add(mount.Name);
            }
            foreach (var endpoint in current.NetworkSettings.Networks?.Values ?? Array.Empty<EndpointSettings>())
            {
                if (!string.IsNullOrWhiteSpace(endpoint.IPAddress)) targets.Add(endpoint.IPAddress);
                if (!string.IsNullOrWhiteSpace(endpoint.GlobalIPv6Address)) targets.Add(endpoint.GlobalIPv6Address);
                foreach (var alias in endpoint.Aliases ?? []) if (alias?.Contains(stagingId, StringComparison.Ordinal) == true) targets.Add(alias);
            }
        }
        var observedNetwork = plan.NetworkId is null ? null : await InspectNetworkAsync(plan.NetworkId, ct);
        if (observedNetwork is not null)
        {
            if (observedNetwork.ID != plan.NetworkId) throw new MigrationStagingRetirementException("ownership-changed");
            ValidateLabels(observedNetwork.Labels, stagingId, candidateArtifactId, null, runtimeContext.ControlPlaneInstanceId);
            ValidateNetworkTopology(observedNetwork, expected);
        }
        else if (observedContainers > 0)
        {
            // A retained container may be disconnected after partial retirement, but no
            // newly created network may be silently substituted for the recorded one.
            foreach (var id in expected)
            {
                var container = await InspectContainerAsync(id, ct);
                if (container?.NetworkSettings?.Networks?.Count > 0)
                    throw new MigrationStagingRetirementException("public-use-unproven");
            }
        }

        // Check all non-selected bind consumers before touching the disposable directory.
        foreach (var container in containers.Where(x => !expected.Contains(x.ID)))
        {
            var inspected = await InspectContainerAsync(container.ID, ct);
            if (inspected is null) continue;
            foreach (var mount in inspected.Mounts ?? [])
            {
                if (mount.Type == "volume" && !string.IsNullOrWhiteSpace(mount.Name) && selectedVolumeNames.Contains(mount.Name))
                    throw new MigrationStagingRetirementException("workspace-in-use");
                if (mount.Type != "bind" || string.IsNullOrWhiteSpace(mount.Source) ||
                    !MigrationStagingRetirementHistory.Overlaps(mount.Source, history.Workspace(stagingId))) continue;
                // The exclusive-ownership guard validates the configured Control Plane
                // identity. Its management mount is not a production consumer.
                var ownControlPlane = runtimeContext.RunningInContainer &&
                    !string.IsNullOrEmpty(runtimeContext.ConfiguredContainerName) &&
                    inspected.Name?.TrimStart('/') == runtimeContext.ConfiguredContainerName &&
                    inspected.Config?.Hostname == Environment.MachineName;
                if (!ownControlPlane) throw new MigrationStagingRetirementException("workspace-in-use");
            }
        }
        // A failed NPM read is not evidence that no public consumer exists.
        var routes = await npm.ListAsync(ct);
        foreach (var route in routes.Where(x => x.enabled != false))
        {
            ValidatePublicRoute(route.forward_host, route.advanced_config, route.locations, targets);
        }
        history.ValidateWorkspace(stagingId, ct);
        return new(plan, observedContainers, observedNetwork is null ? 0 : 1, history.WorkspacePresent(stagingId));
    }

    public async Task RetireAsync(MigrationStagingRetirementPlan plan, Func<string, CancellationToken, Task> progress, CancellationToken ct)
    {
        await RequireExclusiveOwnershipAsync(ct);
        await VerifyAcceptedAsync(plan, ct);
        foreach (var (role, id) in plan.Containers)
        {
            await progress("remove-" + role, ct);
            // Recheck topology, public use and complete membership at every mutation boundary.
            await VerifyAcceptedAsync(plan, ct);
            if (id is not null && await InspectContainerAsync(id, ct) is not null)
            {
                try { await docker.Containers.RemoveContainerAsync(id, new ContainerRemoveParameters { Force = true, RemoveVolumes = role == "postgres" }, ct); }
                catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
                if (await InspectContainerAsync(id, ct) is not null) throw new MigrationStagingRetirementException("cleanup-incomplete");
            }
        }
        await progress("remove-network", ct);
        await VerifyAcceptedAsync(plan, ct);
        if (plan.NetworkId is not null && await InspectNetworkAsync(plan.NetworkId, ct) is not null)
        {
            try { await docker.Networks.DeleteNetworkAsync(plan.NetworkId, ct); }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            if (await InspectNetworkAsync(plan.NetworkId, ct) is not null) throw new MigrationStagingRetirementException("cleanup-incomplete");
        }
        await progress("remove-workspace", ct);
        await VerifyAcceptedAsync(plan, ct);
        history.RemoveWorkspace(plan.StagingId, ct);
        var final = await InspectAsync(plan.StagingId, plan.CandidateArtifactId, ct);
        if (final.Plan != plan || final.ContainerCount != 0 || final.NetworkCount != 0 || final.WorkspacePresent)
            throw new MigrationStagingRetirementException("cleanup-incomplete");
        await progress("record-evidence", ct);
        await history.RecordRetiredAsync(plan, ct);
    }

    private async Task VerifyAcceptedAsync(MigrationStagingRetirementPlan plan, CancellationToken ct)
    {
        await RequireExclusiveOwnershipAsync(ct);
        var current = await InspectAsync(plan.StagingId, plan.CandidateArtifactId, ct);
        if (current.Plan != plan) throw new MigrationStagingRetirementException("ownership-changed");
    }
    private async Task RequireExclusiveOwnershipAsync(CancellationToken ct)
    {
        var ownership = await ownershipGuard.InspectAsync(ct);
        if (!runtimeContext.MutationsAllowed || runtimeContext.ControlPlaneInstanceId == Guid.Empty || ownership.State != "exclusive")
            throw new MigrationStagingRetirementException("control-plane-ownership-unproven");
    }
    internal static void ValidateContainerTopology(ContainerInspectResponse current, string? networkId, string role)
    {
        var volumeMounts = current.Mounts?.Where(m => m.Type == "volume").ToArray() ?? [];
        var allowedPostgresDataVolume = role == "postgres" &&
            volumeMounts.Length <= 1 &&
            volumeMounts.All(m => string.Equals(m.Destination, "/var/lib/postgresql/data", StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(m.Name));
        if (current.HostConfig is null || current.NetworkSettings?.Networks is null ||
            current.HostConfig.NetworkMode is "host" || current.HostConfig.NetworkMode?.StartsWith("container:", StringComparison.Ordinal) == true ||
            current.HostConfig.PortBindings?.Any(p => p.Value?.Count > 0) == true ||
            current.NetworkSettings.Ports?.Any(p => p.Value?.Count > 0) == true ||
            current.NetworkSettings.Networks.Any(n => string.IsNullOrEmpty(n.Value.NetworkID) || n.Value.NetworkID != networkId) ||
            (volumeMounts.Length > 0 && !allowedPostgresDataVolume))
            throw new MigrationStagingRetirementException("public-use-unproven");
    }
    internal static void ValidateNetworkTopology(NetworkResponse network, IReadOnlySet<string> selectedIds)
    {
        if (!network.Internal || network.Containers is null || network.Containers.Keys.Any(id => !selectedIds.Contains(id)))
            throw new MigrationStagingRetirementException("public-use-unproven");
    }
    internal static void ValidatePublicRoute(string? host, string? advanced, object? locations, IEnumerable<string> targets)
    {
        // Docker topology is the primary reachability proof: the selected staging
        // containers must be attached only to their recorded internal network,
        // that network may contain only the selected container IDs, and no host
        // ports may be published. Once that isolation is proven, an unrelated NPM
        // route with a dynamic/blank forwarding host or custom proxy_pass/upstream
        // cannot make the staging runtime reachable and must not veto retirement.
        //
        // We still fail closed on any explicit reference to the staging identity,
        // exact Docker IDs, names, addresses or recorded aliases. A failed NPM read
        // also continues to fail the inspection before this helper is reached.
        var serialized = JsonSerializer.Serialize(new { host, locations, advanced });
        if (targets.Any(target => !string.IsNullOrWhiteSpace(target) &&
                                  serialized.Contains(target, StringComparison.OrdinalIgnoreCase)))
            throw new MigrationStagingRetirementException("public-resource");
    }

    private async Task<ContainerInspectResponse?> InspectContainerAsync(string id, CancellationToken ct)
    {
        try { return await docker.Containers.InspectContainerAsync(id, ct); }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    private async Task<NetworkResponse?> InspectNetworkAsync(string id, CancellationToken ct)
    {
        try { return await docker.Networks.InspectNetworkAsync(id, ct); }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    private static string? Label(IDictionary<string, string>? labels, string key) =>
        labels is not null && labels.TryGetValue(key, out var value) ? value : null;

    internal static void ValidateLabels(IDictionary<string, string>? labels, string stagingId, string candidateId, string? role, Guid controlPlaneId)
    {
        // Element uses the canonical Element starter rather than staging labels.
        // This branch is still reached only for the exact recorded Element ID;
        // production rows, mounts and topology are checked separately above.
        if (role == "element" && Label(labels, "mem.component") == "element-web")
        {
            if (controlPlaneId != Guid.Empty && Label(labels, "mem.managed-by") == "host-agent" &&
                Label(labels, "mem.service") == "element" &&
                Label(labels, MemDockerOwnershipLabels.ManagedKey) == "true" &&
                Label(labels, MemDockerOwnershipLabels.ServiceKey) == "element" &&
                Label(labels, MemDockerOwnershipLabels.ResourceKey) == "matrix-stack-service" &&
                Guid.TryParse(Label(labels, MemDockerOwnershipLabels.ControlPlaneInstanceKey), out var elementOwner) &&
                elementOwner == controlPlaneId &&
                string.IsNullOrEmpty(Label(labels, "mem.restore-staging.id")) &&
                string.IsNullOrEmpty(Label(labels, "mem.restore-staging.source-kind")) &&
                string.IsNullOrEmpty(Label(labels, "mem.restore-staging.catalog-entry-id")) &&
                string.IsNullOrEmpty(Label(labels, "mem.migration-staging.candidate-id"))) return;
            throw new MigrationStagingRetirementException("ownership-unproven");
        }
        if (labels is null || Label(labels, "mem.managed-by") != "mem-host-agent" ||
            Label(labels, "mem.component") != "restore-staging" ||
            Label(labels, "mem.restore-staging.id") != stagingId ||
            Label(labels, "mem.restore-staging.source-kind") != "migration-candidate" ||
            Label(labels, "mem.migration-staging.candidate-id") != candidateId ||
            labels.ContainsKey("mem.restore-staging.catalog-entry-id") ||
            (role is not null && Label(labels, "mem.service") != role) ||
            (labels.TryGetValue(MemDockerOwnershipLabels.ControlPlaneInstanceKey, out var instance) &&
             (!Guid.TryParse(instance, out var parsed) || parsed != controlPlaneId)))
            throw new MigrationStagingRetirementException("ownership-unproven");
    }
}
