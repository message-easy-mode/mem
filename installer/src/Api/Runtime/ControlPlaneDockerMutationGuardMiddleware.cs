using Modules.Shared.Docker;

namespace Api.Runtime;

/// <summary>
/// Fail-closed HTTP boundary for operations that can change Docker-backed MEM
/// state. This also covers the older HostAgent Docker abstraction, so a feature
/// cannot bypass the central ownership decision merely by using another adapter.
/// </summary>
public sealed class ControlPlaneDockerMutationGuardMiddleware(
    RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IControlPlaneDockerOwnershipGuard ownershipGuard)
    {
        if (RequiresExclusiveDockerOwnership(context.Request))
        {
            await ownershipGuard.EnsureMutationAllowedAsync(
                context.RequestAborted);
        }

        await next(context);
    }

    public static bool RequiresExclusiveDockerOwnership(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) ||
            HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        var path = request.Path.Value ?? string.Empty;
        return path.StartsWith("/api/setup", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/host-agent", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/stacks", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/restores", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/backups", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/migrations", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/diagnostics/seq", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/services", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/api/operator/turn", StringComparison.OrdinalIgnoreCase);
    }
}
