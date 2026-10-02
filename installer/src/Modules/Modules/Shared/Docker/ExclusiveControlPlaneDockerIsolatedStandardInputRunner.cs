using Infrastructure.Docker;

namespace Modules.Shared.Docker;

/// <summary>
/// Keeps isolated one-shot Docker mutations behind the same exclusive Control
/// Plane ownership guard used by the normal IDockerHost mutation surface.
/// </summary>
public sealed class ExclusiveControlPlaneDockerIsolatedStandardInputRunner(
    DockerIsolatedStandardInputRunner inner,
    IControlPlaneDockerOwnershipGuard ownershipGuard)
    : IDockerIsolatedStandardInputRunner
{
    public async Task<DockerIsolatedStandardInputResult> RunAsync(
        string image,
        string containerName,
        IReadOnlyList<string> command,
        ReadOnlyMemory<char> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await ownershipGuard.EnsureMutationAllowedAsync(cancellationToken);
        return await inner.RunAsync(
            image,
            containerName,
            command,
            standardInput,
            timeout,
            cancellationToken);
    }
}
