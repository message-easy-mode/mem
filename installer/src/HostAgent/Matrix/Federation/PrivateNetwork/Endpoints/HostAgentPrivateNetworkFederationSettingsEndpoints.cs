using System.Security.Claims;
using Carter;
using HostAgent.Commands;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Modules.Auth.Identity;

namespace HostAgent.Matrix.Federation.PrivateNetwork.Endpoints;

public sealed class HostAgentPrivateNetworkFederationSettingsEndpoints : ICarterModule
{
    private const string BaseRoute = "/internal/host-agent/security/private-network-federation";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(BaseRoute, async (
                HttpContext httpContext,
                IRuntimeStackPrivateNetworkFederationService service,
                CancellationToken ct) =>
            {
                var denied = RequirePlatformOwner(httpContext);
                if (denied is not null) return denied;
                SetNoStore(httpContext);
                return Results.Ok(await service.GetInventoryAsync(ct));
            })
            .WithName("ListPrivateNetworkFederationSettings")
            .WithTags("Security Settings / Private Network Federation")
            .Produces<PrivateNetworkFederationInventoryResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status401Unauthorized)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status403Forbidden);

        app.MapPost(BaseRoute + "/{slugOrId}/review", async (
                string slugOrId,
                [FromBody] PrivateNetworkFederationReviewRequest? request,
                HttpContext httpContext,
                IRuntimeStackPrivateNetworkFederationService service,
                ILogger<HostAgentPrivateNetworkFederationSettingsEndpoints> logger,
                CancellationToken ct) =>
            {
                var denied = RequirePlatformOwner(httpContext);
                if (denied is not null) return denied;
                SetNoStore(httpContext);
                if (request is null)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        "private_network_request_invalid",
                        "Provide one exact private address and choose Add or Remove."));
                }

                try
                {
                    var review = await service.ReviewAsync(slugOrId, request, ct);
                    return review is null ? StackNotFound() : Results.Ok(review);
                }
                catch (PrivateNetworkFederationException ex)
                {
                    return Problem(ex, StatusCodes.Status422UnprocessableEntity);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex,
                        "Private network federation review failed. SlugOrId={SlugOrId}",
                        slugOrId);
                    return Results.Json(
                        new HostAgentErrorResponse(
                            "private_network_state_unavailable",
                            "MEM could not review the exact private network exception safely."),
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
            .WithName("ReviewPrivateNetworkFederationSetting")
            .WithTags("Security Settings / Private Network Federation")
            .Accepts<PrivateNetworkFederationReviewRequest>("application/json")
            .Produces<PrivateNetworkFederationReviewResponse>()
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        app.MapPost(BaseRoute + "/{slugOrId}/apply", async (
                string slugOrId,
                [FromBody] PrivateNetworkFederationApplyRequest? request,
                HttpContext httpContext,
                ILogger<HostAgentPrivateNetworkFederationSettingsEndpoints> logger,
                CancellationToken ct) =>
            {
                var denied = RequirePlatformOwner(httpContext);
                if (denied is not null) return denied;
                SetNoStore(httpContext);

                var authorization = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
                var stepUp = await authorization.AuthorizeAsync(
                    httpContext.User,
                    resource: null,
                    policyName: MemOperatorPolicies.RecentStepUp);
                if (!stepUp.Succeeded)
                {
                    return Results.Json(
                        new HostAgentErrorResponse(
                            "step_up_required",
                            "Fresh password and authenticator verification is required before changing Synapse outbound network policy."),
                        statusCode: StatusCodes.Status403Forbidden);
                }

                if (request is null)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        "private_network_request_invalid",
                        "Provide the reviewed exact private address request, review hash, and idempotency key."));
                }

                // Resolve the mutating service only after Platform Owner and
                // unconditional recent-step-up checks have succeeded.
                var service = httpContext.RequestServices
                    .GetRequiredService<IRuntimeStackPrivateNetworkFederationService>();
                try
                {
                    var result = await service.ApplyAsync(
                        slugOrId,
                        request,
                        ResolveRequestedBy(httpContext.User),
                        ResolveActorOperatorId(httpContext.User),
                        ct);
                    if (result is null) return StackNotFound();
                    var statusCode = result.Status switch
                    {
                        "running" => StatusCodes.Status202Accepted,
                        "candidate_rejected" => StatusCodes.Status422UnprocessableEntity,
                        "failed" => StatusCodes.Status500InternalServerError,
                        _ => StatusCodes.Status200OK
                    };
                    return Results.Json(result, statusCode: statusCode);
                }
                catch (PrivateNetworkFederationException ex)
                {
                    var status = ex.Code switch
                    {
                        "private_network_stack_not_found" => StatusCodes.Status404NotFound,
                        "private_network_operation_in_progress" or
                        "private_network_review_stale" or
                        "private_network_no_change" or
                        "private_network_idempotency_conflict" => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status422UnprocessableEntity
                    };
                    return Problem(ex, status);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex,
                        "Private network federation apply failed unexpectedly. SlugOrId={SlugOrId}",
                        slugOrId);
                    return Results.Json(
                        new HostAgentErrorResponse(
                            "private_network_apply_failed",
                            "MEM could not complete the exact private network exception operation safely."),
                        statusCode: StatusCodes.Status500InternalServerError);
                }
            })
            .WithName("ApplyPrivateNetworkFederationSetting")
            .WithTags("Security Settings / Private Network Federation")
            .Accepts<PrivateNetworkFederationApplyRequest>("application/json")
            .Produces<PrivateNetworkFederationApplyResponse>()
            .Produces<PrivateNetworkFederationApplyResponse>(StatusCodes.Status202Accepted)
            .Produces<PrivateNetworkFederationApplyResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status403Forbidden)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<HostAgentErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<PrivateNetworkFederationApplyResponse>(StatusCodes.Status500InternalServerError);
    }

    private static IResult? RequirePlatformOwner(HttpContext httpContext)
    {
        var hasNamedSession =
            HostAgentEndpointOperatorGuard.HasNamedControlPlaneSession(httpContext.User);
        var hasTransitionalSession =
            HostAgentEndpointOperatorGuard.HasTransitionalInstallerSession(httpContext.User);

        if (!hasNamedSession && !hasTransitionalSession)
        {
            return Results.Unauthorized();
        }

        if (!HostAgentEndpointOperatorGuard.HasNamedPlatformOwnerSession(httpContext.User))
        {
            return Results.Json(
                new HostAgentErrorResponse(
                    "platform_owner_required",
                    "Only a named Platform Owner can view or change private network exceptions."),
                statusCode: StatusCodes.Status403Forbidden);
        }

        return null;
    }

    private static IResult Problem(PrivateNetworkFederationException ex, int statusCode) =>
        Results.Json(new HostAgentErrorResponse(ex.Code, ex.Message), statusCode: statusCode);

    private static IResult StackNotFound() =>
        Results.Json(
            new HostAgentErrorResponse(
                "private_network_stack_not_found",
                "No Runtime Stack exists with the supplied slug or id."),
            statusCode: StatusCodes.Status404NotFound);

    private static string ResolveRequestedBy(ClaimsPrincipal user) =>
        user.Identity?.Name?.Trim() is { Length: > 0 } name
            ? name
            : user.FindFirstValue(ClaimTypes.NameIdentifier)?.Trim() is { Length: > 0 } id
                ? id
                : "control-plane-platform-owner";

    private static Guid? ResolveActorOperatorId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
