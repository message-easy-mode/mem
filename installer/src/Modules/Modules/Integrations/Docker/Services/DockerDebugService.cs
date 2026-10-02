using Infrastructure.Docker;
using Modules.Integrations.Docker.Contracts;

namespace Modules.Integrations.Docker.Services;

public sealed class DockerDebugService(
    IDockerHost dockerHost
)
{
    public async Task<DockerPingResponse> PingAsync(CancellationToken ct)
    {
        var ok = await dockerHost.PingAsync(ct);
        return new DockerPingResponse(ok);
    }

    public async Task<DockerContainersResponse> ListContainersAsync(CancellationToken ct)
    {
        var items = await dockerHost.ListContainersAsync(ct);
        return new DockerContainersResponse(items);
    }

    public async Task<DockerContainersResponse> ListByPrefixAsync(
        string prefix,
        CancellationToken ct)
    {
        var items = await dockerHost.ListByPrefixAsync(prefix, ct);
        return new DockerContainersResponse(items);
    }

    public async Task<DockerInspectResponse> InspectByNameAsync(
        string name,
        CancellationToken ct)
    {
        var item = await dockerHost.InspectByNameAsync(name, ct);
        return new DockerInspectResponse(item);
    }
}