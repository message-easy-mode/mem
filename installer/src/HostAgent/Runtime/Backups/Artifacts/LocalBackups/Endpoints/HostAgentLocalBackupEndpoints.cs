using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HostAgent.Runtime.Backups.Artifacts.LocalBackups.Endpoints;

public sealed class HostAgentLocalBackupEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Artifacts / Local Backups");

        group.MapGet(
                "/backups/artifacts/local-backups",
                async (
                    HttpContext httpContext,
                    LocalBackupCatalogService backupHistoryService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    var result = await backupHistoryService.ListAsync(ct);

                    return Results.Ok(result);
                });

        group.MapGet(
                "/backups/artifacts/local-backups/entries",
                async (
                    HttpContext httpContext,
                    int? page,
                    int? pageSize,
                    string? search,
                    string? stackSlug,
                    string? sortBy,
                    string? sortDirection,
                    LocalBackupCatalogService backupHistoryService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var query = new LocalBackupEntryQuery(
                            Page: page ?? 1,
                            PageSize: pageSize ?? 10,
                            Search: search,
                            StackSlug: stackSlug,
                            SortBy: sortBy,
                            SortDirection: sortDirection);

                        var result = await backupHistoryService.ListEntriesAsync(
                            query,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // A changed table filter, browser navigation, or request abort is not a
                        // HostAgent failure. Mirror Restore Sessions and avoid reporting it as 500.
                        return Results.StatusCode(499);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_local_backup_entry_list_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<LocalBackupEntryListResponse>();

        group.MapGet(
                "/backups/artifacts/local-backups/stacks/{stackSlug}",
                async (
                    HttpContext httpContext,
                    string stackSlug,
                    LocalBackupCatalogService backupHistoryService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await backupHistoryService.ListStackAsync(
                            stackSlug,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_history_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_history_not_found",
                            Detail: ex.Message));
                    }
                });

        group.MapGet(
                "/backups/artifacts/local-backups/stacks/{stackSlug}/{backupId}",
                async (
                    HttpContext httpContext,
                    string stackSlug,
                    string backupId,
                    LocalBackupCatalogService backupHistoryService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await backupHistoryService.InspectAsync(
                            stackSlug,
                            backupId,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_history_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_not_found",
                            Detail: ex.Message));
                    }
                });

        group.MapDelete(
                "/backups/artifacts/local-backups/stacks/{stackSlug}/{backupId}",
                async (
                    HttpContext httpContext,
                    string stackSlug,
                    string backupId,
                    bool? acknowledgeDelete,
                    LocalBackupCatalogService backupHistoryService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await backupHistoryService.DeleteAsync(
                            stackSlug,
                            backupId,
                            acknowledgeDelete ?? false,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_local_backup_delete_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "local_backup_not_found",
                            Detail: ex.Message));
                    }
                    catch (LocalBackupActiveRestoreConflictException ex)
                    {
                        return Results.Json(
                            new RestoreAttemptConflictResponse(
                                Code: "active_restore_exists",
                                RestoreSessionId: ex.RestoreSessionId,
                                ResourceType: "source-backup",
                                ResourceValue: $"{stackSlug}/{backupId}",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (IOException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "local_backup_delete_conflict",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "local_backup_delete_failed",
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                })
            .Produces<LocalBackupDeleteResponse>();

        group.MapPost(
                "/backups/artifacts/local-backups/stacks/{slugOrId}",
                async (
                    HttpContext httpContext,
                    string slugOrId,
                    LocalBackupCaptureService backupService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await backupService.BackupAsync(
                            slugOrId,
                            ct);

                        return Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_request",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_source_missing",
                            Detail: ex.Message));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_source_missing",
                            Detail: ex.Message));
                    }
                });
    }
}