using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Contracts;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.Catalog.Endpoints;

/// <summary>
/// Starts a canonical catalog-backed restore workspace. This is intentionally
/// separate from legacy validation-id routes while downstream execution services
/// are migrated in later slices.
/// </summary>
public sealed class HostAgentCatalogRestoreSessionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/restore-session",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    CatalogRestoreSessionService catalogRestoreSessions,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var response = await catalogRestoreSessions.PrepareAsync(
                            catalogEntryId,
                            ct);

                        return Results.Ok(response);
                    }
                    catch (CatalogRestoreSourceNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "backup_catalog_entry_not_found",
                            Detail: ex.Message,
                            Message: HostAgentStructuredMessages.BackupCatalogEntryNotFound(
                                catalogEntryId)));
                    }
                    catch (CatalogRestoreSourceUnavailableException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "backup_catalog_entry_not_available",
                                Detail: ex.Message,
                                Message: HostAgentStructuredMessages.BackupCatalogEntryUnavailable(
                                    ex.CatalogEntryId,
                                    ex.PayloadState)),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_backup_catalog_restore_request",
                            Detail: ex.Message,
                            Message: HostAgentStructuredMessages.BackupCatalogRestoreRequestInvalid(
                                catalogEntryId)));
                    }
                })
            .Produces<CatalogRestoreSessionResponse>();
    }
}
