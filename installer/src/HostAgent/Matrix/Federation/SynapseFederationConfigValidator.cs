using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Matrix.Runtime;
using HostAgent.Runtime.Manifests;

namespace HostAgent.Matrix.Federation;

public sealed record SynapseFederationCandidateValidationResult(
    bool Valid,
    int? ExitCode,
    string Image,
    bool ImagePinned,
    string? LogTail,
    string? ErrorCode,
    string Detail);

public interface ISynapseFederationConfigCandidateValidator
{
    Task<SynapseFederationCandidateValidationResult> ValidateAsync(
        RuntimeStackManifest manifest,
        RuntimeMatrixContainerObservation container,
        string candidatePath,
        CancellationToken ct);
}

public sealed class SynapseFederationConfigCandidateValidator
    : ISynapseFederationConfigCandidateValidator
{
    private static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(60);
    private readonly DockerClient _docker;

    public SynapseFederationConfigCandidateValidator(DockerClient docker)
    {
        _docker = docker;
    }

    public async Task<SynapseFederationCandidateValidationResult> ValidateAsync(
        RuntimeStackManifest manifest,
        RuntimeMatrixContainerObservation container,
        string candidatePath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(container);

        var dataPath = RequireOwnedDataPath(manifest.Matrix, candidatePath);
        var relative = Path.GetRelativePath(dataPath, Path.GetFullPath(candidatePath));
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidOperationException(
                "The federation candidate path is outside the manifest-owned Matrix data directory.");
        }

        await _docker.Images.InspectImageAsync(container.Image, ct);

        var containerPath = "/data/" + relative
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
        var validationName = "mem-federation-validate-" + Guid.NewGuid().ToString("N")[..12];
        string? validationContainerId = null;
        string? logs = null;
        int? exitCode = null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ValidationTimeout);

        try
        {
            var create = await _docker.Containers.CreateContainerAsync(
                new CreateContainerParameters
                {
                    Name = validationName,
                    Image = container.Image,
                    User = string.IsNullOrWhiteSpace(container.User) ? null : container.User,
                    Entrypoint = ["python"],
                    Cmd = ["-m", "synapse.config", "-c", containerPath],
                    NetworkDisabled = true,
                    Labels = new Dictionary<string, string>
                    {
                        ["mem.component"] = "federation-config-validation",
                        ["mem.managed-by"] = "host-agent"
                    },
                    HostConfig = new HostConfig
                    {
                        AutoRemove = false,
                        NetworkMode = "none",
                        Binds = [$"{dataPath}:/data:ro"]
                    }
                },
                timeout.Token);

            validationContainerId = create.ID;
            if (string.IsNullOrWhiteSpace(validationContainerId))
            {
                throw new InvalidOperationException(
                    "Docker returned no container identity for Synapse config validation.");
            }

            var started = await _docker.Containers.StartContainerAsync(
                validationContainerId,
                new ContainerStartParameters(),
                timeout.Token);
            if (!started)
            {
                throw new InvalidOperationException(
                    "Docker did not start the Synapse config validation container.");
            }

            var wait = await _docker.Containers.WaitContainerAsync(
                validationContainerId,
                timeout.Token);
            exitCode = checked((int)wait.StatusCode);
            logs = await GetSanitisedLogsAsync(validationContainerId, timeout.Token);

            var valid = exitCode == 0;
            return new SynapseFederationCandidateValidationResult(
                Valid: valid,
                ExitCode: exitCode,
                Image: container.Image,
                ImagePinned: IsPinned(container.Image),
                LogTail: logs,
                ErrorCode: valid ? null : "federation_config_validation_failed",
                Detail: valid
                    ? "The candidate configuration was accepted by the active Synapse image."
                    : "The active Synapse image rejected the candidate configuration.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SynapseFederationCandidateValidationResult(
                Valid: false,
                ExitCode: exitCode,
                Image: container.Image,
                ImagePinned: IsPinned(container.Image),
                LogTail: logs,
                ErrorCode: "federation_config_validation_failed",
                Detail: "Synapse candidate validation exceeded its bounded timeout.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(validationContainerId))
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await _docker.Containers.RemoveContainerAsync(
                        validationContainerId,
                        new ContainerRemoveParameters
                        {
                            Force = true,
                            RemoveVolumes = true
                        },
                        cleanup.Token);
                }
                catch
                {
                    // Validation outcome remains authoritative. Cleanup failure is
                    // deliberately not allowed to expose configuration content.
                }
            }
        }
    }

    internal static string SanitiseLogTail(string? logs)
    {
        if (string.IsNullOrWhiteSpace(logs))
        {
            return string.Empty;
        }

        var sensitiveMarkers = new[]
        {
            "secret",
            "password",
            "token",
            "private_key",
            "signing_key",
            "registration_shared_secret",
            "macaroon"
        };
        var safeLines = logs
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => sensitiveMarkers.Any(marker =>
                    line.Contains(marker, StringComparison.OrdinalIgnoreCase))
                ? "[redacted sensitive Synapse config validation line]"
                : line)
            .ToArray();
        var safe = string.Join("\n", safeLines).Trim();
        return safe.Length <= 2000 ? safe : safe[^2000..];
    }

    private async Task<string> GetSanitisedLogsAsync(
        string containerId,
        CancellationToken ct)
    {
        using var stream = await _docker.Containers.GetContainerLogsAsync(
            containerId,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false,
                Timestamps = false,
                Tail = "100"
            },
            ct);
        using var reader = new StreamReader(stream);
        return SanitiseLogTail(await reader.ReadToEndAsync(ct));
    }

    private static string RequireOwnedDataPath(
        RuntimeStackServiceManifest matrix,
        string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(matrix.DataPath))
        {
            throw new InvalidOperationException(
                "The Runtime Stack manifest does not contain the Matrix data directory.");
        }

        if (string.IsNullOrWhiteSpace(candidatePath) || !File.Exists(candidatePath))
        {
            throw new InvalidOperationException(
                "The federation candidate configuration file does not exist.");
        }

        var dataPath = Path.GetFullPath(matrix.DataPath);
        var candidate = Path.GetFullPath(candidatePath);
        var prefix = dataPath.EndsWith(Path.DirectorySeparatorChar)
            ? dataPath
            : dataPath + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The federation candidate is outside the manifest-owned Matrix data directory.");
        }

        return dataPath;
    }

    private static bool IsPinned(string image) =>
        image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase) ||
        (!image.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) &&
         image.Contains(':', StringComparison.Ordinal));
}
