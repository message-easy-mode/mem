using Carter;
using Modules.Auth.Identity;
using Modules.Operator.Migrations;

namespace Modules.Operator.Migrations.Workspace;

public sealed class MigrationWorkspaceEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/api/operator/migrations/sessions/{migrationId}/workspace",
                async (
                    string migrationId,
                    HttpContext httpContext,
                    MigrationWorkspaceProjectionService service,
                    CancellationToken cancellationToken) =>
                {
                    SetNoStore(httpContext);
                    var workspace = await service.GetAsync(migrationId, cancellationToken);
                    return workspace is null
                        ? Results.NotFound(new MigrationSessionProblemResponse(
                            "migration_session_not_found",
                            "Migration session was not found."))
                        : Results.Ok(workspace);
                })
            .WithTags("Migration workspace")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate)
            .Produces<MigrationWorkspaceResponse>()
            .Produces<MigrationSessionProblemResponse>(StatusCodes.Status404NotFound);
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
