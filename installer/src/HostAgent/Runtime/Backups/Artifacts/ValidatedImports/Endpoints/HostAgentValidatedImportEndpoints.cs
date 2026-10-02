using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HostAgent.Runtime.Backups.Artifacts.ValidatedImports.Endpoints;

public sealed class HostAgentValidatedImportEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Artifacts / Validated Imports");

        group.MapPost(
                "/backups/artifacts/validated-imports",
                async (
                    HttpContext httpContext,
                    IFormFile? file,
                    ValidatedImportCatalogIngestionService ingestionService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    if (file is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_export_upload_missing",
                            Detail: "Upload a MEM stack export ZIP using multipart/form-data field name 'file'."));
                    }

                    try
                    {
                        var result = await ingestionService.ValidateAndIngestAsync(
                            file,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidDataException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_export_upload",
                            Detail: ex.Message));
                    }
                    catch (IOException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_export_upload_io_error",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_export_upload_request",
                            Detail: ex.Message));
                    }
                })
            .DisableAntiforgery()
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ImportValidationResponse>();

        group.MapGet(
                "/backups/artifacts/validated-imports/{validationId}",
                async (
                    HttpContext httpContext,
                    string validationId,
                    ValidatedImportArtifactService artifactService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await artifactService.InspectAsync(validationId, ct);
                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_validated_import_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "validated_import_not_found",
                            Detail: ex.Message));
                    }
                    catch (IOException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "validated_import_inspect_failed",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                })
            .Produces<ValidatedImportArtifactDetailResponse>();

        group.MapDelete(
                "/backups/artifacts/validated-imports/{validationId}",
                async (
                    HttpContext httpContext,
                    string validationId,
                    bool? acknowledgeDelete,
                    ValidatedImportArtifactService artifactService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await artifactService.DeleteAsync(
                            validationId,
                            acknowledgeDelete ?? false,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_validated_import_delete_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "validated_import_not_found",
                            Detail: ex.Message));
                    }
                    catch (IOException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "validated_import_delete_conflict",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "validated_import_delete_failed",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                })
            .Produces<ValidatedImportArtifactDeleteResponse>();
    }
}
