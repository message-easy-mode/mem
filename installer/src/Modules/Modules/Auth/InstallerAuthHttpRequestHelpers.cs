using Microsoft.AspNetCore.Http;

namespace Modules.Auth;

public static class InstallerAuthHttpRequestHelpers
{
    public static bool IsApiRequest(HttpRequest request)
    {
        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var accept = request.Headers.Accept.ToString();

        return !string.IsNullOrWhiteSpace(accept) &&
               !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }
}