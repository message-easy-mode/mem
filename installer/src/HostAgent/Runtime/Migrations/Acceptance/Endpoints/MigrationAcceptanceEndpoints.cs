using Carter;
using HostAgent.Commands;
using HostAgent.Security;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.Acceptance.Endpoints;

public sealed class MigrationAcceptanceEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/migrations/sessions/{migrationId}/acceptance")
            .WithTags("Operator / Migrations / Acceptance")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", StateAsync);
        group.MapPost("", AcceptAsync);
        group.MapPost("finish", FinishAsync);
        group.MapGet("report", ReportAsync);
    }

    private static async Task<IResult> StateAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationAcceptanceService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.GetStateAsync(migrationId, ct));
    }

    private static async Task<IResult> AcceptAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] AcceptMigrationRequest? request,
        MigrationAcceptanceService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        var stepUp = await HostAgentEndpointOperatorGuard.ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
            httpContext,
            authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_acceptance_body_required",
                "Provide the retention period and every required acceptance acknowledgement."));
        }

        return await ExecuteAsync(() => service.AcceptAsync(
            migrationId,
            httpContext.User.Identity?.Name ?? "operator",
            request,
            ct));
    }


    private static async Task<IResult> FinishAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] FinishMigrationRequest? request,
        MigrationFinishService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        var stepUp = await HostAgentEndpointOperatorGuard.ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
            httpContext,
            authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        if (request is null)
        {
            return Results.BadRequest(new HostAgentErrorResponse(
                "migration_finish_body_required",
                "Choose the legacy retention period and confirm the verified-server, recovery-boundary, and old-server retention consequences."));
        }

        return await ExecuteAsync(() => service.FinishAsync(
            migrationId,
            httpContext.User.Identity?.Name ?? "operator",
            request,
            ct));
    }

    private static async Task<IResult> ReportAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationAcceptanceService service,
        IAuthorizationService authorizationService,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        var stepUp = await HostAgentEndpointOperatorGuard.ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
            httpContext,
            authorizationService);
        if (stepUp is not null)
        {
            return stepUp;
        }

        try
        {
            var report = await service.GetCompletionReportAsync(migrationId, ct);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                report,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
            return Results.File(
                bytes,
                "application/json",
                $"{report.Payload.ReportId}.mem-migration-completion.json");
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse("migration_acceptance_not_found", ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_completion_report_blocked", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse("migration_acceptance_not_found", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_acceptance_blocked", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
