using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Manifests;

namespace HostAgent.Matrix.Runtime;

public sealed record RuntimeMatrixContainerObservation(
    string ContainerId,
    string ContainerName,
    string Image,
    string? User,
    bool Running,
    bool IdentityMatches,
    bool DataBindMatches,
    bool ExpectedNetworkAttached,
    bool DirectHostPortExposed);

public interface IRuntimeMatrixContainerLifecycleService
{
    Task<RuntimeMatrixContainerObservation> InspectAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct);

    Task<RuntimeMatrixContainerObservation> RestartAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct);

    Task<string> GetBoundedLogsAsync(
        string containerId,
        int tail,
        CancellationToken ct);
}

public sealed class RuntimeMatrixContainerLifecycleService
    : IRuntimeMatrixContainerLifecycleService
{
    private static readonly TimeSpan RunningTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollDelay = TimeSpan.FromSeconds(2);
    private readonly DockerClient _docker;

    public RuntimeMatrixContainerLifecycleService(DockerClient docker)
    {
        _docker = docker;
    }

    public async Task<RuntimeMatrixContainerObservation> InspectAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var identity = !string.IsNullOrWhiteSpace(matrix.ContainerId)
            ? matrix.ContainerId.Trim()
            : matrix.ContainerName?.Trim();
        if (string.IsNullOrWhiteSpace(identity))
        {
            throw new InvalidOperationException(
                "The Runtime Stack manifest does not identify the Matrix container.");
        }

        var inspect = await _docker.Containers.InspectContainerAsync(identity, ct);
        var actualName = (inspect.Name ?? string.Empty).TrimStart('/');
        var expectedName = matrix.ContainerName?.Trim();
        var idMatches = string.IsNullOrWhiteSpace(matrix.ContainerId) ||
            IdsMatch(inspect.ID, matrix.ContainerId.Trim());
        var nameMatches = string.IsNullOrWhiteSpace(expectedName) ||
            string.Equals(actualName, expectedName, StringComparison.OrdinalIgnoreCase);

        var dataPath = matrix.DataPath?.Trim();
        var dataBindMatches = !string.IsNullOrWhiteSpace(dataPath) &&
            inspect.Mounts?.Any(mount =>
                string.Equals(mount.Destination, "/data", StringComparison.Ordinal) &&
                PathsEqual(mount.Source, dataPath)) == true;

        var expectedNetwork = matrix.RuntimeMetadata.TryGetValue(
            "runtimeNetworkName",
            out var network)
            ? network?.Trim()
            : null;
        var networkAttached = !string.IsNullOrWhiteSpace(expectedNetwork) &&
            inspect.NetworkSettings?.Networks?.ContainsKey(expectedNetwork) == true;
        var directPort = inspect.HostConfig?.PortBindings?.Any(binding =>
            binding.Value is not null && binding.Value.Count > 0) == true;

        var image = (inspect.Image ?? inspect.Config?.Image)?.Trim();
        if (string.IsNullOrWhiteSpace(image))
        {
            throw new InvalidOperationException(
                "The running Matrix container does not expose its image identity.");
        }

        return new RuntimeMatrixContainerObservation(
            ContainerId: inspect.ID,
            ContainerName: actualName,
            Image: image,
            User: inspect.Config?.User,
            Running: inspect.State?.Running == true,
            IdentityMatches: idMatches && nameMatches,
            DataBindMatches: dataBindMatches,
            ExpectedNetworkAttached: networkAttached,
            DirectHostPortExposed: directPort);
    }

    public async Task<RuntimeMatrixContainerObservation> RestartAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct)
    {
        var before = await InspectAsync(matrix, ct);
        EnsureSafeIdentity(before);

        if (before.Running)
        {
            await _docker.Containers.StopContainerAsync(
                before.ContainerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 30 },
                ct);
        }

        var started = await _docker.Containers.StartContainerAsync(
            before.ContainerId,
            new ContainerStartParameters(),
            ct);
        if (!started)
        {
            throw new InvalidOperationException(
                "Docker did not confirm that the Matrix container started.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        while (DateTimeOffset.UtcNow - startedAt < RunningTimeout)
        {
            ct.ThrowIfCancellationRequested();
            var observation = await InspectAsync(matrix, ct);
            if (observation.Running)
            {
                EnsureSafeIdentity(observation);
                return observation;
            }

            await Task.Delay(PollDelay, ct);
        }

        throw new TimeoutException(
            "The Matrix container did not return to a running state within the restart timeout.");
    }

    public async Task<string> GetBoundedLogsAsync(
        string containerId,
        int tail,
        CancellationToken ct)
    {
        var safeTail = Math.Clamp(tail, 1, 200);
        using var stream = await _docker.Containers.GetContainerLogsAsync(
            containerId,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false,
                Timestamps = false,
                Tail = safeTail.ToString()
            },
            ct);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(ct);
        return text.Length <= 4000 ? text : text[^4000..];
    }

    private static void EnsureSafeIdentity(RuntimeMatrixContainerObservation observation)
    {
        if (!observation.IdentityMatches)
        {
            throw new InvalidOperationException(
                "The inspected Matrix container does not match the Runtime Stack manifest.");
        }

        if (!observation.DataBindMatches)
        {
            throw new InvalidOperationException(
                "The Matrix container does not use the manifest-owned data directory at /data.");
        }

        if (!observation.ExpectedNetworkAttached)
        {
            throw new InvalidOperationException(
                "The Matrix container is not attached to the expected MEM runtime network.");
        }

        if (observation.DirectHostPortExposed)
        {
            throw new InvalidOperationException(
                "The Matrix container exposes a direct Docker host port.");
        }
    }

    private static bool IdsMatch(string? actual, string expected) =>
        !string.IsNullOrWhiteSpace(actual) &&
        (actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase) ||
         expected.StartsWith(actual, StringComparison.OrdinalIgnoreCase));

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim()),
                Path.GetFullPath(right.Trim()),
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
