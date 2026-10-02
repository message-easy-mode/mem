using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Element.Runtime;

public sealed record ElementContainerStartResult(
    string ContainerId,
    string ContainerName,
    string Image,
    bool AlreadyExisted,
    bool Running,
    string NetworkName,
    string DataPath,
    string ConfigPath,
    string InternalBaseUrl);

public sealed class ElementContainerStarter
{
    private const string ElementInternalPort = "80/tcp";
    private const string ContainerConfigPath = "/app/config.json";

    private readonly DockerClient _docker;
    private readonly MemControlPlaneRuntimeContext? _runtimeContext;

    public ElementContainerStarter(
        DockerClient docker,
        MemControlPlaneRuntimeContext? runtimeContext = null)
    {
        _docker = docker;
        _runtimeContext = runtimeContext;
    }

    public async Task<ElementContainerStartResult> EnsureStartedAsync(
        string containerName,
        string image,
        string dataPath,
        string configPath,
        string networkName,
        string internalBaseUrl,
        CancellationToken cancellationToken,
        bool allowPullIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(containerName))
            throw new InvalidOperationException("Element container name is required.");

        if (string.IsNullOrWhiteSpace(image))
            throw new InvalidOperationException("Element image is required.");

        if (string.IsNullOrWhiteSpace(dataPath))
            throw new InvalidOperationException("Element data path is required.");

        if (string.IsNullOrWhiteSpace(configPath))
            throw new InvalidOperationException("Element config path is required.");

        if (string.IsNullOrWhiteSpace(networkName))
            throw new InvalidOperationException("Runtime Docker network name is required.");

        if (!Directory.Exists(dataPath))
            throw new InvalidOperationException($"Element data path does not exist: {dataPath}");

        if (!File.Exists(configPath))
            throw new InvalidOperationException($"Element config.json does not exist: {configPath}");

        await EnsureNetworkExistsAsync(networkName, cancellationToken);

        var existing = await FindContainerByNameAsync(containerName, cancellationToken);

        if (existing is not null)
        {
            await EnsureContainerConnectedToNetworkAsync(
                existing.ID,
                containerName,
                networkName,
                cancellationToken);

            if (!existing.State.Running)
            {
                await _docker.Containers.StartContainerAsync(
                    existing.ID,
                    new ContainerStartParameters(),
                    cancellationToken);
            }

            var refreshed = await _docker.Containers.InspectContainerAsync(
                existing.ID,
                cancellationToken);

            return new ElementContainerStartResult(
                ContainerId: refreshed.ID,
                ContainerName: containerName,
                Image: refreshed.Config.Image,
                AlreadyExisted: true,
                Running: refreshed.State.Running,
                NetworkName: networkName,
                DataPath: dataPath,
                ConfigPath: configPath,
                InternalBaseUrl: internalBaseUrl);
        }

        if (allowPullIfMissing)
        {
            await PullImageIfMissingAsync(image, cancellationToken);
        }
        else
        {
            await EnsureImagePresentAsync(image, cancellationToken);
        }

        var create = await _docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = containerName,
                Image = image,

                ExposedPorts = new Dictionary<string, EmptyStruct>
                {
                    [ElementInternalPort] = default
                },

                HostConfig = new HostConfig
                {
                    RestartPolicy = new RestartPolicy
                    {
                        Name = RestartPolicyKind.UnlessStopped
                    },

                    Binds =
                    [
                        $"{configPath}:{ContainerConfigPath}:ro"
                    ]

                    // No PortBindings.
                    // Element remains internal-only on the Docker network until NPM routes are created.
                },

                NetworkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [networkName] = new EndpointSettings
                        {
                            Aliases =
                            [
                                containerName,
                                "element",
                                "chat"
                            ]
                        }
                    }
                },

                Labels = CreateOwnershipLabels(
                    component: "element-web",
                    service: "element")
            },
            cancellationToken);

        await _docker.Containers.StartContainerAsync(
            create.ID,
            new ContainerStartParameters(),
            cancellationToken);

        var inspect = await _docker.Containers.InspectContainerAsync(
            create.ID,
            cancellationToken);

        return new ElementContainerStartResult(
            ContainerId: create.ID,
            ContainerName: containerName,
            Image: image,
            AlreadyExisted: false,
            Running: inspect.State.Running,
            NetworkName: networkName,
            DataPath: dataPath,
            ConfigPath: configPath,
            InternalBaseUrl: internalBaseUrl);
    }

    private Dictionary<string, string> CreateOwnershipLabels(
        string component,
        string service)
    {
        var labels = new Dictionary<string, string>
        {
            ["mem.component"] = component,
            ["mem.managed-by"] = "host-agent",
            ["mem.service"] = service
        };
        MemDockerOwnershipLabels.AddTo(
            labels,
            _runtimeContext,
            resource: "matrix-stack-service",
            service: service);
        return labels;
    }

    private async Task<ContainerInspectResponse?> FindContainerByNameAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        var containers = await _docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true
            },
            cancellationToken);

        var match = containers.FirstOrDefault(container =>
            container.Names.Any(name =>
                string.Equals(
                    name.TrimStart('/'),
                    containerName,
                    StringComparison.OrdinalIgnoreCase)));

        if (match is null)
            return null;

        return await _docker.Containers.InspectContainerAsync(
            match.ID,
            cancellationToken);
    }

    private async Task EnsureNetworkExistsAsync(
        string networkName,
        CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Networks.InspectNetworkAsync(
                networkName,
                cancellationToken);
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"Docker network '{networkName}' does not exist. Run the platform install step that creates/verifies the MEM gateway network first.");
        }
    }

    private async Task EnsureContainerConnectedToNetworkAsync(
        string containerId,
        string containerName,
        string networkName,
        CancellationToken cancellationToken)
    {
        var inspect = await _docker.Containers.InspectContainerAsync(
            containerId,
            cancellationToken);

        if (inspect.NetworkSettings.Networks.ContainsKey(networkName))
            return;

        try
        {
            await _docker.Networks.ConnectNetworkAsync(
                networkName,
                new NetworkConnectParameters
                {
                    Container = containerId,
                    EndpointConfig = new EndpointSettings
                    {
                        Aliases =
                        [
                            containerName,
                            "element",
                            "chat"
                        ]
                    }
                },
                cancellationToken);
        }
        catch (DockerApiException ex)
            when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                  ex.Message.Contains("is already connected", StringComparison.OrdinalIgnoreCase))
        {
            // idempotent success
        }
    }

    private async Task EnsureImagePresentAsync(
        string image,
        CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Images.InspectImageAsync(image, cancellationToken);
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                $"The approved Element image '{image}' is not available locally. Operational pulls are prohibited.",
                ex);
        }
    }

    private async Task PullImageIfMissingAsync(
        string image,
        CancellationToken cancellationToken)
    {
        try
        {
            await _docker.Images.InspectImageAsync(
                image,
                cancellationToken);

            return;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // pull below
        }

        var parts = SplitImage(image);

        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters
            {
                FromImage = parts.Repository,
                Tag = parts.Tag
            },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken);
    }

    private static (string Repository, string Tag) SplitImage(string image)
    {
        var trimmed = image.Trim();

        var slashIndex = trimmed.LastIndexOf('/');
        var colonIndex = trimmed.LastIndexOf(':');

        if (colonIndex > slashIndex)
        {
            return (
                Repository: trimmed[..colonIndex],
                Tag: trimmed[(colonIndex + 1)..]);
        }

        return (
            Repository: trimmed,
            Tag: "latest");
    }
}