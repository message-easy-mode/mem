using System.Runtime.InteropServices;
using Infrastructure.Docker;
using Modules.Shared.RuntimeImages;

namespace Modules.Integrations.Portainer.Services;

public sealed record PortainerResolvedRuntimeImage(
    string ApprovedReference,
    string ResolvedImageId,
    IReadOnlyList<string> RepositoryDigests,
    string ExpectedVersion,
    string Architecture,
    bool PulledDuringPreparation);

public interface IPortainerHostArchitectureReader
{
    Architecture Current { get; }
}

public sealed class PortainerHostArchitectureReader : IPortainerHostArchitectureReader
{
    public Architecture Current => RuntimeInformation.OSArchitecture;
}

public static class PortainerArchitecturePolicy
{
    public static string ToDockerArchitecture(Architecture architecture) =>
        architecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => throw new PortainerOperationException(
                "portainer_architecture_unsupported",
                $"Portainer is not approved for host architecture '{architecture}'.")
        };
}

public sealed class PortainerRuntimeImageProvider(
    PortainerRuntimeOptions options,
    IRuntimeImageInspector imageInspector,
    IDockerHost dockerHost,
    IPortainerHostArchitectureReader architectureReader)
{
    public Task<PortainerResolvedRuntimeImage> ResolveForOperationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync(
            allowPreparationPull: false,
            cancellationToken);

    public Task<PortainerResolvedRuntimeImage> PrepareForInstallationAsync(
        CancellationToken cancellationToken) =>
        ResolveAsync(
            allowPreparationPull: true,
            cancellationToken);

    private async Task<PortainerResolvedRuntimeImage> ResolveAsync(
        bool allowPreparationPull,
        CancellationToken cancellationToken)
    {
        PortainerRuntimeOptionsValidator.ThrowIfInvalid(options);

        var architecture = PortainerArchitecturePolicy.ToDockerArchitecture(
            architectureReader.Current);
        if (!options.SupportedArchitectures.Contains(
                architecture,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new PortainerOperationException(
                "portainer_architecture_unsupported",
                $"Portainer is not approved for Docker architecture '{architecture}'.");
        }

        var inspection = await imageInspector.InspectAsync(
            options.ApprovedImageReference,
            cancellationToken);
        var pulled = false;

        if (inspection is null && allowPreparationPull)
        {
            await dockerHost.PullImageAsync(
                options.ApprovedImageReference,
                cancellationToken);
            pulled = true;
            inspection = await imageInspector.InspectAsync(
                options.ApprovedImageReference,
                cancellationToken);
        }

        if (inspection is null)
        {
            throw new PortainerOperationException(
                "portainer_approved_image_missing",
                "The approved Portainer image is not available locally. Operational requests never pull images; prepare it through the explicit installation workflow.");
        }

        var imageId = NormalizeImageId(inspection.ImageId);
        if (imageId is null)
        {
            throw new PortainerOperationException(
                "portainer_approved_image_invalid",
                "Docker did not return a valid immutable local image identity for the approved Portainer runtime.");
        }

        return new PortainerResolvedRuntimeImage(
            options.ApprovedImageReference,
            imageId,
            inspection.RepositoryDigests,
            options.ExpectedVersion,
            architecture,
            pulled);
    }

    private static string? NormalizeImageId(string? value)
    {
        var imageId = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return imageId.StartsWith("sha256:", StringComparison.Ordinal) &&
               imageId.Length == "sha256:".Length + 64 &&
               imageId["sha256:".Length..].All(char.IsAsciiHexDigit)
            ? imageId
            : null;
    }
}

public sealed class PortainerOperationException(
    string code,
    string message) : Exception(message)
{
    public string Code { get; } = code;
}
