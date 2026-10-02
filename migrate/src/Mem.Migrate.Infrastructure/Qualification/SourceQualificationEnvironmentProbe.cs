using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Qualification;

namespace Mem.Migrate.Infrastructure.Qualification;

public sealed class SourceQualificationEnvironmentProbe(IProcessRunner processRunner) :
    ISourceQualificationEnvironmentProbe
{
    public async Task<SourceQualificationEnvironmentObservation> ObserveAsync(
        CutoverPlanDocument plan,
        string dockerCommand,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var machineId = await ReadMachineIdAsync(cancellationToken);
        var dockerInfo = await RunDockerAsync(
            dockerCommand,
            ["info", "--format", "{{json .}}"],
            commandTimeoutSeconds,
            cancellationToken);
        using var infoDocument = JsonDocument.Parse(dockerInfo);
        var info = infoDocument.RootElement;
        var dockerId = RequiredString(info, "ID", "Docker Engine ID");
        var dockerName = RequiredString(info, "Name", "Docker Engine name");
        var dockerVersion = RequiredString(info, "ServerVersion", "Docker Engine version");

        var expected = plan.SourceContainersToFreeze
            .Select(item => new ExpectedContainer(
                item.Role,
                item.ContainerId,
                item.ContainerName,
                item.ImageId,
                WriterContainer: true))
            .Concat(plan.RetainedSourceContainers.Select(item => new ExpectedContainer(
                item.Role,
                item.ContainerId,
                item.ContainerName,
                item.ImageId,
                WriterContainer: false)))
            .ToArray();
        if (expected.Length == 0)
        {
            throw new InvalidDataException(
                "The cutover plan contains no source containers for qualification.");
        }

        var inspectOutput = await RunDockerAsync(
            dockerCommand,
            ["inspect", .. expected.Select(item => item.ContainerId)],
            commandTimeoutSeconds,
            cancellationToken);
        using var inspectDocument = JsonDocument.Parse(inspectOutput);
        if (inspectDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Docker inspect did not return a JSON array.");
        }

        var observedById = inspectDocument.RootElement
            .EnumerateArray()
            .Select(ReadContainer)
            .ToDictionary(item => item.ContainerId, StringComparer.Ordinal);
        var observed = new List<SourceQualificationObservedContainer>(expected.Length);
        foreach (var item in expected)
        {
            if (!observedById.TryGetValue(item.ContainerId, out var current))
            {
                throw new InvalidDataException(
                    $"Source container '{item.ContainerName}' no longer exists with the captured ID.");
            }

            observed.Add(current with
            {
                Role = item.Role,
                WriterContainer = item.WriterContainer
            });
        }

        return new SourceQualificationEnvironmentObservation(
            new QualificationHostIdentity(
                Sha256(machineId),
                Environment.MachineName,
                RuntimeInformation.OSDescription,
                RuntimeInformation.OSArchitecture.ToString(),
                Sha256(dockerId),
                dockerName,
                dockerVersion),
            observed);
    }

    private async Task<string> RunDockerAsync(
        string dockerCommand,
        IReadOnlyList<string> arguments,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                dockerCommand,
                arguments,
                TimeSpan.FromSeconds(commandTimeoutSeconds),
                MaximumOutputCharacters: 4 * 1024 * 1024),
            cancellationToken);
        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.StandardError.Trim()
                : result.ErrorMessage;
            throw new InvalidOperationException(
                $"Docker qualification inspection failed: {detail}");
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            throw new InvalidDataException(
                "Docker qualification inspection returned no output.");
        }

        return result.StandardOutput;
    }

    private static SourceQualificationObservedContainer ReadContainer(
        JsonElement element)
    {
        var id = RequiredString(element, "Id", "container ID");
        var name = RequiredString(element, "Name", "container name").TrimStart('/');
        var imageId = RequiredString(element, "Image", "container image ID");
        if (!element.TryGetProperty("State", out var state))
        {
            throw new InvalidDataException(
                $"Docker inspect evidence for '{name}' has no State object.");
        }

        var status = RequiredString(state, "Status", "container state");
        var running = state.TryGetProperty("Running", out var runningValue) &&
                      runningValue.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? runningValue.GetBoolean()
            : throw new InvalidDataException(
                $"Docker inspect evidence for '{name}' has no running state.");
        if (!element.TryGetProperty("HostConfig", out var hostConfig) ||
            !hostConfig.TryGetProperty("RestartPolicy", out var restartPolicy))
        {
            throw new InvalidDataException(
                $"Docker inspect evidence for '{name}' has no restart policy.");
        }

        var policy = RequiredString(
            restartPolicy,
            "Name",
            "container restart policy",
            allowEmpty: true);
        return new SourceQualificationObservedContainer(
            Role: string.Empty,
            ContainerId: id,
            ContainerName: name,
            ImageId: imageId,
            State: status,
            Running: running,
            RestartPolicy: string.IsNullOrWhiteSpace(policy) ? "no" : policy,
            WriterContainer: false);
    }

    private static async Task<string> ReadMachineIdAsync(
        CancellationToken cancellationToken)
    {
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            if (!File.Exists(path))
            {
                continue;
            }

            var value = (await File.ReadAllTextAsync(path, cancellationToken)).Trim();
            if (value.Length >= 16 && value.All(Uri.IsHexDigit))
            {
                return value.ToLowerInvariant();
            }
        }

        throw new InvalidOperationException(
            "A stable Linux machine ID is required for two-server qualification.");
    }

    private static string RequiredString(
        JsonElement element,
        string propertyName,
        string description,
        bool allowEmpty = false)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException($"Docker evidence has no {description}.");
        }

        var text = value.GetString() ?? string.Empty;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException($"Docker evidence has an empty {description}.");
        }

        return text;
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record ExpectedContainer(
        string Role,
        string ContainerId,
        string ContainerName,
        string ImageId,
        bool WriterContainer);
}
