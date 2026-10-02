using System.Globalization;
using System.Text.Json;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Docker;

public sealed class DockerInventoryProbe(IProcessRunner processRunner)
    : IDockerInventoryProbe
{
    private static readonly HashSet<string> SafeEnvironmentKeys =
        new(StringComparer.Ordinal)
        {
            "POSTGRES_DB",
            "POSTGRES_USER",
            "DB_SQLITE_FILE",
            "ASPNETCORE_URLS",
            "NODE_ENV",
            "PORT"
        };

    private static readonly HashSet<string> SafeLabelKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "mem.instanceId",
            "mem.serviceKey",
            "mem.component",
            "mem.managed-by",
            "mem.service",
            "matrixeasymode.managed",
            "matrixeasymode.service",
            "matrixeasymode.resource-kind",
            "io.matrixeasymode.managed",
            "io.matrixeasymode.scope",
            "io.matrixeasymode.service-id",
            "io.matrixeasymode.stack-id",
            "io.matrixeasymode.service-key",
            "io.matrixeasymode.instance-id",
            "com.docker.compose.project",
            "com.docker.compose.service",
            "com.docker.compose.version",
            "org.opencontainers.image.version"
        };

    public async Task<DockerInventoryObservation> ProbeAsync(
        AssessmentOptions options,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(options.CommandTimeoutSeconds);
        var version = await processRunner.RunAsync(
            new ProcessRequest(
                options.DockerCommand,
                ["version", "--format", "{{.Server.Version}}"],
                timeout),
            cancellationToken);

        if (!version.Succeeded)
        {
            return new DockerInventoryObservation(
                Available: false,
                ServerVersion: null,
                ErrorCode: version.ErrorCode ?? "docker_unavailable",
                ErrorMessage: SafeProcessMessage(version),
                Containers: [],
                Networks: [],
                ObservedAtUtc: DateTimeOffset.UtcNow);
        }

        var containerIds = await processRunner.RunAsync(
            new ProcessRequest(
                options.DockerCommand,
                ["ps", "-aq", "--no-trunc"],
                timeout),
            cancellationToken);

        if (!containerIds.Succeeded)
        {
            return new DockerInventoryObservation(
                Available: false,
                ServerVersion: version.StandardOutput.Trim(),
                ErrorCode: containerIds.ErrorCode ?? "docker_list_failed",
                ErrorMessage: SafeProcessMessage(containerIds),
                Containers: [],
                Networks: [],
                ObservedAtUtc: DateTimeOffset.UtcNow);
        }

        var ids = SplitLines(containerIds.StandardOutput);
        DockerContainerObservation[] containers = [];

        if (ids.Length > 0)
        {
            var inspectArguments = new List<string> { "inspect" };
            inspectArguments.AddRange(ids);

            var inspect = await processRunner.RunAsync(
                new ProcessRequest(
                    options.DockerCommand,
                    inspectArguments,
                    timeout,
                    MaximumOutputCharacters: 64 * 1024 * 1024),
                cancellationToken);

            if (!inspect.Succeeded)
            {
                return new DockerInventoryObservation(
                    Available: false,
                    ServerVersion: version.StandardOutput.Trim(),
                    ErrorCode: inspect.ErrorCode ?? "docker_inspect_failed",
                    ErrorMessage: SafeProcessMessage(inspect),
                    Containers: [],
                    Networks: [],
                    ObservedAtUtc: DateTimeOffset.UtcNow);
            }

            try
            {
                containers = ParseContainers(inspect.StandardOutput);
            }
            catch (JsonException)
            {
                return new DockerInventoryObservation(
                    Available: false,
                    ServerVersion: version.StandardOutput.Trim(),
                    ErrorCode: "docker_inspect_invalid_json",
                    ErrorMessage: "Docker returned an invalid container inspection document.",
                    Containers: [],
                    Networks: [],
                    ObservedAtUtc: DateTimeOffset.UtcNow);
            }
        }

        var networks = await ProbeNetworksAsync(
            options,
            timeout,
            cancellationToken);

        return new DockerInventoryObservation(
            Available: true,
            ServerVersion: version.StandardOutput.Trim(),
            ErrorCode: null,
            ErrorMessage: null,
            Containers: containers,
            Networks: networks,
            ObservedAtUtc: DateTimeOffset.UtcNow);
    }

    public static DockerContainerObservation[] ParseContainers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var observations = new List<DockerContainerObservation>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Docker inspect root must be an array.");
        }

        foreach (var container in document.RootElement.EnumerateArray())
        {
            var id = GetString(container, "Id") ?? string.Empty;
            var name = (GetString(container, "Name") ?? string.Empty)
                .TrimStart('/');
            var imageId = GetString(container, "Image") ?? string.Empty;

            var config = GetObject(container, "Config");
            var state = GetObject(container, "State");
            var hostConfig = GetObject(container, "HostConfig");
            var networkSettings = GetObject(container, "NetworkSettings");

            var image = config is null
                ? string.Empty
                : GetString(config.Value, "Image") ?? string.Empty;

            var labels = config is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ReadFilteredDictionary(
                    GetObject(config.Value, "Labels"),
                    IsManagedLabel);

            var environment = config is null
                ? []
                : ReadStringArray(GetArray(config.Value, "Env"));

            var environmentNames = environment
                .Select(GetEnvironmentName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            var safeEnvironment = environment
                .Select(ParseEnvironment)
                .Where(x => x is not null && SafeEnvironmentKeys.Contains(x.Value.Key))
                .Select(x => x!.Value)
                .GroupBy(x => x.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.Ordinal);

            var health = state is null
                ? null
                : GetString(GetObject(state.Value, "Health"), "Status");

            var restartPolicy = hostConfig is null
                ? string.Empty
                : GetString(
                    GetObject(hostConfig.Value, "RestartPolicy"),
                    "Name") ?? string.Empty;

            var composeProject = labels.GetValueOrDefault(
                "com.docker.compose.project");
            var composeService = labels.GetValueOrDefault(
                "com.docker.compose.service");

            observations.Add(
                new DockerContainerObservation(
                    Id: id,
                    Name: name,
                    Image: image,
                    ImageId: imageId,
                    State: state is null
                        ? "unknown"
                        : GetString(state.Value, "Status") ?? "unknown",
                    Health: health,
                    RestartPolicy: restartPolicy,
                    CreatedAtUtc: ParseDateTimeOffset(
                        GetString(container, "Created")),
                    ComposeProject: composeProject,
                    ComposeService: composeService,
                    ManagedLabels: labels,
                    EnvironmentNames: environmentNames,
                    SafeEnvironment: safeEnvironment,
                    Ports: ReadPorts(networkSettings),
                    Mounts: ReadMounts(GetArray(container, "Mounts")),
                    Networks: ReadNetworks(networkSettings)));
        }

        return observations
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static DockerNetworkObservation[] ParseNetworks(string json)
    {
        using var document = JsonDocument.Parse(json);
        var observations = new List<DockerNetworkObservation>();

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Docker network inspect root must be an array.");
        }

        foreach (var network in document.RootElement.EnumerateArray())
        {
            var labels = ReadFilteredDictionary(
                GetObject(network, "Labels"),
                IsManagedLabel);

            observations.Add(
                new DockerNetworkObservation(
                    Id: GetString(network, "Id") ?? string.Empty,
                    Name: GetString(network, "Name") ?? string.Empty,
                    Driver: GetString(network, "Driver") ?? string.Empty,
                    Scope: GetString(network, "Scope") ?? string.Empty,
                    Internal: GetBoolean(network, "Internal"),
                    ManagedLabels: labels));
        }

        return observations
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<DockerNetworkObservation[]> ProbeNetworksAsync(
        AssessmentOptions options,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var networkIds = await processRunner.RunAsync(
            new ProcessRequest(
                options.DockerCommand,
                ["network", "ls", "-q", "--no-trunc"],
                timeout),
            cancellationToken);

        if (!networkIds.Succeeded)
        {
            return [];
        }

        var ids = SplitLines(networkIds.StandardOutput);

        if (ids.Length == 0)
        {
            return [];
        }

        var arguments = new List<string> { "network", "inspect" };
        arguments.AddRange(ids);

        var inspect = await processRunner.RunAsync(
            new ProcessRequest(
                options.DockerCommand,
                arguments,
                timeout,
                MaximumOutputCharacters: 32 * 1024 * 1024),
            cancellationToken);

        if (!inspect.Succeeded)
        {
            return [];
        }

        try
        {
            return ParseNetworks(inspect.StandardOutput);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static DockerPortBinding[] ReadPorts(JsonElement? networkSettings)
    {
        if (networkSettings is null)
        {
            return [];
        }

        var ports = GetObject(networkSettings.Value, "Ports");

        if (ports is null)
        {
            return [];
        }

        var results = new List<DockerPortBinding>();

        foreach (var property in ports.Value.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null)
            {
                results.Add(new DockerPortBinding(property.Name, null, null));
                continue;
            }

            if (property.Value.ValueKind is not JsonValueKind.Array)
            {
                continue;
            }

            foreach (var binding in property.Value.EnumerateArray())
            {
                results.Add(
                    new DockerPortBinding(
                        ContainerPort: property.Name,
                        HostIp: GetString(binding, "HostIp"),
                        HostPort: GetString(binding, "HostPort")));
            }
        }

        return results
            .OrderBy(x => x.ContainerPort, StringComparer.Ordinal)
            .ThenBy(x => x.HostPort, StringComparer.Ordinal)
            .ToArray();
    }

    private static DockerMountObservation[] ReadMounts(JsonElement? mounts)
    {
        if (mounts is null || mounts.Value.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return mounts.Value.EnumerateArray()
            .Select(mount => new DockerMountObservation(
                Type: GetString(mount, "Type") ?? string.Empty,
                Name: GetString(mount, "Name"),
                Source: GetString(mount, "Source") ?? string.Empty,
                Destination: GetString(mount, "Destination") ?? string.Empty,
                ReadWrite: GetBoolean(mount, "RW")))
            .OrderBy(x => x.Destination, StringComparer.Ordinal)
            .ToArray();
    }

    private static DockerNetworkAttachment[] ReadNetworks(
        JsonElement? networkSettings)
    {
        if (networkSettings is null)
        {
            return [];
        }

        var networks = GetObject(networkSettings.Value, "Networks");

        if (networks is null)
        {
            return [];
        }

        return networks.Value.EnumerateObject()
            .Select(network => new DockerNetworkAttachment(
                Name: network.Name,
                IpAddress: GetString(network.Value, "IPAddress"),
                Aliases: ReadStringArray(
                    GetArray(network.Value, "Aliases"))))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, string> ReadFilteredDictionary(
        JsonElement? element,
        Func<string, bool> include)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (element is null || element.Value.ValueKind is not JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in element.Value.EnumerateObject())
        {
            if (!include(property.Name) ||
                property.Value.ValueKind is not JsonValueKind.String)
            {
                continue;
            }

            result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
    }

    private static string[] ReadStringArray(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return element.Value.EnumerateArray()
            .Where(x => x.ValueKind is JsonValueKind.String)
            .Select(x => x.GetString() ?? string.Empty)
            .ToArray();
    }

    private static string GetEnvironmentName(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        return separator < 0 ? value : value[..separator];
    }

    private static KeyValuePair<string, string>? ParseEnvironment(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);

        if (separator <= 0)
        {
            return null;
        }

        return new KeyValuePair<string, string>(
            value[..separator],
            value[(separator + 1)..]);
    }

    private static bool IsManagedLabel(string key) =>
        !AssessmentRedactor.IsSensitiveKey(key)
        && SafeLabelKeys.Contains(key);

    private static string[] SplitLines(string value) =>
        value.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string SafeProcessMessage(ProcessResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            return result.ErrorMessage;
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            return result.StandardError.Trim();
        }

        return "Docker command failed.";
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;

    private static JsonElement? GetObject(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        return value;
    }

    private static JsonElement? GetArray(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not JsonValueKind.Array)
        {
            return null;
        }

        return value;
    }

    private static string? GetString(
        JsonElement? element,
        string propertyName)
    {
        if (element is null)
        {
            return null;
        }

        return GetString(element.Value, propertyName);
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool GetBoolean(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True;
    }
}
