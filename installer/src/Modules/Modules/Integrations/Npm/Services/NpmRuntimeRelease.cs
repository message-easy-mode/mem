namespace Modules.Integrations.Npm.Services;

/// <summary>
/// MEM-owned Nginx Proxy Manager runtime release contract for the v0.2.0
/// product line. All fresh NPM creation paths must use this exact release
/// reference rather than floating tags such as <c>latest</c> or <c>2</c>.
/// </summary>
public static class NpmRuntimeRelease
{
    public const string ApprovedVersion = "2.15.1";
    public const string ApprovedImage = "jc21/nginx-proxy-manager:2.15.1";

    public static bool IsApprovedImage(string? imageReference)
    {
        var observed = Normalize(imageReference);
        if (observed.Length == 0)
        {
            return false;
        }

        return string.Equals(
            observed,
            Normalize(ApprovedImage),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string? imageReference)
    {
        var value = imageReference?.Trim() ?? string.Empty;
        const string dockerHubPrefix = "docker.io/";

        if (value.StartsWith(dockerHubPrefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[dockerHubPrefix.Length..];
        }

        return value;
    }
}
