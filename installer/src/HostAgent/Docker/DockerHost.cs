using Docker.DotNet;
using Docker.DotNet.Models;
using System.Net;
using System.Globalization;
using HostAgent.Docker.Models;

namespace HostAgent.Docker;

public interface IDockerHost
{
    Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct);
    Task StartContainerAsync(string containerId, CancellationToken ct);
    Task StopContainerAsync(string containerId, CancellationToken ct);
    Task RemoveContainerAsync(string containerId, bool force, CancellationToken ct);

    Task<DockerContainerInspection> InspectAsync(string containerId, CancellationToken ct);
    Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct);

    Task<int> WaitForExitAsync(string containerId, CancellationToken ct);
    Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct);

    Task EnsureNetworkConnectedAsync(string containerId, string networkName, string? alias, CancellationToken ct);
}

public sealed class DockerHost : IDockerHost, IDisposable
{
    private readonly DockerClient _client;

    public DockerHost(DockerClient client)
    {
        _client = client;
    }

    public async Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
            throw new ArgumentException("Container name is required.", nameof(spec));

        if (string.IsNullOrWhiteSpace(spec.Image))
            throw new ArgumentException("Image is required.", nameof(spec));

        await PullImageIfMissingAsync(spec.Image, ct);

        var env = spec.Env?.Select(kv => $"{kv.Key}={kv.Value}").ToList();

        var hostConfig = new HostConfig
        {
            AutoRemove = spec.AutoRemove,
            RestartPolicy = ToRestartPolicy(spec.RestartPolicy)
        };

        // ✅ Bind mounts
        if (spec.BindMounts is not null && spec.BindMounts.Count > 0)
        {
            hostConfig.Binds = spec.BindMounts
                .Select(m => $"{m.HostPath}:{m.ContainerPath}" + (m.ReadOnly ? ":ro" : ""))
                .ToList();
        }

        IDictionary<string, IList<PortBinding>>? portBindings = null;
        IDictionary<string, EmptyStruct>? exposedPorts = null;

        if (spec.PortBindings is not null && spec.PortBindings.Count > 0)
        {
            portBindings = new Dictionary<string, IList<PortBinding>>();
            exposedPorts = new Dictionary<string, EmptyStruct>();

            foreach (var kv in spec.PortBindings)
            {
                var containerPort = kv.Key.Trim(); // e.g. "8008/tcp"
                var hostPort = kv.Value.Trim();    // e.g. "28008"

                if (containerPort.Length == 0 || hostPort.Length == 0)
                    throw new ArgumentException("PortBindings must be non-empty.");

                exposedPorts[containerPort] = default;

                portBindings[containerPort] = new List<PortBinding>
                {
                    new PortBinding { HostPort = hostPort }
                };
            }

            hostConfig.PortBindings = portBindings;
        }

        var createParams = new CreateContainerParameters
        {
            Name = spec.Name,
            Image = spec.Image,
            Env = env,
            Labels = spec.Labels is null ? null : new Dictionary<string, string>(spec.Labels),
            Cmd = spec.Cmd?.ToList(),
            ExposedPorts = exposedPorts,
            HostConfig = hostConfig,

            // ✅ Optional: user
            User = string.IsNullOrWhiteSpace(spec.User) ? null : spec.User
        };

        // ✅ Option B: attach to docker network at creation time (if specified)
        if (!string.IsNullOrWhiteSpace(spec.NetworkName))
        {
            var aliases = (spec.NetworkAliases ?? Array.Empty<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToList();

            createParams.NetworkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings>
                {
                    [spec.NetworkName] = new EndpointSettings
                    {
                        Aliases = aliases.Count == 0 ? null : aliases
                    }
                }
            };
        }

        var res = await _client.Containers.CreateContainerAsync(createParams, ct);

        if (string.IsNullOrWhiteSpace(res.ID))
            throw new InvalidOperationException("Docker returned empty container ID.");

        return res.ID;
    }

    public async Task StartContainerAsync(string containerId, CancellationToken ct)
    {
        var ok = await _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);
        if (!ok) throw new InvalidOperationException($"Failed to start container {containerId}.");
    }

    public async Task StopContainerAsync(string containerId, CancellationToken ct)
    {
        await _client.Containers.StopContainerAsync(
            containerId,
            new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
            ct
        );
    }

    public async Task RemoveContainerAsync(string containerId, bool force, CancellationToken ct)
    {
        await _client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters
        {
            Force = force,
            RemoveVolumes = false
        }, ct);
    }

    public async Task<DockerContainerInspection> InspectAsync(string containerId, CancellationToken ct)
    {
        var info = await _client.Containers.InspectContainerAsync(containerId, ct);

        var state = info.State;
        var running = state?.Running ?? false;
        var status = state?.Status ?? "unknown";
        var health = state?.Health?.Status;

        var networks = info.NetworkSettings?.Networks is null
            ? []
            : info.NetworkSettings.Networks
                .Select(item => new DockerNetworkAttachmentInspection(
                    item.Key,
                    (item.Value?.Aliases ?? [])
                        .Where(alias => !string.IsNullOrWhiteSpace(alias))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(alias => alias, StringComparer.OrdinalIgnoreCase)
                        .ToArray()))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var bindMounts = (info.Mounts ?? [])
            .Where(mount =>
                !string.IsNullOrWhiteSpace(mount.Source) &&
                !string.IsNullOrWhiteSpace(mount.Destination))
            .Select(mount => new DockerBindMountInspection(
                mount.Source,
                mount.Destination,
                ReadOnly: !mount.RW))
            .OrderBy(mount => mount.Destination, StringComparer.Ordinal)
            .ToArray();

        return new DockerContainerInspection(
            Id: info.ID,
            Name: (info.Name ?? "").TrimStart('/'),
            Running: running,
            Status: status,
            HealthStatus: health)
        {
            ImageId = NormalizeOptional(info.Image),
            ConfiguredImage = NormalizeOptional(info.Config?.Image),
            Restarting = state?.Restarting ?? false,
            Paused = state?.Paused ?? false,
            OomKilled = state?.OOMKilled ?? false,
            Dead = state?.Dead ?? false,
            ExitCode = state?.ExitCode ?? 0,
            StateErrorPresent = !string.IsNullOrWhiteSpace(state?.Error),
            StartedAtUtc = NormalizeDockerTimestamp(state?.StartedAt),
            FinishedAtUtc = NormalizeDockerTimestamp(state?.FinishedAt),
            RestartCount = info.RestartCount,
            RestartPolicy = NormalizeRestartPolicy(info.HostConfig?.RestartPolicy?.Name),
            NetworkMode = NormalizeOptional(info.HostConfig?.NetworkMode),
            Networks = networks,
            BindMounts = bindMounts,
            Command = (info.Config?.Cmd ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray(),
            User = NormalizeOptional(info.Config?.User)
        };
    }

    public async Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct)
    {
        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["label"] = new Dictionary<string, bool> { [$"{labelKey}={labelValue}"] = true }
        };

        var items = await _client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = filters
        }, ct);

        return items.Select(x => new DockerContainerSummary(
            Id: x.ID,
            Name: x.Names?.FirstOrDefault()?.TrimStart('/') ?? "",
            Status: x.Status ?? ""
        )).ToList();
    }

    public async Task<int> WaitForExitAsync(string containerId, CancellationToken ct)
    {
        var res = await _client.Containers.WaitContainerAsync(containerId, ct);
        return (int)res.StatusCode;
    }

    public async Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct)
    {
        tail = Math.Clamp(tail, 1, 5000);

        using var stream = await _client.Containers.GetContainerLogsAsync(
            containerId,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false,
                Timestamps = false,
                Tail = tail.ToString()
            },
            ct
        );

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }

    private async Task PullImageIfMissingAsync(string image, CancellationToken ct)
    {
        var trimmed = image.Trim();

        try
        {
            await _client.Images.InspectImageAsync(trimmed, ct);
            return;
        }
        catch (DockerImageNotFoundException)
        {
            // Handle the missing image below.
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Handle the missing image below.
        }

        if (DockerImageReferencePolicy.IsLocalImmutableImageId(trimmed))
        {
            throw new InvalidOperationException(
                $"Docker image '{trimmed}' is not available locally. " +
                "Immutable local image IDs cannot be pulled; provision the approved runtime image before starting this operation.");
        }

        var (repo, tag) = SplitImage(trimmed);

        await _client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repo, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken: ct
        );
    }

    private static (string repo, string tag) SplitImage(string image)
    {
        var trimmed = image.Trim();
        var idx = trimmed.LastIndexOf(':');

        if (idx > -1 && idx > trimmed.LastIndexOf('/'))
            return (trimmed[..idx], trimmed[(idx + 1)..]);

        return (trimmed, "latest");
    }

    private static DateTimeOffset? NormalizeDockerTimestamp(string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                normalized,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
        {
            return null;
        }

        return timestamp == default
            ? null
            : timestamp.ToUniversalTime();
    }

    private static string NormalizeRestartPolicy(RestartPolicyKind? policy) =>
        policy switch
        {
            RestartPolicyKind.Always => "always",
            RestartPolicyKind.UnlessStopped => "unless-stopped",
            RestartPolicyKind.OnFailure => "on-failure",
            RestartPolicyKind.No => "no",
            _ => "unknown"
        };

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static RestartPolicy ToRestartPolicy(DockerRestartPolicy? rp)
    {
        if (rp is null || rp.Name == DockerRestartPolicyName.No)
            return new RestartPolicy { Name = RestartPolicyKind.No };

        return rp.Name switch
        {
            DockerRestartPolicyName.Always => new RestartPolicy { Name = RestartPolicyKind.Always },

            DockerRestartPolicyName.UnlessStopped => new RestartPolicy { Name = RestartPolicyKind.UnlessStopped },

            DockerRestartPolicyName.OnFailure => new RestartPolicy
            {
                Name = RestartPolicyKind.OnFailure,
                MaximumRetryCount = rp.MaximumRetryCount ?? 0
            },

            _ => new RestartPolicy { Name = RestartPolicyKind.No }
        };
    }

    public void Dispose()
    {
        // Do not dispose the injected DockerClient here.
        //
        // DockerClient is registered by DI and may be shared across HostAgent,
        // readiness checks, route verification, Docker debug endpoints, and
        // runtime services. Disposing it from this wrapper can dispose the
        // underlying Docker.DotNet HttpClient while other scoped services still
        // need it, causing:
        //
        //   ObjectDisposedException: System.Net.Http.HttpClient
        //
        // The DI container owns the DockerClient lifetime.
    }

    public async Task EnsureNetworkConnectedAsync(string containerId, string networkName, string? alias, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerId))
            throw new ArgumentException("containerId required", nameof(containerId));

        if (string.IsNullOrWhiteSpace(networkName))
            throw new ArgumentException("networkName required", nameof(networkName));

        // Inspect container to see if already connected
        var info = await _client.Containers.InspectContainerAsync(containerId, ct);
        var networks = info?.NetworkSettings?.Networks;

        if (networks is not null && networks.ContainsKey(networkName))
        {
            // Already connected. (Docker doesn't provide a clean "update aliases" flow.)
            return;
        }

        // Ensure network exists (better error message)
        try
        {
            _ = await _client.Networks.InspectNetworkAsync(networkName, ct);
        }
        catch (DockerApiException ex)
        {
            throw new InvalidOperationException(
                $"Docker network '{networkName}' does not exist. Ensure your docker-compose created it (or create it manually).",
                ex);
        }

        var aliases = string.IsNullOrWhiteSpace(alias) ? null : new List<string> { alias! };

        await _client.Networks.ConnectNetworkAsync(
            networkName,
            new NetworkConnectParameters
            {
                Container = containerId,
                EndpointConfig = new EndpointSettings
                {
                    Aliases = aliases
                }
            },
            ct
        );
    }
}
internal static class DockerImageReferencePolicy
{
    private const string Sha256Prefix = "sha256:";
    private const int Sha256HexLength = 64;

    internal static bool IsLocalImmutableImageId(string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
            return false;

        var trimmed = image.Trim();
        if (!trimmed.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var hex = trimmed[Sha256Prefix.Length..];
        return hex.Length == Sha256HexLength && hex.All(Uri.IsHexDigit);
    }
}

