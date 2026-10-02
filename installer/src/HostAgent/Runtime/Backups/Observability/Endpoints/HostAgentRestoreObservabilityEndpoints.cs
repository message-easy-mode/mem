using Carter;
using HostAgent.Commands;
using HostAgent.Security;

namespace HostAgent.Runtime.Backups.Observability.Endpoints;

public sealed class HostAgentRestoreObservabilityEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/internal/host-agent")
            .WithTags("Host Agent / Backups / Restore Observability");

        group.MapGet(
                "/backups/restores/{restoreSessionId}/logs",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    int? page,
                    int? pageSize,
                    string? severity,
                    string? stage,
                    string? search,
                    RestoreStructuredLogService logs,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await logs.ListAsync(
                            restoreSessionId,
                            new RestoreLogQuery(
                                Page: page ?? 1,
                                PageSize: pageSize ?? 100,
                                Severity: severity,
                                Stage: stage,
                                Search: search),
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
                            Error: "invalid_restore_log_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<RestoreLogPage>();

        group.MapPost(
                "/backups/restores/{restoreSessionId}/support-report",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    RestoreSupportReportService supportReports,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await supportReports.GenerateAsync(restoreSessionId, ct);
                        return result is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                Error: "restore_attempt_not_found",
                                Detail: $"Restore attempt '{restoreSessionId}' was not found."))
                            : Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_restore_support_report_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<RestoreSupportReportGenerationResult>();

        group.MapGet(
                "/backups/restores/{restoreSessionId}/support-report",
                async (
                    HttpContext httpContext,
                    string restoreSessionId,
                    RestoreSupportReportService supportReports,
                    CancellationToken ct) =>
                {
                    var authResult = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(httpContext);
                    if (authResult is not null)
                    {
                        return authResult;
                    }

                    try
                    {
                        var result = await supportReports.GetAsync(restoreSessionId, ct);
                        return result is null
                            ? Results.NotFound(new HostAgentErrorResponse(
                                Error: "restore_support_report_not_found",
                                Detail: $"No generated support report exists for restore '{restoreSessionId}'."))
                            : Results.Ok(result);
                    }
                    catch (InvalidOperationException ex)
                    {
                        return Results.BadRequest(new HostAgentErrorResponse(
                            Error: "invalid_restore_support_report_request",
                            Detail: ex.Message));
                    }
                })
            .Produces<RestoreSupportReport>();
    }
}
