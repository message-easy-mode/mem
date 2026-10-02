using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HostAgent.Runtime.Backups.Artifacts.PortableExports.Endpoints;

public sealed class HostAgentPortableExportEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Artifacts / Portable Exports");

        group.MapPost(
                "/backups/artifacts/portable-exports/from-local-backups/stacks/{stackSlug}/{backupId}",
                async (
                    HttpContext httpContext,
                    string stackSlug,
                    string backupId,
                    PortableExportService exportService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await exportService.ExportAsync(
                            stackSlug,
                            backupId,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_export_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_export_source_not_found",
                            Detail: ex.Message));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_export_source_not_found",
                            Detail: ex.Message));
                    }
                });

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/portable-export",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    CatalogPortableExportService catalogExportService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await catalogExportService.ExportAsync(
                            catalogEntryId,
                            ct);

                        return Results.Ok(result);
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
                            Error: "invalid_catalog_backup_export_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_catalog_export_source_not_found",
                            Detail: ex.Message));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_catalog_export_source_not_found",
                            Detail: ex.Message));
                    }
                });

        group.MapGet(
                "/backups/artifacts/portable-exports/{exportId}/download",
                async (
                    HttpContext httpContext,
                    string exportId,
                    PortableExportService exportService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var export = await exportService.ResolveDownloadAsync(
                            exportId,
                            ct);

                        return Results.File(
                            path: export.ExportPath,
                            contentType: "application/zip",
                            fileDownloadName: export.DownloadName,
                            enableRangeProcessing: true);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_export_download_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_export_not_found",
                            Detail: ex.Message));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_export_not_found",
                            Detail: ex.Message));
                    }
                });
    }
}