using Infrastructure.Docker.Models;

namespace Infrastructure.Docker;

public interface IDockerHost
{
    Task<bool> PingAsync(CancellationToken ct);

    Task PullImageAsync(
        string image,
        CancellationToken ct);

    Task<bool> ImageExistsAsync(string image, CancellationToken ct);

    Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(
        CancellationToken ct);

    Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(
        string namePrefix,
        CancellationToken ct);

    Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
        string labelKey,
        string labelValue,
        CancellationToken ct);

    Task<DockerContainerInspection?> InspectByNameAsync(
        string containerName,
        CancellationToken ct);

    Task EnsureNetworkAsync(
        string networkName,
        CancellationToken ct);

    Task EnsureVolumeAsync(
        string volumeName,
        CancellationToken ct);

    Task ConnectContainerToNetworkAsync(
        string containerIdOrName,
        string networkName,
        CancellationToken ct);

    Task<string> CreateContainerAsync(
        DockerContainerSpec spec,
        CancellationToken ct);

    Task CopyFileToContainerAsync(
        string containerIdOrName,
        string destinationDirectory,
        string fileName,
        ReadOnlyMemory<byte> content,
        UnixFileMode mode,
        CancellationToken ct);

    Task<DockerExecResult> ExecAsync(
        string containerIdOrName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken ct);

    Task StartContainerAsync(
        string containerId,
        CancellationToken ct);

    Task StopContainerAsync(
        string containerId,
        CancellationToken ct);

    Task RemoveContainerAsync(
        string containerId,
        bool force,
        bool removeVolumes,
        CancellationToken ct);

    Task<string> GetLogsAsync(
        string containerId,
        int tail,
        CancellationToken ct);
}
