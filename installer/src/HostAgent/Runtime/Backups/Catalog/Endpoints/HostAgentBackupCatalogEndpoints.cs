using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.Catalog.Endpoints;

public sealed class HostAgentBackupCatalogEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog");

        group.MapGet(
                "/backups/catalog",
                async (
                    HttpContext httpContext,
                    BackupCatalogStore catalogStore,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    var result = await catalogStore.ListAsync(ct);
                    return Results.Ok(result);
                })
            .Produces<BackupCatalogListResponse>();

        group.MapGet(
                "/backups/catalog/{catalogEntryId}",
                async (
                    string catalogEntryId,
                    HttpContext httpContext,
                    BackupCatalogStore catalogStore,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await catalogStore.FindByCatalogEntryIdAsync(
                            catalogEntryId,
                            ct);

                        return result is null
                            ? Results.Json(
                                new HostAgentErrorResponse(
                                    Error: "backup_catalog_entry_not_found",
                                    Detail: "No Backup Catalog entry exists with the supplied catalog entry id."),
                                statusCode: StatusCodes.Status404NotFound)
                            : Results.Ok(result);
                    }
                    catch (InvalidOperationException)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "backup_catalog_entry_id_required",
                                Detail: "A Backup Catalog entry id is required."),
                            statusCode: StatusCodes.Status400BadRequest);
                    }
                })
            .Produces<BackupCatalogDetailResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost(
                "/backups/catalog/backfill/local",
                async (
                    HttpContext httpContext,
                    LocalCapturedBackupCatalogRegistrationService registrationService,
                    ILogger<HostAgentBackupCatalogEndpoints> logger,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await registrationService.BackfillAsync(ct);
                        return Results.Ok(result);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return Results.StatusCode(499);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Local Backup Catalog backfill failed.");

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "backup_catalog_backfill_failed",
                                Detail: "Local backup catalog registration did not complete. Inspect server logs and retry."),
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                })
            .Produces<BackupCatalogLocalBackfillResponse>();

        group.MapPost(
                "/backups/catalog/imports/{validationId}/materialise",
                async (
                    string validationId,
                    HttpContext httpContext,
                    ImportedZipBackupCatalogMaterialisationService materialisationService,
                    ILogger<HostAgentBackupCatalogEndpoints> logger,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await materialisationService.MaterialiseAsync(
                            validationId,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_import_materialisation_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "validated_import_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidDataException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_validated_import_archive",
                            Detail: ex.Message));
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Imported ZIP Backup Catalog materialisation failed for validation id {ValidationId}.",
                            validationId);

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "backup_catalog_import_materialisation_failed",
                                Detail: "Imported ZIP materialisation did not complete. Inspect server logs and retry."),
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                })
            .Produces<BackupCatalogImportedMaterialisationResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound);
    }
}
