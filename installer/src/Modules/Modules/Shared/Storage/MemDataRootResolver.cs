using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace Modules.Shared.Storage;

/// <summary>
/// Canonical resolution policy for MEM durable data that is intentionally
/// visible outside the Control Plane private /data volume. Production must
/// receive this root explicitly from the deployment boundary; user-profile
/// fallback is retained only for non-production compatibility/test contexts.
/// </summary>
public static class MemDataRootResolver
{
    public const string CanonicalProductionDataRoot =
        "/var/lib/message-easy-mode/mem-data";

    public static string Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configured = FirstNonBlank(
            configuration["MEM_DATA_ROOT"],
            configuration["HostAgent:DataRoot"],
            configuration["Mem:DataRoot"]);

        var runtimeMode = FirstNonBlank(
            configuration["MEM_RUNTIME_MODE"],
            configuration["MemRuntime:Mode"]);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return NormalizeConfiguredRoot(configured, runtimeMode);
        }

        if (string.Equals(
                runtimeMode,
                MemRuntimeModes.ContainerizedProduction,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "MEM_DATA_ROOT must be explicitly configured for containerized-production. " +
                "The official server bootstrap supplies a server-owned root under /var/lib/message-easy-mode.");
        }

        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            throw new InvalidOperationException(
                "MEM data root is not configured and no user-profile compatibility root is available.");
        }

        return Path.GetFullPath(Path.Combine(home, "mem-data"));
    }

    private static string NormalizeConfiguredRoot(
        string configured,
        string? runtimeMode)
    {
        var trimmed = configured.Trim();
        var production = string.Equals(
            runtimeMode,
            MemRuntimeModes.ContainerizedProduction,
            StringComparison.Ordinal);

        if (production && !Path.IsPathRooted(trimmed))
        {
            throw new InvalidOperationException(
                "MEM_DATA_ROOT must be an absolute server-owned path in containerized-production.");
        }

        var resolved = Path.GetFullPath(trimmed);
        if (production && IsLoginHomePath(resolved))
        {
            throw new InvalidOperationException(
                "MEM_DATA_ROOT cannot use a login-user home directory in containerized-production.");
        }

        return resolved;
    }

    private static bool IsLoginHomePath(string path)
    {
        if (Path.DirectorySeparatorChar != '/')
        {
            return false;
        }

        return string.Equals(path, "/home", StringComparison.Ordinal) ||
               path.StartsWith("/home/", StringComparison.Ordinal) ||
               string.Equals(path, "/root", StringComparison.Ordinal) ||
               path.StartsWith("/root/", StringComparison.Ordinal);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
