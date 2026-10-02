using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.Readiness;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Readiness.Endpoints;

public sealed class HostAgentCatalogPreCutoverReadinessEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/pre-cutover-readiness", async (
            HttpContext httpContext, string catalogEntryId, string candidateId, string previewId, string confirmationId,
            CatalogPreCutoverReadinessService readinessService, CancellationToken ct) =>
        {
            var auth = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
            if (auth is not null) return auth;
            try { return Results.Ok(await readinessService.GetAsync(catalogEntryId, candidateId, previewId, confirmationId, ct)); }
            catch (CatalogRestoreSourceNotFoundException ex) { return Results.NotFound(new HostAgentErrorResponse("backup_catalog_entry_not_found", ex.Message)); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new HostAgentErrorResponse("invalid_backup_catalog_pre_cutover_readiness_request", ex.Message)); }
        }).WithTags("Host Agent / Backups / Catalog / Pre-cutover readiness").Produces<CatalogPreCutoverReadinessResponse>();
    }
}
