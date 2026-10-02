using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Security;
using Microsoft.AspNetCore.Mvc;

namespace HostAgent.Runtime.Backups.StandardRecreate.Endpoints;

/// <summary>
/// Read-only Standard Recreate preflight for a canonical Backup Catalog entry.
/// Uploaded ZIP validation is ingestion-only; this route reads managed catalog
/// payload material and does not expose a validation-id restore path.
/// </summary>
public sealed class HostAgentCatalogStandardRecreatePreflightEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Standard Recreate");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/standard-recreate/preflight",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    [FromBody] CatalogStandardRecreatePreflightInput? input,
                    CatalogStandardRecreatePreflightService preflightService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    if (input is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_catalog_standard_recreate_preflight_body_required",
                            Detail: "Provide a JSON request body containing targetStackSlug and elementHost. requestedDomainId and matrixHost are optional."));
                    }

                    try
                    {
                        var response = await preflightService.AssessAsync(
                            catalogEntryId,
                            input.ToPreflightRequest(),
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
                            Error: "invalid_backup_catalog_standard_recreate_preflight_request",
                            Detail: ex.Message));
                    }
                })
            .Accepts<CatalogStandardRecreatePreflightInput>("application/json")
            .Produces<CatalogStandardRecreatePreflightResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict);
    }
}
