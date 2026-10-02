using Carter;
using Modules.Auth.Identity;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;

namespace Modules.Operator.Runtime;

public sealed class ControlPlaneRuntimeEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
            "/health/runtime",
            (HttpContext context, MemControlPlaneRuntimeContext runtimeContext) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";

                return Results.Ok(new MemControlPlaneRuntimePreflightProjection(
                    runtimeContext.SchemaVersion,
                    global::Shared.ControlPlane.MemControlPlaneIdentity.ProductDisplayName,
                    runtimeContext.RuntimeMode,
                    runtimeContext.ControlPlaneInstanceId,
                    runtimeContext.ApiProcessInstanceId,
                    runtimeContext.UiDeliveryMode,
                    runtimeContext.Version,
                    runtimeContext.Commit,
                    runtimeContext.ValidationState,
                    runtimeContext.ShowDevelopmentBanner));
            })
        .WithName("GetControlPlaneRuntimePreflight")
        .WithTags("Health")
        .AllowAnonymous()
        .Produces<MemControlPlaneRuntimePreflightProjection>(StatusCodes.Status200OK);

        app.MapGet(
            "/api/operator/runtime-context",
            async (
                HttpContext context,
                MemControlPlaneRuntimeContext runtimeContext,
                IControlPlaneDockerOwnershipGuard ownershipGuard,
                IControlPlaneExposureInspector exposureInspector,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.Pragma = "no-cache";

                var ownership = await ownershipGuard.InspectAsync(cancellationToken);
                var exposure = await exposureInspector.InspectAsync(cancellationToken);
                var projection = runtimeContext.ToSafeProjection() with
                {
                    MutationsAllowed = runtimeContext.MutationsAllowed &&
                                       ownership.MutationsAllowed,
                    DockerOwnership = ownership,
                    ControlPlaneExposure = exposure
                };
                return Results.Ok(projection);
            })
        .WithName("GetControlPlaneRuntimeContext")
        .WithTags("Operator Runtime")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<MemControlPlaneRuntimeContextProjection>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }
}
