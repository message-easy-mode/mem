using Carter;
using Modules.Auth.Identity;

namespace Modules.Operator.Dashboard;

/// <summary>
/// Browser-facing, authenticated Home dashboard route. The endpoint exposes
/// only the bounded projection built by <see cref="DashboardOverviewService"/>;
/// it does not expose operational inputs, evidence, filesystem paths, secrets,
/// raw Docker data, or persistence identifiers beyond the dashboard's stable
/// action references.
/// </summary>
public sealed class DashboardOverviewEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/dashboard")
            .WithTags("Operator Dashboard")
            .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapGet("/overview", async (
            HttpContext httpContext,
            DashboardOverviewService dashboard,
            CancellationToken ct) =>
        {
            SetNoStoreHeaders(httpContext);

            var overview = await dashboard.GetOverviewAsync(httpContext.User, ct);
            return Results.Ok(overview);
        })
        .WithName("GetOperatorDashboardOverview")
        .Produces<DashboardOverviewResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }

    private static void SetNoStoreHeaders(HttpContext httpContext)
    {
        // The response is a current operational snapshot for the local control
        // plane. Do not allow browsers or intermediaries to retain a stale
        // operator view after sign-out, a role change, or a local topology
        // change.
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
