using Docker.DotNet;
using Docker.DotNet.Models;

namespace Modules.Shared.RuntimeImages;

public sealed class DockerRuntimeImageInspector(DockerClient docker) : IRuntimeImageInspector
{
    public async Task<RuntimeImageInspection?> InspectAsync(
        string immutableReference,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await docker.Images.InspectImageAsync(immutableReference, cancellationToken);
            return new RuntimeImageInspection(
                image.ID ?? string.Empty,
                image.RepoDigests?.ToArray() ?? [],
                image.Config?.Env?.ToArray() ?? []);
        }
        catch (DockerImageNotFoundException)
        {
            return null;
        }
    }

    public async Task PullAsync(
        string immutableReference,
        CancellationToken cancellationToken)
    {
        var progress = new Progress<JSONMessage>();
        await docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = immutableReference },
            new AuthConfig(),
            progress,
            cancellationToken);
    }
}
