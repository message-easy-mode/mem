using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Security;
using Microsoft.AspNetCore.Mvc;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preview.Endpoints;

/// <summary>
/// Creates a read-only public cutover preview for a private candidate created
/// from canonical Backup Catalog material. No public mutation is available.
/// </summary>
public sealed class HostAgentCatalogPublicCutoverPreviewEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Public Cutover Preview");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-preview",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    [FromBody] CatalogPublicCutoverPreviewRequest? request,
                    RuntimeStackBackupPublicCutoverPreviewService previewService,
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
                            Error: "backup_catalog_cutover_preview_body_required",
                            Detail: "Provide a JSON request body with candidateId. targetStackSlug, restoreMode, intendedMatrixHost, and intendedElementHost are optional."));
                    }

                    try
                    {
                        var response = await previewService.CreatePreviewFromCatalogAsync(
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
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_catalog_private_candidate_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidDataException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_cutover_preview_source",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_cutover_preview_request",
                            Detail: ex.Message));
                    }
                })
            .Accepts<CatalogPublicCutoverPreviewRequest>("application/json")
            .Produces<CatalogPublicCutoverPreviewResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict);
    }
}
