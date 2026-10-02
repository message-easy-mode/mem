using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Security;
using Microsoft.AspNetCore.Mvc;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preflight.Endpoints;

/// <summary>
/// Read-only Production Restore planning from a canonical Backup Catalog entry.
/// This route is deliberately parallel to the legacy validated-import route
/// while candidate/recreate execution remains unavailable for catalog sources.
/// </summary>
public sealed class HostAgentCatalogProductionRestorePlanEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Production Restore");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/production-restore/plan",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    [FromBody] CatalogProductionRestorePlanRequest? request,
                    CatalogProductionRestorePlanService planService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    if (request is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_catalog_production_restore_plan_body_required",
                            Detail: "Provide a JSON request body. stagingId, candidateId, targetStackSlug, restoreMode, intendedMatrixHost, and intendedElementHost are optional operator inputs."));
                    }

                    try
                    {
                        var response = await planService.CreatePlanAsync(
                            catalogEntryId,
                            request,
                            ct);

                        return Results.Ok(response);
                    }
                    catch (CatalogRestoreSourceNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_catalog_entry_not_found",
                            Detail: ex.Message));
                    }
                    catch (CatalogRestoreSourceUnavailableException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "backup_catalog_entry_not_available",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (BackupCatalogPayloadResolutionException ex)
                        when (string.Equals(
                            ex.ErrorCode,
                            "backup_catalog_entry_not_found",
                            StringComparison.Ordinal))
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: ex.ErrorCode,
                            Detail: ex.Message));
                    }
                    catch (BackupCatalogPayloadResolutionException ex)
                        when (ex.ErrorCode is "backup_catalog_payload_not_available"
                            or "backup_catalog_payload_invalid"
                            or "backup_catalog_payload_not_found")
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: ex.ErrorCode,
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (BackupCatalogPayloadResolutionException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: ex.ErrorCode,
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_production_restore_plan_request",
                            Detail: ex.Message));
                    }
                })
            .Accepts<CatalogProductionRestorePlanRequest>("application/json")
            .Produces<CatalogProductionRestorePlanResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict);
    }
}
