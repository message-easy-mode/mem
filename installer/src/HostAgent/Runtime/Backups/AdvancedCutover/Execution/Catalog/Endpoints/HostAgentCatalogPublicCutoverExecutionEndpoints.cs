using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog.Endpoints;

public sealed class HostAgentCatalogPublicCutoverExecutionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Catalog / Public Cutover Execution");

        group.MapPost(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-executions/execute",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    [FromBody] CatalogPublicCutoverExecutionRequest? request,
                    CancellationToken ct) =>
                {
                    // A public cutover can capture a final backup, stop the
                    // existing runtime, and mutate live NPM routes. Ordinary
                    // control-plane authentication is therefore not enough:
                    // require a fresh, server-side password-plus-current-TOTP
                    // grant bound to this exact browser session before resolving
                    // any execution service or beginning external mutation.
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();

                    var authResult = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    if (request is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "backup_catalog_cutover_execution_body_required",
                            Detail: "Provide confirmationId, candidateId, oldRuntimeStackSlug, execute=true, and all explicit acknowledgement fields."));
                    }

                    var executionService = httpContext.RequestServices
                        .GetRequiredService<CatalogPublicCutoverExecutionService>();

                    try
                    {
                        return Results.Ok(await executionService.ExecuteAsync(catalogEntryId, request, ct));
                    }
                    catch (CatalogRestoreSourceNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse("backup_catalog_entry_not_found", ex.Message));
                    }
                    catch (CatalogRestoreSourceUnavailableException ex)
                    {
                        return Results.Json(new HostAgentErrorResponse("backup_catalog_entry_not_available", ex.Message), statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse("backup_catalog_cutover_execution_source_not_found", ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse("invalid_backup_catalog_cutover_execution_request", ex.Message));
                    }
                })
            .Accepts<CatalogPublicCutoverExecutionRequest>("application/json")
            .Produces<CatalogPublicCutoverExecutionResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict);

        group.MapGet(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-executions",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    string? confirmationId,
                    string? candidateId,
                    string? status,
                    int? max,
                    [FromServices] CatalogPublicCutoverExecutionHistoryService historyService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        return Results.Ok(await historyService.ListExecutionsAsync(catalogEntryId, confirmationId, candidateId, status, max, ct));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse("invalid_backup_catalog_cutover_execution_history_request", ex.Message));
                    }
                })
            .Produces<CatalogPublicCutoverExecutionHistoryResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest);

        group.MapGet(
                "/backups/catalog/{catalogEntryId}/production-restore/cutover-executions/{executionId}",
                async (
                    HttpContext httpContext,
                    string catalogEntryId,
                    string executionId,
                    [FromServices] CatalogPublicCutoverExecutionHistoryService historyService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await historyService.GetExecutionAsync(catalogEntryId, executionId, ct);
                        return result is null
                            ? Results.NotFound(new HostAgentErrorResponse("backup_catalog_cutover_execution_not_found", $"Catalog public cutover execution '{executionId}' was not found for Backup Catalog entry '{catalogEntryId}'."))
                            : Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse("invalid_backup_catalog_cutover_execution_history_request", ex.Message));
                    }
                })
            .Produces<CatalogPublicCutoverExecutionDetailResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound);
    }
}
