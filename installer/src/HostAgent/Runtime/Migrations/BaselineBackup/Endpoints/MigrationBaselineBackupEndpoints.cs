using Carter;
using HostAgent.Commands;
using HostAgent.Security;
using HostAgent.Runtime.Migrations.Acceptance;
using Microsoft.AspNetCore.Authorization;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.BaselineBackup.Endpoints;

public sealed class MigrationBaselineBackupEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/operator/migrations/sessions/{migrationId}/baseline-backup")
            .WithTags("Operator / Migrations / Baseline Backup")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", StateAsync);
        group.MapPost("/retry", RetryAsync);
    }

    private static async Task<IResult> StateAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationBaselineBackupHandoffService service,
        CancellationToken ct)
    {
        SetNoStore(httpContext);
        return await ExecuteAsync(() => service.GetStateAsync(migrationId, ct));
    }

    private static async Task<IResult> RetryAsync(
        HttpContext httpContext,
        string migrationId,
        MigrationBaselineBackupHandoffService service,
        MigrationAcceptanceService acceptanceService,
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

        return await ExecuteAsync(async () =>
        {
            _ = await service.EnsureAndAttemptAsync(migrationId, ct);
            return await acceptanceService.GetStateAsync(migrationId, ct);
        });
    }

    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (FileNotFoundException ex)
        {
            return Results.NotFound(new HostAgentErrorResponse("migration_baseline_backup_not_found", ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Json(
                new HostAgentErrorResponse("migration_baseline_backup_blocked", ex.Message),
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
