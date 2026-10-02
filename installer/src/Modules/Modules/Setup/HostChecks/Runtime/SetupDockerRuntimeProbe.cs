using Docker.DotNet;
using Docker.DotNet.Models;

namespace Modules.Setup.HostChecks.Runtime;

/// <summary>
/// Bounded Docker Engine evidence used by first-time setup and host checks.
///
/// The Control Plane talks to the host Docker daemon through Docker.DotNet and
/// the configured daemon endpoint (normally the mounted Unix socket). Setup
/// code must not require a docker CLI executable inside the Control Plane image.
/// </summary>
public interface ISetupDockerRuntimeProbe
{
    Task<SetupDockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(
        CancellationToken cancellationToken);
}

public sealed record SetupDockerSystemInfo(
    string ServerVersion,
    string DockerRootDir,
    string OperatingSystem,
    string Architecture,
    long MemoryBytes,
    long CpuCount,
    long ContainerCount,
    long ImageCount);

public sealed record SetupDockerContainer(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<SetupDockerPortBinding>? Ports = null);

public sealed record SetupDockerPortBinding(
    uint PrivatePort,
    uint PublicPort,
    string Type,
    string Ip);

public sealed record SetupDockerVolume(
    string Name,
    string Driver);

public sealed record SetupDockerNetwork(
    string Name,
    string Driver);

public sealed class DockerDotNetSetupDockerRuntimeProbe(DockerClient docker)
    : ISetupDockerRuntimeProbe
{
    public async Task<SetupDockerSystemInfo> GetSystemInfoAsync(
        CancellationToken cancellationToken)
    {
        var info = await docker.System.GetSystemInfoAsync(cancellationToken);

        return new SetupDockerSystemInfo(
            ServerVersion: info.ServerVersion ?? string.Empty,
            DockerRootDir: info.DockerRootDir ?? string.Empty,
            OperatingSystem: info.OperatingSystem ?? string.Empty,
            Architecture: info.Architecture ?? string.Empty,
            MemoryBytes: info.MemTotal,
            CpuCount: info.NCPU,
            ContainerCount: info.Containers,
            ImageCount: info.Images);
    }

    public async Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
        CancellationToken cancellationToken)
    {
        var items = await docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true
            },
            cancellationToken);

        return items
            .Select(item => new SetupDockerContainer(
                Id: item.ID ?? string.Empty,
                Name: item.Names?.FirstOrDefault()?.TrimStart('/') ?? string.Empty,
                Image: item.Image ?? string.Empty,
                State: item.State ?? string.Empty,
                Status: item.Status ?? string.Empty,
                Labels: item.Labels is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(
                        item.Labels,
                        StringComparer.Ordinal),
                Ports: item.Ports?.Select(port => new SetupDockerPortBinding(
                        PrivatePort: port.PrivatePort,
                        PublicPort: port.PublicPort,
                        Type: port.Type ?? string.Empty,
                        Ip: port.IP ?? string.Empty))
                    .ToArray() ?? []))
            .ToArray();
    }

    public async Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(
        CancellationToken cancellationToken)
    {
        var response = await docker.Volumes.ListAsync(
            new VolumesListParameters(),
            cancellationToken);

        return (response.Volumes ?? [])
            .Select(volume => new SetupDockerVolume(
                Name: volume.Name ?? string.Empty,
                Driver: volume.Driver ?? string.Empty))
            .ToArray();
    }

    public async Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(
        CancellationToken cancellationToken)
    {
        var response = await docker.Networks.ListNetworksAsync(
            new NetworksListParameters(),
            cancellationToken);

        return response
            .Select(network => new SetupDockerNetwork(
                Name: network.Name ?? string.Empty,
                Driver: network.Driver ?? string.Empty))
            .ToArray();
    }
}
