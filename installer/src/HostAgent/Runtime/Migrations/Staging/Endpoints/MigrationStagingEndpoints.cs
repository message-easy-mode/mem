using Carter;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.Staging.Endpoints;

public sealed class MigrationStagingEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/migrations/sessions/{migrationId}/staging-runs")
            .WithTags("Operator / Migrations / Private Staging")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", ListAsync);
        group.MapPost("", StartAsync);
    }

    private static async Task<IResult> ListAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationStagingOrchestrator service,
        CancellationToken cancellationToken)
    {
        SetNoStore(httpContext);
        var runs = await service.ListAsync(migrationId, cancellationToken);
        return Results.Ok(runs);
    }

    private static async Task<IResult> StartAsync(
        HttpContext httpContext,
        string migrationId,
        [FromBody] StartMigrationStagingRequest? request,
        MigrationStagingOrchestrator service,
        IAuthorizationService authorizationService,
        CancellationToken cancellationToken)
    {
        SetNoStore(httpContext);

        var stepUpRequired = await HostAgentEndpointOperatorGuard
            .ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
                httpContext,
                authorizationService);

        if (stepUpRequired is not null)
        {
            return stepUpRequired;
        }

        try
        {
            var response = await service.StartAsync(
                migrationId,
                request ?? new StartMigrationStagingRequest(),
                cancellationToken);

            return Results.Ok(response);
        }
        catch (MigrationStagingException ex)
        {
            return ToProblem(ex, "migration_not_found");
        }
    }

    private static IResult ToProblem(
        MigrationStagingException exception,
        string notFoundCode)
    {
        var statusCode = string.Equals(
            exception.Code,
            notFoundCode,
            StringComparison.Ordinal)
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status409Conflict;

        return Results.Json(
            new
            {
                error = exception.Code,
                detail = exception.Message
            },
            statusCode: statusCode);
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
