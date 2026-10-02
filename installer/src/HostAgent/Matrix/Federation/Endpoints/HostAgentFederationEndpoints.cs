using Carter;
using System.Security.Claims;
using HostAgent.Commands;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HostAgent.Matrix.Federation.Endpoints;

public sealed class HostAgentFederationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/internal/host-agent/runtime-stacks/{slugOrId}/federation",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    IRuntimeStackFederationStateService stateService,
                    ILogger<HostAgentFederationEndpoints> logger,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard
                        .ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    httpContext.Response.Headers.CacheControl = "no-store";

                    try
                    {
                        var state = await stateService.GetAsync(slugOrId, ct);
                        if (state is null)
                        {
                            return StackNotFound();
                        }

                        return Results.Ok(state);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Federation state inspection failed. SlugOrId={SlugOrId}",
                            slugOrId);

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "federation_state_unavailable",
                                Detail: "MEM could not inspect the current federation state safely."),
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                })
            .WithName("InspectHostAgentRuntimeStackFederation")
            .WithTags("Host Agent / Runtime Stacks / Federation")
            .Produces<RuntimeStackFederationStateResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/federation/review",
                async (
                    string slugOrId,
                    [FromBody] RuntimeStackFederationPolicyRequest? request,
                    HttpContext httpContext,
                    IRuntimeStackFederationReviewService reviewService,
                    ILogger<HostAgentFederationEndpoints> logger,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard
                        .ValidateCurrentControlPlaneSession(httpContext);

                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    httpContext.Response.Headers.CacheControl = "no-store";

                    if (request is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "federation_policy_invalid",
                            Detail: "Provide a Public, Restricted, or Local-only federation mode and its exact allowlist."));
                    }

                    try
                    {
                        var review = await reviewService.ReviewAsync(slugOrId, request, ct);
                        if (review is null)
                        {
                            return StackNotFound();
                        }

                        return Results.Ok(review);
                    }
                    catch (FederationPolicyValidationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "federation_policy_invalid",
                            Detail: ex.Message));
                    }
                    catch (FederationReviewBlockedException ex)
                    {
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: ex.Code,
                                Detail: ex.Message),
                            statusCode: StatusCodes.Status409Conflict);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Federation policy review failed. SlugOrId={SlugOrId}",
                            slugOrId);

                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "federation_state_unavailable",
                                Detail: "MEM could not review the federation policy safely."),
                            statusCode: StatusCodes.Status503ServiceUnavailable);
                    }
                })
            .WithName("ReviewHostAgentRuntimeStackFederation")
            .WithTags("Host Agent / Runtime Stacks / Federation")
            .Accepts<RuntimeStackFederationPolicyRequest>("application/json")
            .Produces<RuntimeStackFederationReviewResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        app.MapPost(
                "/internal/host-agent/runtime-stacks/{slugOrId}/federation/apply",
                async (
                    string slugOrId,
                    [FromBody] RuntimeStackFederationApplyRequest? request,
                    HttpContext httpContext,
                    ILogger<HostAgentFederationEndpoints> logger,
                    CancellationToken ct) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";

                    var authorization = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();
                    var authResult = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorization);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    if (request is null)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "federation_policy_invalid",
                            Detail: "Provide the reviewed Public, Restricted, or Local-only federation request, review hash, and idempotency key."));
                    }

                    // Resolve the mutating service only after current-session and
                    // recent-step-up enforcement has succeeded.
                    var applyService = httpContext.RequestServices
                        .GetRequiredService<IRuntimeStackFederationApplyService>();

                    try
                    {
                        var result = await applyService.ApplyAsync(
                            slugOrId,
                            request,
                            ResolveRequestedBy(httpContext.User),
                            ResolveActorOperatorId(httpContext.User),
                            ct);
                        if (result is null)
                        {
                            return StackNotFound();
                        }

                        var statusCode = result.Status switch
                        {
                            "running" => StatusCodes.Status202Accepted,
                            "candidate_rejected" => StatusCodes.Status422UnprocessableEntity,
                            "failed" => StatusCodes.Status500InternalServerError,
                            _ => StatusCodes.Status200OK
                        };
                        return Results.Json(result, statusCode: statusCode);
                    }
                    catch (FederationPolicyValidationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "federation_policy_invalid",
                            Detail: ex.Message));
                    }
                    catch (FederationApplyBlockedException ex)
                    {
                        var statusCode = string.Equals(
                                ex.Code,
                                "federation_policy_invalid",
                                StringComparison.Ordinal)
                            ? StatusCodes.Status400BadRequest
                            : string.Equals(
                                ex.Code,
                                "federation_stack_not_found",
                                StringComparison.Ordinal)
                                ? StatusCodes.Status404NotFound
                                : StatusCodes.Status409Conflict;
                        return Results.Json(
                            new HostAgentErrorResponse(ex.Code, ex.Message),
                            statusCode: statusCode);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex,
                            "Federation policy application failed unexpectedly. SlugOrId={SlugOrId}",
                            slugOrId);
                        return Results.Json(
                            new HostAgentErrorResponse(
                                Error: "federation_apply_failed",
                                Detail: "MEM could not complete the federation policy operation safely."),
                            statusCode: StatusCodes.Status500InternalServerError);
                    }
                })
            .WithName("ApplyHostAgentRuntimeStackFederation")
            .WithTags("Host Agent / Runtime Stacks / Federation")
            .Accepts<RuntimeStackFederationApplyRequest>("application/json")
            .Produces<RuntimeStackFederationApplyResponse>()
            .Produces<RuntimeStackFederationApplyResponse>(StatusCodes.Status202Accepted)
            .Produces<RuntimeStackFederationApplyResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<RuntimeStackFederationApplyResponse>(StatusCodes.Status500InternalServerError);
    }

    private static string ResolveRequestedBy(ClaimsPrincipal user) =>
        user.Identity?.Name?.Trim() is { Length: > 0 } name
            ? name
            : user.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim() is { Length: > 0 } id
                ? id
                : "control-plane-operator";

    private static Guid? ResolveActorOperatorId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;

    private static IResult StackNotFound() => Results.Json(
        new HostAgentErrorResponse(
            Error: "federation_stack_not_found",
            Detail: "No Runtime Stack exists with the supplied slug or id."),
        statusCode: StatusCodes.Status404NotFound);
}
