using Microsoft.AspNetCore.Http;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Shared.ControlPlane;
using Shared.ControlPlane.Runtime;
using Shared.Exceptions;

namespace Modules.Shared.Docker;

public interface IControlPlaneDockerOwnershipGuard
{
    Task<MemDockerOwnershipProjection> InspectAsync(
        CancellationToken cancellationToken);

    Task EnsureMutationAllowedAsync(
        CancellationToken cancellationToken);
}

public interface IControlPlaneContainerInventory
{
    Task<IReadOnlyList<DockerContainerSummary>> ListAsync(
        CancellationToken cancellationToken);
}

public sealed class DockerControlPlaneContainerInventory(
    DockerHost dockerHost) : IControlPlaneContainerInventory
{
    public Task<IReadOnlyList<DockerContainerSummary>> ListAsync(
        CancellationToken cancellationToken) =>
        dockerHost.ListContainersAsync(cancellationToken);
}

public sealed class ControlPlaneDockerOwnershipService(
    IControlPlaneContainerInventory inventory,
    MemControlPlaneRuntimeContext runtimeContext)
    : IControlPlaneDockerOwnershipGuard
{
    public async Task<MemDockerOwnershipProjection> InspectAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DockerContainerSummary> containers;
        try
        {
            containers = await inventory.ListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new MemDockerOwnershipProjection(
                State: "unavailable",
                MutationsAllowed: false,
                DevelopmentOverrideActive: false,
                CompetingContainers: [],
                WarningCode: "control_plane_docker_ownership_unavailable");
        }

        var configuredName = runtimeContext.ConfiguredContainerName?.Trim();
        var competing = containers
            .Where(container => IsRunning(container.State))
            .Select(container => container.Name.TrimStart('/'))
            .Where(IsControlPlaneRuntimeName)
            .Where(name => string.IsNullOrWhiteSpace(configuredName) ||
                           !string.Equals(name, configuredName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (competing.Length == 0)
        {
            return new MemDockerOwnershipProjection(
                State: "exclusive",
                MutationsAllowed: true,
                DevelopmentOverrideActive: false,
                CompetingContainers: [],
                WarningCode: null);
        }

        var overrideActive = runtimeContext.AllowSharedDockerHost &&
                             MemRuntimeModes.IsNonProduction(runtimeContext.RuntimeMode);
        if (overrideActive)
        {
            return new MemDockerOwnershipProjection(
                State: "shared-development-override",
                MutationsAllowed: true,
                DevelopmentOverrideActive: true,
                CompetingContainers: competing,
                WarningCode: "control_plane_shared_docker_host_override_active");
        }

        return new MemDockerOwnershipProjection(
            State: "conflict",
            MutationsAllowed: false,
            DevelopmentOverrideActive: false,
            CompetingContainers: competing,
            WarningCode: "control_plane_competing_controller_detected");
    }

    public async Task EnsureMutationAllowedAsync(
        CancellationToken cancellationToken)
    {
        var result = await InspectAsync(cancellationToken);
        if (result.MutationsAllowed)
        {
            return;
        }

        throw new MemProblemException(
            StatusCodes.Status409Conflict,
            result.WarningCode ?? "control_plane_docker_ownership_unavailable",
            "Docker mutation is unavailable",
            result.State == "conflict"
                ? "Another MEM Control Plane is active on this Docker host. Stop the competing controller or use the supported environment switch before retrying."
                : "MEM could not establish exclusive Control Plane ownership of this Docker host. No Docker mutation was attempted.");
    }

    private static bool IsRunning(string? state) =>
        string.Equals(state, "running", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "restarting", StringComparison.OrdinalIgnoreCase);

    private static bool IsControlPlaneRuntimeName(string name) =>
        string.Equals(name, MemControlPlaneIdentity.CanonicalContainerName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, MemControlPlaneIdentity.Legacy.ContainerName, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(MemControlPlaneIdentity.DevelopmentContainerName, StringComparison.OrdinalIgnoreCase);
}
