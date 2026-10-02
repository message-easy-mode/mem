using Microsoft.AspNetCore.Authorization;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Services.TemporaryStaging.Endpoints;

public sealed class TemporaryStagingInventoryEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/operator/services/temporary-staging", async (
            HttpContext context,
            TemporaryStagingInventoryService service,
            IAuthorizationService authorization,
            CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            var response = await service.GetAsync(ct);
            var canOpenMigration = (await authorization.AuthorizeAsync(
                context.User, null, MemOperatorPolicies.MigrationIntakeOperate)).Succeeded;
            var canOpenRestore = (await authorization.AuthorizeAsync(
                context.User, null, MemOperatorPolicies.Operate)).Succeeded;
            // Safe inventory readers need not have access to an operational workspace.
            return Results.Ok(response with
            {
                Items = response.Items.Select(item => item.Owner is not null &&
                    !(item.Owner.Kind == "migration" ? canOpenMigration : canOpenRestore)
                    ? item with { Owner = item.Owner with { WorkspaceHref = null }, RetirementReview = null }
                    : item).ToArray()
            });
        })
        .WithTags("Operator / Services / Temporary staging")
        .WithName("Services_TemporaryStagingInventory")
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus)
        .Produces<TemporaryStagingInventoryResponse>(StatusCodes.Status200OK);
    }
}
