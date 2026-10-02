// HostAgent/Endpoints/HostAgentCoturnEndpoints.cs

using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Coturn;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace HostAgent.Endpoints;

public sealed class HostAgentCoturnEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent");

        group.MapGet("/platform/coturn",
            async (
                HttpContext httpContext,
                CoturnRuntimeService coturn,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var result = await coturn.InspectAsync(ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "invalid_coturn_request",
                        Detail: ex.Message));
                }
            });

        group.MapGet("/platform/coturn/startup-supervision",
            (
                HttpContext httpContext,
                CoturnStartupSupervisionState startupSupervision) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";
                return Results.Ok(startupSupervision.Read());
            });

        group.MapPost("/platform/coturn/ensure",
            async (
                HttpContext httpContext,
                CoturnEnsureRequest? request,
                CoturnRuntimeService coturn,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var result = await coturn.EnsureStartedAsync(
                        request ?? new CoturnEnsureRequest(),
                        ct);

                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: ClassifyEnsureError(ex),
                        Detail: ex.Message));
                }
            });


        group.MapPost("/platform/coturn/install",
            async (
                HttpContext httpContext,
                CoturnPlatformInstallRequest? request,
                CoturnPlatformInstallOperationService installService,
                IAuthorizationService authorizationService,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    await HostAgentEndpointOperatorGuard.ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                        httpContext,
                        authorizationService);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var accepted = await installService.AcceptAsync(
                        request ?? new CoturnPlatformInstallRequest(),
                        httpContext.User.Identity?.Name ?? "platform-owner",
                        ct);

                    return Results.Accepted(accepted.PollUrl, accepted);
                }
                catch (CoturnPlatformInstallException ex)
                {
                    return Results.Json(
                        new HostAgentErrorResponse(
                            Error: ex.Code,
                            Detail: ex.Message),
                        statusCode: ex.StatusCode);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "coturn_install_invalid",
                        Detail: ex.Message));
                }
            });

        group.MapPost("/platform/coturn/maintenance",
            async (
                HttpContext httpContext,
                CoturnPlatformMaintenanceRequest? request,
                CoturnPlatformMaintenanceOperationService maintenanceService,
                IAuthorizationService authorizationService,
                CancellationToken ct) =>
            {
                var authorizationResult = await ValidateMaintenanceAuthorizationAsync(
                    httpContext,
                    request,
                    authorizationService);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var accepted = await maintenanceService.AcceptAsync(
                        request ?? new CoturnPlatformMaintenanceRequest(null),
                        httpContext.User.Identity?.Name ?? "platform-owner",
                        ct);

                    return Results.Accepted(accepted.PollUrl, accepted);
                }
                catch (CoturnPlatformMaintenanceException ex)
                {
                    return Results.Json(
                        new HostAgentErrorResponse(
                            Error: ex.Code,
                            Detail: ex.Message),
                        statusCode: ex.StatusCode);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "coturn_maintenance_invalid",
                        Detail: ex.Message));
                }
            });

        group.MapGet("/platform/coturn/maintenance/active",
            async (
                HttpContext httpContext,
                CoturnPlatformMaintenanceOperationService maintenanceService,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var active = await maintenanceService.FindActiveAsync(ct);
                    return Results.Ok(active);
                }
                catch (CoturnPlatformMaintenanceException ex)
                {
                    return Results.Json(
                        new HostAgentErrorResponse(
                            Error: ex.Code,
                            Detail: ex.Message),
                        statusCode: ex.StatusCode);
                }
            });

        group.MapGet("/platform/coturn/check/latest",
            async (
                HttpContext httpContext,
                CoturnRuntimeService coturn,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                var result = await coturn.GetLatestCheckAsync(ct);
                return Results.Ok(result);
            });

        group.MapPost("/platform/coturn/check",
            async (
                HttpContext httpContext,
                CoturnRuntimeService coturn,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var result = await coturn.CheckAsync(ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "coturn_check_unavailable",
                        Detail: ex.Message));
                }
            });

        group.MapGet("/platform/coturn/logs",
            async (
                HttpContext httpContext,
                int? tail,
                CoturnRuntimeService coturn,
                CancellationToken ct) =>
            {
                var authorizationResult =
                    HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

                if (authorizationResult is not null)
                {
                    return authorizationResult;
                }

                httpContext.Response.Headers.CacheControl = "no-store";

                try
                {
                    var result = await coturn.GetRecentLogsAsync(tail, ct);
                    return Results.Ok(result);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new HostAgentErrorResponse(
                        Error: "coturn_logs_unavailable",
                        Detail: ex.Message));
                }
            });
    }

    /// <summary>
    /// Restart &amp; Verify is a bounded availability operation over the existing
    /// MEM-owned Coturn runtime, so an ordinary current Platform Owner session
    /// is sufficient. Repair can recreate protected runtime state and therefore
    /// retains the configured recent-step-up requirement. Unknown/missing actions
    /// fail closed to the high-risk path before the maintenance service validates
    /// the canonical action.
    /// </summary>
    public static async Task<IResult?> ValidateMaintenanceAuthorizationAsync(
        HttpContext httpContext,
        CoturnPlatformMaintenanceRequest? request,
        IAuthorizationService authorizationService)
    {
        var sessionDenied =
            HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);

        if (sessionDenied is not null)
        {
            return sessionDenied;
        }

        var action = request?.Action?.Trim();
        if (string.Equals(
                action,
                CoturnPlatformMaintenanceActions.RestartVerify,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);
    }

    private static string ClassifyEnsureError(InvalidOperationException exception) =>
        exception.Message.Contains(
            "externalIp must be a valid IPv4 or IPv6 address literal",
            StringComparison.OrdinalIgnoreCase)
            ? "coturn_external_ip_invalid"
            : "invalid_coturn_request";
}
