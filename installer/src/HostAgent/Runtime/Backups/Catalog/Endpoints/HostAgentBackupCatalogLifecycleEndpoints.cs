using Carter;
using HostAgent.Commands;
using HostAgent.Security;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Runtime.Backups.Catalog.Endpoints;

public sealed class HostAgentBackupCatalogLifecycleEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog Lifecycle");

        group.MapGet(
            "/backups/catalog/{catalogEntryId}/lifecycle",
            async (
                string catalogEntryId,
                HttpContext context,
                [FromServices] BackupCatalogLifecycleService lifecycleService,
                CancellationToken ct) =>
            {
                var denied = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(context);

                if (denied is not null)
                {
                    return denied;
                }

                var response = await lifecycleService.GetAsync(
                    catalogEntryId,
                    ct);

                return response is null
                    ? Results.NotFound(
                        new HostAgentErrorResponse(
                            "backup_catalog_entry_not_found",
                            "No Backup Catalog entry exists with the supplied catalog entry id."))
                    : Results.Ok(response);
            });

        group.MapDelete(
            "/backups/catalog/{catalogEntryId}",
            async (
                string catalogEntryId,
                HttpContext context,
                CancellationToken ct) =>
            {
                // Resolve the authorization service before any destructive
                // service. This makes direct API denial deterministic: without
                // a valid session-bound step-up grant, no catalog lifecycle
                // service is reached and no payload/artifact mutation starts.
                var authorization = context.RequestServices
                    .GetRequiredService<IAuthorizationService>();

                var denied = await HostAgentEndpointOperatorGuard
                    .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                        context,
                        authorization);

                if (denied is not null)
                {
                    return denied;
                }

                var userManager = context.RequestServices
                    .GetRequiredService<UserManager<MemOperator>>();

                var currentOperator = await userManager.GetUserAsync(context.User);

                if (currentOperator is null)
                {
                    return Results.Unauthorized();
                }

                var lifecycleService = context.RequestServices
                    .GetRequiredService<BackupCatalogLifecycleService>();

                try
                {
                    // The displayed/audited deletion actor comes from the
                    // server-side Identity session. A browser must not be able
                    // to choose an arbitrary actor name in a DELETE payload.
                    var response = await lifecycleService.DeleteAsync(
                        catalogEntryId,
                        currentOperator.UserName,
                        ct);

                    return response is null
                        ? Results.NotFound(
                            new HostAgentErrorResponse(
                                "backup_catalog_entry_not_found",
                                "No Backup Catalog entry exists with the supplied catalog entry id."))
                        : Results.Ok(response);
                }
                catch (BackupCatalogLifecycleConflictException ex)
                {
                    return Results.Conflict(
                        new HostAgentErrorResponse(
                            "backup_catalog_delete_conflict",
                            ex.Message));
                }
            });

        group.MapGet(
            "/backups/catalog/imports/{validationId}/archive",
            async (
                string validationId,
                HttpContext context,
                [FromServices] BackupCatalogLifecycleService lifecycleService,
                CancellationToken ct) =>
            {
                var denied = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(context);

                if (denied is not null)
                {
                    return denied;
                }

                var response = await lifecycleService.GetArchiveAsync(
                    validationId,
                    ct);

                return response is null
                    ? Results.NotFound(
                        new HostAgentErrorResponse(
                            "backup_catalog_import_not_found",
                            "No imported Backup Catalog entry exists with the supplied validation id."))
                    : Results.Ok(response);
            });

        group.MapDelete(
            "/backups/catalog/imports/{validationId}/archive",
            async (
                string validationId,
                [FromBody] BackupCatalogImportArchiveDeleteRequest request,
                HttpContext context,
                [FromServices] BackupCatalogLifecycleService lifecycleService,
                CancellationToken ct) =>
            {
                var denied = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(context);

                if (denied is not null)
                {
                    return denied;
                }

                var response = await lifecycleService.DeleteArchiveAsync(
                    validationId,
                    request.Operator,
                    ct);

                return response is null
                    ? Results.NotFound(
                        new HostAgentErrorResponse(
                            "backup_catalog_import_not_found",
                            "No imported Backup Catalog entry exists with the supplied validation id."))
                    : Results.Ok(response);
            });
    }
}
