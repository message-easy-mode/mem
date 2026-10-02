using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Docker.DotNet;

namespace HostAgent.Runtime.Migrations.Qualification;

public sealed class MigrationTargetHostIdentityProbe(DockerClient docker) :
    IMigrationTargetHostIdentityProbe
{
    public async Task<MigrationTwoServerHostIdentity> ObserveAsync(
        CancellationToken cancellationToken)
    {
        var machineId = await ReadMachineIdAsync(cancellationToken);
        var info = await docker.System.GetSystemInfoAsync(cancellationToken);
        var json = JsonSerializer.SerializeToElement(info);
        var dockerId = RequiredString(json, "Docker Engine ID", "ID", "Id");
        var dockerName = RequiredString(json, "Docker Engine name", "Name");
        var dockerVersion = RequiredString(
            json,
            "Docker Engine version",
            "ServerVersion");

        return new MigrationTwoServerHostIdentity(
            Sha256(machineId),
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            Sha256(dockerId),
            dockerName,
            dockerVersion);
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
        string description,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!;
            }
        }

        throw new InvalidDataException($"Docker evidence has no {description}.");
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
