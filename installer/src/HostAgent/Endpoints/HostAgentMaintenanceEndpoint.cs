using Carter;
using HostAgent.Commands;
using HostAgent.Runtime.Maintenance;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HostAgent.Endpoints;

public sealed class HostAgentMaintenanceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/internal/host-agent/admin/maintenance/runtime-reconciliation",
                async (
                    HttpContext httpContext,
                    RuntimeReconciliationReportService service,
                    CancellationToken ct) =>
                {
                    if (!HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(httpContext.User))
                    {
                        return Results.Json(
                            new
                            {
                                source = "control-plane",
                                status = "unauthorized",
                                detail = "A valid installer unlock session is required."
                            },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }

                    return Results.Ok(await service.BuildAsync(ct));
                })
            .WithName("GetRuntimeReconciliationReport")
            .WithTags("HostAgent", "Maintenance");

        app.MapPost(
                "/internal/host-agent/admin/maintenance/runtime-reconciliation/npm-proxy-hosts/delete",
                async (
                    [FromBody] RuntimeReconciliationNpmProxyHostCleanupRequest? request,
                    HttpContext httpContext,
                    CancellationToken ct) =>
                {
                    var authorization = httpContext.RequestServices
                        .GetRequiredService<IAuthorizationService>();

                    var denied = await HostAgentEndpointOperatorGuard
                        .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                            httpContext,
                            authorization);

                    if (denied is not null)
                    {
                        return denied;
                    }

                    var cleanup = httpContext.RequestServices
                        .GetRequiredService<RuntimeReconciliationCleanupService>();

                    var response = await cleanup.DeleteSelectedOrphanedNpmProxyHostsAsync(
                        request ?? new RuntimeReconciliationNpmProxyHostCleanupRequest([], null),
                        ct);

                    return response.Status switch
                    {
                        "confirmation_required" => Results.BadRequest(new HostAgentErrorResponse(
                            "runtime_reconciliation_confirmation_required",
                            response.Detail ?? "Explicit confirmation is required.")),
                        "no_selection" => Results.BadRequest(new HostAgentErrorResponse(
                            "runtime_reconciliation_no_selection",
                            response.Detail ?? "Select at least one orphaned NPM proxy host.")),
                        _ => Results.Ok(response)
                    };
                })
            .WithName("DeleteRuntimeReconciliationOrphanedNpmProxyHosts")
            .WithTags("HostAgent", "Maintenance");
    }
}
