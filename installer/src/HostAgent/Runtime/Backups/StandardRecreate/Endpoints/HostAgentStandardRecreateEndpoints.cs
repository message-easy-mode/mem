using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Runtime.Backups.StandardRecreate.Endpoints;

public sealed class HostAgentStandardRecreateEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Standard Recreate");

        group.MapPost(
                "/backups/restores/{restoreSessionId}/standard-recreate/preflight",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    string? targetStackSlug,
                    string? requestedDomainId,
                    string? matrixHost,
                    string? elementHost,
                    [FromServices] StandardRecreatePreflightService preflightService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await preflightService.AssessAsync(
                            restoreSessionId,
                            new StandardRecreatePreflightRequest(
                                TargetStackSlug: targetStackSlug,
                                RequestedDomainId: requestedDomainId,
                                MatrixHost: matrixHost,
                                ElementHost: elementHost),
                            ct);

                        return result is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                Error: "restore_attempt_not_found",
                                Detail: $"Restore attempt '{restoreSessionId}' was not found."))
                            : Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_standard_recreate_preflight_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<StandardRecreatePreflightResponse>();

        group.MapPost(
                "/backups/restores/{restoreSessionId}/standard-recreate",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    string? targetStackSlug,
                    string? requestedDomainId,
                    string? matrixHost,
                    string? elementHost,
                    string? matrixImage,
                    string? elementImage,
                    string? operatorName,
                    string? note,
                    bool? executeProductionRecreate,
                    bool? acknowledgeCreatesRealStack,
                    bool? acknowledgeMutatesProductionPostgres,
                    bool? acknowledgeMutatesNpmRoutes,
                    bool? acknowledgeNoAutomaticRollback,
                    CancellationToken ct) =>
                {
                    // Standard Recreate creates a real public runtime and
                    // mutates production Postgres, Docker, and NPM routes.
                    // Ordinary control-plane authentication is therefore not
                    // sufficient: require the current browser session to have
                    // a fresh server-side password-plus-TOTP grant before any
                    // destructive restore service is resolved.
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

                    var recreateService = httpContext.RequestServices
                        .GetRequiredService<RestoreWorkspaceStandardRecreateService>();

                    try
                    {
                        var request = new StandardRecreateRequest(
                            TargetStackSlug: targetStackSlug,
                            RequestedDomainId: requestedDomainId,
                            MatrixHost: matrixHost,
                            ElementHost: elementHost,
                            MatrixImage: matrixImage,
                            ElementImage: elementImage,
                            Operator: operatorName,
                            Note: note,
                            ExecuteProductionRecreate: executeProductionRecreate ?? false,
                            AcknowledgeCreatesRealStack: acknowledgeCreatesRealStack ?? false,
                            AcknowledgeMutatesProductionPostgres: acknowledgeMutatesProductionPostgres ?? false,
                            AcknowledgeMutatesNpmRoutes: acknowledgeMutatesNpmRoutes ?? false,
                            AcknowledgeNoAutomaticRollback: acknowledgeNoAutomaticRollback ?? false);

                        var result = await recreateService.ExecuteAsync(
                            restoreSessionId,
                            request,
                            ct);

                        return result is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                Error: "restore_attempt_not_found",
                                Detail: $"Restore attempt '{restoreSessionId}' was not found."))
                            : Results.Ok(result);
                    }
                    catch (RestoreAttemptConflictException ex)
                    {
                        return Results.Conflict(ex.Conflict);
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
                            Error: "invalid_workspace_standard_recreate_request",
                            Detail: ex.Message));
                    }
                    catch (InvalidDataException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_production_recreate_source",
                            Detail: ex.Message));
                    }
                    catch (DirectoryNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "production_recreate_source_not_found",
                            Detail: ex.Message));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "production_recreate_source_not_found",
                            Detail: ex.Message));
                    }
                })
            .Produces<StandardRecreateResult>()
            .Produces<RestoreAttemptConflictResponse>(
                StatusCodes.Status409Conflict);
    }
}
