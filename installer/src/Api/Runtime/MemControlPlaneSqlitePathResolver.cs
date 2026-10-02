using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Api.Runtime;

/// <summary>
/// Resolves the Control Plane SQLite path consistently for both the normal API
/// process and host-authoritative maintenance commands.
/// </summary>
public static class MemControlPlaneSqlitePathResolver
{
    public static string Resolve(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredPath =
            configuration["Aio:SqlitePath"] ??
            "data/mem-installer.dev.db";

        var contentRoot = environment.ContentRootPath;

        var possibleSrcDir = Directory.GetParent(contentRoot);
        var possibleInstallerRoot = possibleSrcDir?.Parent?.FullName;

        var installerRoot =
            possibleSrcDir?.Name.Equals(
                "src",
                StringComparison.OrdinalIgnoreCase) == true
                ? possibleInstallerRoot ?? contentRoot
                : contentRoot;

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(installerRoot, configuredPath));
    }
}
