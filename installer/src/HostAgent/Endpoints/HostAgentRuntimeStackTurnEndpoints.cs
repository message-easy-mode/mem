using System.Security.Claims;
using Carter;
using HostAgent.Runtime.Stacks.Turn;
using HostAgent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HostAgent.Endpoints;

public sealed class HostAgentRuntimeStackTurnEndpoints : ICarterModule
{
    private const string BaseRoute = "/internal/host-agent/runtime-stacks/{slugOrId}/turn";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                BaseRoute,
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] IRuntimeStackTurnInspectionService inspectionService,
                    CancellationToken ct) =>
                {
                    SetNoStore(httpContext);
                    var denied = RequireCurrentOperator(httpContext);
                    if (denied is not null)
                    {
                        return denied;
                    }

                    var inspection = await inspectionService.InspectAsync(slugOrId, ct);
                    if (inspection is null)
                    {
                        return StackNotFound(slugOrId);
                    }

                    return Results.Ok(inspection);
                })
            .WithName("InspectHostAgentRuntimeStackTurn")
            .WithTags("HostAgent");

        app.MapPost(
                BaseRoute + "/connect/review",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] IRuntimeStackTurnConnectionService connectionService,
                    ILogger<HostAgentRuntimeStackTurnEndpoints> logger,
                    CancellationToken ct) =>
                {
                    SetNoStore(httpContext);
                    var denied = RequireCurrentOperator(httpContext);
                    if (denied is not null)
                    {
                        return denied;
                    }

                    try
                    {
                        var review = await connectionService.ReviewAsync(slugOrId, ct);
                        return review is null ? StackNotFound(slugOrId) : Results.Ok(review);
                    }
                    catch (RuntimeStackTurnConnectionException ex)
                    {
                        return ConnectionProblem(ex, ReviewStatusCode(ex.Code));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(
                            ex,
                            "Stack TURN connection review failed. SlugOrId={SlugOrId}",
                            slugOrId);
                        return Error(
                            "turn_connect_review_failed",
                            "MEM could not review the stack TURN connection safely.",
                            StatusCodes.Status503ServiceUnavailable);
                    }
                })
            .WithName("ReviewHostAgentRuntimeStackTurnConnect")
            .WithTags("HostAgent")
            .Produces<RuntimeStackTurnConnectReviewResponse>();

        app.MapPost(
                BaseRoute + "/connect",
                async (
                    string slugOrId,
                    [FromBody] RuntimeStackTurnConnectRequest? request,
                    HttpContext httpContext,
                    [FromServices] IRuntimeStackTurnConnectionService connectionService,
                    ILogger<HostAgentRuntimeStackTurnEndpoints> logger,
                    CancellationToken ct) =>
                {
                    SetNoStore(httpContext);
                    var denied = RequireCurrentOperator(httpContext);
                    if (denied is not null)
                    {
                        return denied;
                    }

                    if (request is null)
                    {
                        return Error(
                            "turn_connect_request_invalid",
                            "Provide the reviewed TURN connection request, confirmation, and idempotency key.",
                            StatusCodes.Status400BadRequest);
                    }

                    try
                    {
                        var result = await connectionService.ConnectAsync(
                            slugOrId,
                            request,
                            ResolveRequestedBy(httpContext.User),
                            ResolveActorOperatorId(httpContext.User),
                            ct);
                        if (result is null)
                        {
                            return StackNotFound(slugOrId);
                        }

                        var statusCode = result.Status switch
                        {
                            RuntimeStackTurnConnectStatuses.Running => StatusCodes.Status202Accepted,
                            RuntimeStackTurnConnectStatuses.CandidateRejected => StatusCodes.Status422UnprocessableEntity,
                            RuntimeStackTurnConnectStatuses.Failed or
                            RuntimeStackTurnConnectStatuses.Unresolved => StatusCodes.Status500InternalServerError,
                            _ => StatusCodes.Status200OK
                        };
                        return Results.Json(result, statusCode: statusCode);
                    }
                    catch (RuntimeStackTurnConnectionException ex)
                    {
                        return ConnectionProblem(ex, ApplyStatusCode(ex.Code));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(
                            ex,
                            "Stack TURN connection failed unexpectedly. SlugOrId={SlugOrId}",
                            slugOrId);
                        return Error(
                            "turn_connect_apply_failed",
                            "MEM could not complete the stack TURN connection safely.",
                            StatusCodes.Status500InternalServerError);
                    }
                })
            .WithName("ApplyHostAgentRuntimeStackTurnConnect")
            .WithTags("HostAgent")
            .Accepts<RuntimeStackTurnConnectRequest>("application/json")
            .Produces<RuntimeStackTurnConnectResponse>()
            .Produces<RuntimeStackTurnConnectResponse>(StatusCodes.Status202Accepted)
            .Produces<RuntimeStackTurnConnectResponse>(StatusCodes.Status500InternalServerError);

        app.MapPost(
                BaseRoute + "/disconnect/review",
                async (
                    string slugOrId,
                    HttpContext httpContext,
                    [FromServices] IRuntimeStackTurnDisconnectionService disconnectionService,
                    ILogger<HostAgentRuntimeStackTurnEndpoints> logger,
                    CancellationToken ct) =>
                {
                    SetNoStore(httpContext);
                    var denied = RequireCurrentOperator(httpContext);
                    if (denied is not null)
                    {
                        return denied;
                    }

                    try
                    {
                        var review = await disconnectionService.ReviewAsync(slugOrId, ct);
                        return review is null ? StackNotFound(slugOrId) : Results.Ok(review);
                    }
                    catch (RuntimeStackTurnConnectionException ex)
                    {
                        return ConnectionProblem(ex, DisconnectReviewStatusCode(ex.Code));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogWarning(
                            ex,
                            "Stack TURN disconnection review failed. SlugOrId={SlugOrId}",
                            slugOrId);
                        return Error(
                            "turn_disconnect_review_failed",
                            "MEM could not review the stack TURN disconnection safely.",
                            StatusCodes.Status503ServiceUnavailable);
                    }
                })
            .WithName("ReviewHostAgentRuntimeStackTurnDisconnect")
            .WithTags("HostAgent")
            .Produces<RuntimeStackTurnDisconnectReviewResponse>();

        app.MapPost(
                BaseRoute + "/disconnect",
                async (
                    string slugOrId,
                    [FromBody] RuntimeStackTurnDisconnectRequest? request,
                    HttpContext httpContext,
                    [FromServices] IRuntimeStackTurnDisconnectionService disconnectionService,
                    ILogger<HostAgentRuntimeStackTurnEndpoints> logger,
                    CancellationToken ct) =>
                {
                    SetNoStore(httpContext);
                    var denied = RequireCurrentOperator(httpContext);
                    if (denied is not null)
                    {
                        return denied;
                    }

                    if (request is null)
                    {
                        return Error(
                            "turn_disconnect_request_invalid",
                            "Provide the reviewed TURN disconnection request, confirmation, and idempotency key.",
                            StatusCodes.Status400BadRequest);
                    }

                    try
                    {
                        var result = await disconnectionService.DisconnectAsync(
                            slugOrId,
                            request,
                            ResolveRequestedBy(httpContext.User),
                            ResolveActorOperatorId(httpContext.User),
                            ct);
                        if (result is null)
                        {
                            return StackNotFound(slugOrId);
                        }

                        var statusCode = result.Status switch
                        {
                            RuntimeStackTurnDisconnectStatuses.Running => StatusCodes.Status202Accepted,
                            RuntimeStackTurnDisconnectStatuses.CandidateRejected => StatusCodes.Status422UnprocessableEntity,
                            RuntimeStackTurnDisconnectStatuses.Failed or
                            RuntimeStackTurnDisconnectStatuses.Unresolved => StatusCodes.Status500InternalServerError,
                            _ => StatusCodes.Status200OK
                        };
                        return Results.Json(result, statusCode: statusCode);
                    }
                    catch (RuntimeStackTurnConnectionException ex)
                    {
                        return ConnectionProblem(ex, DisconnectApplyStatusCode(ex.Code));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(
                            ex,
                            "Stack TURN disconnection failed unexpectedly. SlugOrId={SlugOrId}",
                            slugOrId);
                        return Error(
                            "turn_disconnect_apply_failed",
                            "MEM could not complete the stack TURN disconnection safely.",
                            StatusCodes.Status500InternalServerError);
                    }
                })
            .WithName("ApplyHostAgentRuntimeStackTurnDisconnect")
            .WithTags("HostAgent")
            .Accepts<RuntimeStackTurnDisconnectRequest>("application/json")
            .Produces<RuntimeStackTurnDisconnectResponse>()
            .Produces<RuntimeStackTurnDisconnectResponse>(StatusCodes.Status202Accepted)
            .Produces<RuntimeStackTurnDisconnectResponse>(StatusCodes.Status500InternalServerError);
    }

    private static IResult? RequireCurrentOperator(HttpContext httpContext) =>
        HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(httpContext.User)
            ? null
            : Error(
                "turn_operator_session_required",
                "A current control-plane operator session is required.",
                StatusCodes.Status401Unauthorized);

    private static int ReviewStatusCode(string code) =>
        code switch
        {
            "turn_connect_no_change" => StatusCodes.Status409Conflict,
            "turn_connect_platform_not_ready" or
            "turn_connect_external_configuration" or
            "turn_connect_state_unavailable" or
            "turn_connect_drift_requires_attention" or
            "turn_connect_matrix_runtime_unavailable" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status422UnprocessableEntity
        };

    private static int ApplyStatusCode(string code) =>
        code switch
        {
            "turn_connect_stack_not_found" => StatusCodes.Status404NotFound,
            "turn_connect_operation_in_progress" or
            "turn_connect_review_stale" or
            "turn_connect_no_change" or
            "turn_connect_idempotency_conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

    private static int DisconnectReviewStatusCode(string code) =>
        code switch
        {
            "turn_disconnect_no_change" => StatusCodes.Status409Conflict,
            "turn_disconnect_external_configuration" or
            "turn_disconnect_state_unavailable" or
            "turn_disconnect_drift_requires_attention" or
            "turn_disconnect_matrix_runtime_unavailable" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status422UnprocessableEntity
        };

    private static int DisconnectApplyStatusCode(string code) =>
        code switch
        {
            "turn_disconnect_stack_not_found" => StatusCodes.Status404NotFound,
            "turn_disconnect_operation_in_progress" or
            "turn_disconnect_review_stale" or
            "turn_disconnect_no_change" or
            "turn_disconnect_idempotency_conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

    private static IResult ConnectionProblem(
        RuntimeStackTurnConnectionException ex,
        int statusCode) =>
        Error(ex.Code, ex.Message, statusCode);

    private static IResult StackNotFound(string slugOrId) =>
        Error(
            "turn_stack_not_found",
            $"Runtime stack '{slugOrId}' was not found.",
            StatusCodes.Status404NotFound);

    private static IResult Error(
        string code,
        string detail,
        int statusCode) =>
        Results.Json(
            new { error = code, detail },
            statusCode: statusCode);

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

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
