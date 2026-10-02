using Infrastructure.Docker;
using Infrastructure.Docker.Models;

namespace Modules.Shared.Docker;

/// <summary>
/// Central Docker mutation boundary. Read-only inspection remains available
/// during an ownership conflict so Diagnostics can explain the problem, while
/// every mutation through IDockerHost fails before Docker is changed.
/// </summary>
public sealed class ExclusiveControlPlaneDockerHost(
    DockerHost inner,
    IControlPlaneDockerOwnershipGuard ownershipGuard) : IDockerHost
{
    public Task<bool> PingAsync(CancellationToken ct) => inner.PingAsync(ct);

    public async Task PullImageAsync(string image, CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.PullImageAsync(image, ct);
    }

    public Task<bool> ImageExistsAsync(string image, CancellationToken ct) =>
        inner.ImageExistsAsync(image, ct);

    public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(
        CancellationToken ct) => inner.ListContainersAsync(ct);

    public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(
        string namePrefix,
        CancellationToken ct) => inner.ListByPrefixAsync(namePrefix, ct);

    public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
        string labelKey,
        string labelValue,
        CancellationToken ct) => inner.ListByLabelAsync(labelKey, labelValue, ct);

    public Task<DockerContainerInspection?> InspectByNameAsync(
        string containerName,
        CancellationToken ct) => inner.InspectByNameAsync(containerName, ct);

    public async Task EnsureNetworkAsync(string networkName, CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.EnsureNetworkAsync(networkName, ct);
    }

    public async Task EnsureVolumeAsync(string volumeName, CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.EnsureVolumeAsync(volumeName, ct);
    }

    public async Task ConnectContainerToNetworkAsync(
        string containerIdOrName,
        string networkName,
        CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.ConnectContainerToNetworkAsync(containerIdOrName, networkName, ct);
    }

    public async Task<string> CreateContainerAsync(
        DockerContainerSpec spec,
        CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        return await inner.CreateContainerAsync(spec, ct);
    }

    public async Task CopyFileToContainerAsync(
        string containerIdOrName,
        string destinationDirectory,
        string fileName,
        ReadOnlyMemory<byte> content,
        UnixFileMode mode,
        CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.CopyFileToContainerAsync(
            containerIdOrName,
            destinationDirectory,
            fileName,
            content,
            mode,
            ct);
    }

    public async Task<DockerExecResult> ExecAsync(
        string containerIdOrName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        return await inner.ExecAsync(containerIdOrName, command, timeout, ct);
    }

    public async Task StartContainerAsync(string containerId, CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.StartContainerAsync(containerId, ct);
    }

    public async Task StopContainerAsync(string containerId, CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.StopContainerAsync(containerId, ct);
    }

    public async Task RemoveContainerAsync(
        string containerId,
        bool force,
        bool removeVolumes,
        CancellationToken ct)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(ct);
        await inner.RemoveContainerAsync(containerId, force, removeVolumes, ct);
    }

    public Task<string> GetLogsAsync(
        string containerId,
        int tail,
        CancellationToken ct) => inner.GetLogsAsync(containerId, tail, ct);
}
