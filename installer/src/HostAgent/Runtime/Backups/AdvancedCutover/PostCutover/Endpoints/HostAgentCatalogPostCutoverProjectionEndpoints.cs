using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.PostCutover;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.AdvancedCutover.PostCutover.Endpoints;

public sealed class HostAgentCatalogPostCutoverProjectionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Post Cutover");

        group.MapGet(
                "/backups/catalog/{catalogEntryId}/production-restore/post-cutover",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    CatalogPostCutoverProjectionService projectionService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var response = await projectionService.GetAsync(catalogEntryId, ct);
                        return response is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                "backup_catalog_entry_not_found",
                                $"Backup Catalog entry '{catalogEntryId}' was not found."))
                            : Results.Ok(response);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            "invalid_backup_catalog_post_cutover_projection_request",
                            ex.Message));
                    }
                })
            .Produces<CatalogPostCutoverProjectionResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound);
    }
}
