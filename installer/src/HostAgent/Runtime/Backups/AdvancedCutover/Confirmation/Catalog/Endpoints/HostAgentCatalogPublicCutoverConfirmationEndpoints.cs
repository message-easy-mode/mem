using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Security;
using Microsoft.AspNetCore.Mvc;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog.Endpoints;

/// <summary>
/// Records catalog-native Public Cutover acknowledgements and evaluates
/// read-only confirmation gates. No route, DNS, certificate, runtime, or
/// federation mutation is available from these endpoints.
/// </summary>
public sealed class HostAgentCatalogPublicCutoverConfirmationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Public Cutover Confirmation");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-confirmations/evaluate",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    [FromBody] CatalogPublicCutoverConfirmationRequest? request,
                    CatalogPublicCutoverConfirmationService confirmationService,
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
                            Error: "backup_catalog_cutover_confirmation_body_required",
                            Detail: "Provide a JSON request body with previewId, candidateId, and explicit acknowledgement flags."));
                    }

                    try
                    {
                        var response = await confirmationService.EvaluateAsync(
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
                            Error: "backup_catalog_cutover_confirmation_source_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidDataException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_cutover_confirmation_source",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_cutover_confirmation_request",
                            Detail: ex.Message));
                    }
                })
            .Accepts<CatalogPublicCutoverConfirmationRequest>("application/json")
            .Produces<CatalogPublicCutoverConfirmationResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-confirmations",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    string? previewId,
                    string? candidateId,
                    string? status,
                    int? max,
                    CatalogPublicCutoverConfirmationHistoryService historyService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var response = await historyService.ListConfirmationsAsync(
                            catalogEntryId,
                            previewId,
                            candidateId,
                            status,
                            max,
                            ct);

                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_cutover_confirmation_history_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<CatalogPublicCutoverConfirmationHistoryResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-confirmations/{confirmationId}",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    string confirmationId,
                    CatalogPublicCutoverConfirmationHistoryService historyService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var response = await historyService.GetConfirmationAsync(
                            catalogEntryId,
                            confirmationId,
                            ct);

                        if (response is null)
                        {
                            return Results.NotFound(new HostAgentErrorResponse(
                                Error: "backup_catalog_cutover_confirmation_not_found",
                                Detail: $"Catalog-native Public Cutover confirmation '{confirmationId}' was not found for Backup Catalog entry '{catalogEntryId}'."));
                        }

                        return Results.Ok(response);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_cutover_confirmation_history_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<CatalogPublicCutoverConfirmationDetailResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound);
    }
}
