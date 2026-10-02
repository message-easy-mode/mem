using System.Security.Claims;
using HostAgent.Security;
using Microsoft.AspNetCore.Authorization;
using Modules.Auth.Identity;

namespace HostAgent.Runtime.Migrations.Staging.Retirement.Endpoints;

public sealed class MigrationStagingRetirementEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/migrations/sessions/{migrationId}/staging-runs/{stagingRunId}/retirement")
            .WithTags("Operator / Migrations / Staging retirement")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);
        group.MapGet("", async (HttpContext context, string migrationId, string stagingRunId,
            MigrationStagingRetirementService service, CancellationToken ct) =>
        {
            NoStore(context);
            try { return Results.Ok(await service.ReviewAsync(migrationId, stagingRunId, ct)); }
            catch (MigrationStagingRetirementException ex) { return Problem(ex); }
        });
        group.MapPost("", async (HttpContext context, string migrationId, string stagingRunId,
            MigrationStagingRetirementRequest request, MigrationStagingRetirementService service,
            IAuthorizationService authorization, CancellationToken ct) =>
        {
            NoStore(context);
            var sessionDenied = HostAgentEndpointOperatorGuard.ValidateCurrentControlPlaneSession(context);
            if (sessionDenied is not null) return sessionDenied;
            // Retirement always requires the existing recent-step-up grant. A global
            // optional high-risk setting must not weaken this destructive recovery contract.
            var stepUp = await authorization.AuthorizeAsync(context.User, null, MemOperatorPolicies.RecentStepUp);
            if (!stepUp.Succeeded)
                return Results.Json(new { error = "step_up_required", detail = "Fresh identity verification is required before this action." },
                    statusCode: StatusCodes.Status403Forbidden);
            if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) || actor == Guid.Empty)
                return Results.Forbid();
            try
            {
                var operation = await service.AcceptAsync(migrationId, stagingRunId, actor, request, ct);
                return Results.Json(operation, statusCode: operation.Status == "retired" ? 200 : 202);
            }
            catch (MigrationStagingRetirementException ex) { return Problem(ex); }
        });
    }
    private static void NoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }
    private static IResult Problem(MigrationStagingRetirementException ex) => Results.Json(
        new { error = ex.Code, detail = ex.Message }, statusCode: ex.Code == "staging-not-found" ? 404 : 409);
}
