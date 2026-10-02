using System.Formats.Tar;
using System.Security.Cryptography;
using Docker.DotNet;
using Docker.DotNet.Models;
using Infrastructure.Docker.Models;

namespace Infrastructure.Docker;

public sealed class DockerHost(DockerClient client) : IDockerHost
{
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            await client.System.PingAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct)
    {
        var items = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            ct);

        return items
            .Select(ToSummary)
            .ToList();
    }

    public async Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(
        string namePrefix,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(namePrefix))
            return [];

        var items = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            ct);

        return items
            .Where(x => (x.Names ?? [])
                .Select(n => n.TrimStart('/'))
                .Any(n => n.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase)))
            .Select(ToSummary)
            .ToList();
    }

    public async Task<DockerContainerInspection?> InspectByNameAsync(
        string containerName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            return null;

        var items = await client.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            ct);

        var match = items.FirstOrDefault(x =>
            (x.Names ?? [])
                .Select(n => n.TrimStart('/'))
                .Any(n => string.Equals(n, containerName, StringComparison.OrdinalIgnoreCase)));

        if (match is null)
            return null;

        var info = await client.Containers.InspectContainerAsync(match.ID, ct);

        return new DockerContainerInspection(
            Id: info.ID,
            Name: (info.Name ?? string.Empty).TrimStart('/'),
            Image: info.Config?.Image ?? string.Empty,
            State: info.State?.Status ?? "unknown",
            Running: info.State?.Running ?? false,
            Ports: ExtractPorts(info))
        {
            Labels = info.Config?.Labels is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(
                    info.Config.Labels,
                    StringComparer.Ordinal),
            EnvironmentVariableNames = ExtractEnvironmentVariableNames(info.Config?.Env)
        };
    }

    public async Task<string> CreateContainerAsync(
    DockerContainerSpec spec,
    CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spec.Name))
            throw new ArgumentException("Container name is required.", nameof(spec));

        if (string.IsNullOrWhiteSpace(spec.Image))
            throw new ArgumentException("Image is required.", nameof(spec));

        var imageExists = await ImageExistsAsync(spec.Image, ct);
        if (!imageExists)
        {
            await PullImageAsync(spec.Image, ct);
        }

        if (!string.IsNullOrWhiteSpace(spec.NetworkName))
        {
            await EnsureNetworkAsync(spec.NetworkName, ct);
        }

        var env = spec.Environment?
            .Select(kv => $"{kv.Key}={kv.Value}")
            .ToList();

        var hostConfig = new HostConfig
        {
            AutoRemove = false,
            RestartPolicy = ToRestartPolicy(spec.RestartPolicy),
            LogConfig = spec.DisableLogging
                ? new LogConfig { Type = "none" }
                : null
        };

        if (spec.PortBindings.Count > 0)
        {
            hostConfig.PortBindings = spec.PortBindings.ToDictionary(
                kv => kv.Key,
                kv => (IList<PortBinding>)new List<PortBinding>
                {
                    new()
                    {
                        HostPort = kv.Value
                    }
                });
        }

        var mountBindings = DockerMountBindingBuilder.Build(spec);
        if (mountBindings.Count > 0)
        {
            hostConfig.Binds = mountBindings.ToList();
        }

        NetworkingConfig? networkingConfig = null;

        if (!string.IsNullOrWhiteSpace(spec.NetworkName))
        {
            networkingConfig = new NetworkingConfig
            {
                EndpointsConfig = new Dictionary<string, EndpointSettings>
                {
                    [spec.NetworkName] = new EndpointSettings
                    {
                        Aliases = spec.NetworkAliases.Count == 0
                            ? null
                            : spec.NetworkAliases.ToList()
                    }
                }
            };
        }

        var create = new CreateContainerParameters
        {
            Name = spec.Name,
            Image = spec.Image,
            Env = env,
            Labels = spec.Labels.Count == 0
                ? null
                : new Dictionary<string, string>(spec.Labels),
            Cmd = spec.Command.Count == 0
                ? null
                : spec.Command.ToList(),
            HostConfig = hostConfig,
            NetworkingConfig = networkingConfig,
            ExposedPorts = spec.PortBindings.Count == 0
                ? null
                : spec.PortBindings.Keys.ToDictionary(k => k, _ => default(EmptyStruct))
        };

        var result = await client.Containers.CreateContainerAsync(create, ct);

        if (string.IsNullOrWhiteSpace(result.ID))
            throw new InvalidOperationException("Docker returned an empty container ID.");

        return result.ID;
    }

    public async Task StartContainerAsync(string containerId, CancellationToken ct)
    {
        var ok = await client.Containers.StartContainerAsync(
            containerId,
            new ContainerStartParameters(),
            ct);

        if (!ok)
            throw new InvalidOperationException($"Failed to start container '{containerId}'.");
    }

    public async Task StopContainerAsync(string containerId, CancellationToken ct)
    {
        await client.Containers.StopContainerAsync(
            containerId,
            new ContainerStopParameters
            {
                WaitBeforeKillSeconds = 10
            },
            ct);
    }

    public async Task RemoveContainerAsync(
        string containerId,
        bool force,
        bool removeVolumes,
        CancellationToken ct)
    {
        await client.Containers.RemoveContainerAsync(
            containerId,
            new ContainerRemoveParameters
            {
                Force = force,
                RemoveVolumes = removeVolumes
            },
            ct);
    }

    public async Task<string> GetLogsAsync(
        string containerId,
        int tail,
        CancellationToken ct)
    {
        tail = Math.Clamp(tail, 1, 5000);

        using var stream = await client.Containers.GetContainerLogsAsync(
            containerId,
            new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Follow = false,
                Timestamps = false,
                Tail = tail.ToString()
            },
            ct);

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(ct);
    }


    private static IReadOnlyList<string> ExtractEnvironmentVariableNames(
        IEnumerable<string>? environment)
    {
        if (environment is null)
            return [];

        return environment
            .Select(value =>
            {
                var separator = value.IndexOf('=');
                return separator < 0 ? value : value[..separator];
            })
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }


    public async Task EnsureVolumeAsync(string volumeName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(volumeName))
            throw new ArgumentException("Volume name is required.", nameof(volumeName));

        var response = await client.Volumes.ListAsync(
            new VolumesListParameters(),
            ct);

        var exists = (response.Volumes ?? []).Any(volume =>
            string.Equals(volume.Name, volumeName, StringComparison.OrdinalIgnoreCase));

        if (exists)
            return;

        await client.Volumes.CreateAsync(
            new VolumesCreateParameters
            {
                Name = volumeName
            },
            ct);
    }

    public async Task ConnectContainerToNetworkAsync(
        string containerIdOrName,
        string networkName,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerIdOrName))
            throw new ArgumentException("Container ID or name is required.", nameof(containerIdOrName));
        if (string.IsNullOrWhiteSpace(networkName))
            throw new ArgumentException("Network name is required.", nameof(networkName));

        await EnsureNetworkAsync(networkName, ct);

        var info = await client.Containers.InspectContainerAsync(containerIdOrName, ct);
        if (info.NetworkSettings?.Networks?.Keys.Any(name =>
                string.Equals(name, networkName, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return;
        }

        var networks = await client.Networks.ListNetworksAsync(
            new NetworksListParameters(),
            ct);
        var network = networks.FirstOrDefault(item =>
            string.Equals(item.Name, networkName, StringComparison.OrdinalIgnoreCase));

        if (network is null || string.IsNullOrWhiteSpace(network.ID))
        {
            throw new InvalidOperationException(
                $"Docker network '{networkName}' could not be resolved after ensure.");
        }

        await client.Networks.ConnectNetworkAsync(
            network.ID,
            new NetworkConnectParameters
            {
                Container = info.ID
            },
            ct);
    }

    public async Task CopyFileToContainerAsync(
        string containerIdOrName,
        string destinationDirectory,
        string fileName,
        ReadOnlyMemory<byte> content,
        UnixFileMode mode,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerIdOrName))
            throw new ArgumentException("Container ID or name is required.", nameof(containerIdOrName));
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !destinationDirectory.StartsWith('/'))
            throw new ArgumentException("Destination directory must be an absolute container path.", nameof(destinationDirectory));
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains('\\'))
            throw new ArgumentException("File name must be a single safe path component.", nameof(fileName));

        var payload = content.ToArray();
        try
        {
            using var archive = new MemoryStream();
            using (var data = new MemoryStream(payload, writable: false))
            using (var writer = new TarWriter(archive, leaveOpen: true))
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, fileName)
                {
                    DataStream = data,
                    Mode = mode
                };
                writer.WriteEntry(entry);
            }

            archive.Position = 0;
            await client.Containers.ExtractArchiveToContainerAsync(
                containerIdOrName,
                new ContainerPathStatParameters
                {
                    Path = destinationDirectory
                },
                archive,
                ct);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async Task<DockerExecResult> ExecAsync(
        string containerIdOrName,
        IReadOnlyList<string> command,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerIdOrName))
            throw new ArgumentException("Container ID or name is required.", nameof(containerIdOrName));
        ArgumentNullException.ThrowIfNull(command);
        if (command.Count == 0)
            throw new ArgumentException("At least one command argument is required.", nameof(command));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(timeout);
        var operationToken = timeoutSource.Token;

        try
        {
            var exec = await client.Exec.ExecCreateContainerAsync(
                containerIdOrName,
                new ContainerExecCreateParameters
                {
                    AttachStdout = true,
                    AttachStderr = true,
                    Cmd = command.ToArray()
                },
                operationToken);

            using var stream = await client.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                cancellationToken: operationToken);

            var output = await stream.ReadOutputToEndAsync(operationToken);
            var inspection = await client.Exec.InspectContainerExecAsync(
                exec.ID,
                operationToken);

            return new DockerExecResult(
                ExitCode: inspection.ExitCode <= int.MaxValue
                    ? (int)inspection.ExitCode
                    : -1,
                StandardOutput: TrimOutput(output.stdout),
                StandardError: TrimOutput(output.stderr),
                TimedOut: false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DockerExecResult(
                ExitCode: -1,
                StandardOutput: string.Empty,
                StandardError: "Docker container exec timed out.",
                TimedOut: true);
        }
    }

    private static string TrimOutput(string? value)
    {
        const int maxLength = 64 * 1024;
        var text = value ?? string.Empty;
        return text.Length <= maxLength
            ? text.Trim()
            : text[..maxLength].Trim() + "\n[output truncated]";
    }

    private static DockerContainerSummary ToSummary(ContainerListResponse x)
        => new(
            Id: x.ID,
            Name: x.Names?.FirstOrDefault()?.TrimStart('/') ?? string.Empty,
            Image: x.Image ?? string.Empty,
            State: x.State ?? string.Empty,
            Status: x.Status ?? string.Empty,
            Ports: x.Ports?.Select(p =>
                    new DockerPortBinding(
                        PrivatePort: p.PrivatePort,
                        PublicPort: p.PublicPort,
                        Type: p.Type ?? string.Empty,
                        Ip: p.IP ?? string.Empty))
                .ToList() ?? []
        );

    private static IReadOnlyList<DockerPortBinding> ExtractPorts(ContainerInspectResponse info)
    {
        var result = new List<DockerPortBinding>();

        var ports = info.NetworkSettings?.Ports;
        if (ports is null)
            return result;

        foreach (var entry in ports)
        {
            var key = entry.Key; // e.g. "5432/tcp"
            var bindings = entry.Value;

            var parts = key.Split('/');
            var privatePort = uint.TryParse(parts[0], out var pp) ? pp : 0;
            var type = parts.Length > 1 ? parts[1] : string.Empty;

            if (bindings is null || bindings.Count == 0)
            {
                result.Add(new DockerPortBinding(
                    PrivatePort: privatePort,
                    PublicPort: 0,
                    Type: type,
                    Ip: string.Empty));
                continue;
            }

            foreach (var binding in bindings)
            {
                _ = uint.TryParse(binding.HostPort, out var publicPort);

                result.Add(new DockerPortBinding(
                    PrivatePort: privatePort,
                    PublicPort: publicPort,
                    Type: type,
                    Ip: binding.HostIP ?? string.Empty));
            }
        }

        return result;
    }

    private static RestartPolicy ToRestartPolicy(Models.DockerRestartPolicy? policy)
    {
        if (policy is null || policy.Name == Models.DockerRestartPolicyName.No)
        {
            return new RestartPolicy
            {
                Name = RestartPolicyKind.No
            };
        }

        return policy.Name switch
        {
            Models.DockerRestartPolicyName.Always => new RestartPolicy
            {
                Name = RestartPolicyKind.Always
            },

            Models.DockerRestartPolicyName.UnlessStopped => new RestartPolicy
            {
                Name = RestartPolicyKind.UnlessStopped
            },

            Models.DockerRestartPolicyName.OnFailure => new RestartPolicy
            {
                Name = RestartPolicyKind.OnFailure,
                MaximumRetryCount = policy.MaximumRetryCount ?? 0
            },

            _ => new RestartPolicy
            {
                Name = RestartPolicyKind.No
            }
        };
    }

    public async Task PullImageAsync(string image, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(image))
            throw new ArgumentException("Image is required.", nameof(image));

        var (repository, tag) = SplitImage(image);

        await client.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = repository,
                Tag = tag
            },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken: ct);
    }

    public async Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
        string labelKey,
        string labelValue,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(labelKey))
            return [];

        if (string.IsNullOrWhiteSpace(labelValue))
            return [];

        var filters = new Dictionary<string, IDictionary<string, bool>>
        {
            ["label"] = new Dictionary<string, bool>
            {
                [$"{labelKey}={labelValue}"] = true
            }
        };

        var items = await client.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = filters
            },
            ct);

        return items
            .Select(ToSummary)
            .ToList();
    }

    private static (string Repository, string Tag) SplitImage(string image)
    {
        var trimmed = image.Trim();

        var lastColon = trimmed.LastIndexOf(':');
        var lastSlash = trimmed.LastIndexOf('/');

        if (lastColon > -1 && lastColon > lastSlash)
        {
            return (
                Repository: trimmed[..lastColon],
                Tag: trimmed[(lastColon + 1)..]
            );
        }

        return (trimmed, "latest");
    }

    public async Task<bool> ImageExistsAsync(string image, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(image))
            return false;

        var images = await client.Images.ListImagesAsync(
            new ImagesListParameters
            {
                All = true
            },
            ct);

        return images.Any(x => DockerImageIdentityMatcher.IsMatch(
            x.ID,
            x.RepoTags,
            x.RepoDigests,
            image));
    }

    public async Task EnsureNetworkAsync(string networkName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(networkName))
            throw new ArgumentException("Network name is required.", nameof(networkName));

        var networks = await client.Networks.ListNetworksAsync(
            new NetworksListParameters(),
            ct);

        var exists = networks.Any(x =>
            string.Equals(x.Name, networkName, StringComparison.OrdinalIgnoreCase));

        if (exists)
            return;

        await client.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
            Name = networkName,
            Driver = "bridge"
        }, ct);
    }
}