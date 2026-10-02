using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.StandardRecreate.Cleanup;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Runtime.Backups.StandardRecreate.Cleanup.Endpoints;

public sealed class HostAgentFailedStandardRecreateCleanupEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Standard Recreate / Failed Cleanup");

        group.MapGet(
                "/backups/standard-recreate/runs/{recreateId}/failed-cleanup/assessment",
                async (
                    HttpContext httpContext,
                    string recreateId,
                    [FromServices] FailedStandardRecreateCleanupService cleanupService,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        return Results.Ok(await cleanupService.AssessAsync(recreateId, ct));
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "production_recreate_run_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_failed_recreate_cleanup_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<FailedStandardRecreateCleanupAssessment>();

        group.MapPost(
                "/backups/standard-recreate/runs/{recreateId}/failed-cleanup/execute",
                async (
                    HttpContext httpContext,
                    string recreateId,
                    string? operatorName,
                    bool? acknowledgeCleanup,
                    CancellationToken ct) =>
                {
                    // Failed Standard Recreate cleanup removes recorded partial
                    // production resources, including NPM routes, containers, and
                    // retained runtime directories. A normal signed-in session is
                    // therefore not sufficient: require fresh password-plus-TOTP
                    // step-up before the cleanup service can be resolved or run.
                    var authorizationService = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();

                    var stepUpRequired = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorizationService);

                    if (stepUpRequired is not null)
                    {
                        return stepUpRequired;
                    }

                    var cleanupService = httpContext.RequestServices
                        .GetRequiredService<FailedStandardRecreateCleanupService>();

                    try
                    {
                        var result = await cleanupService.ExecuteAsync(
                            recreateId,
                            new FailedStandardRecreateCleanupRequest(
                                Operator: operatorName,
                                AcknowledgeCleanup: acknowledgeCleanup ?? false),
                            ct);

                        return Results.Ok(result);
                    }
                    catch (FailedStandardRecreateCleanupConflictException ex)
                    {
                        return Results.Conflict(ex.Conflict);
                    }
                    catch (FileNotFoundException ex)
                    {
                        return Results.NotFound(new HostAgentErrorResponse(
                            Error: "production_recreate_run_not_found",
                            Detail: ex.Message));
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_failed_recreate_cleanup_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<FailedStandardRecreateCleanupResult>()
            .Produces<FailedStandardRecreateCleanupConflictResponse>(StatusCodes.Status409Conflict);
    }
}
