using Carter;
using Modules.Auth.Identity;

namespace Modules.Operator.Migrations.Conversion;

public sealed class MigrationConversionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/migrations/sessions/{migrationId}/conversion-attempts")
            .WithTags("Migration conversion")
            .RequireAuthorization(MemOperatorPolicies.MigrationIntakeOperate);

        group.MapGet("", async (
            string migrationId,
            HttpContext httpContext,
            MigrationConversionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            return Results.Ok(await orchestrator.ListAsync(migrationId, cancellationToken));
        });

        group.MapGet("options", async (
            string migrationId,
            HttpContext httpContext,
            MigrationConversionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            try
            {
                return Results.Ok(await orchestrator.GetOptionsAsync(migrationId, cancellationToken));
            }
            catch (MigrationConversionException exception)
            {
                return ToProblem(exception);
            }
        });

        group.MapPost("", async (
            string migrationId,
            StartMigrationConversionRequest request,
            HttpContext httpContext,
            MigrationConversionOrchestrator orchestrator,
            CancellationToken cancellationToken) =>
        {
            SetNoStore(httpContext);
            try
            {
                return Results.Ok(await orchestrator.StartAsync(
                    migrationId,
                    request.RetryOfConversionAttemptId,
                    cancellationToken));
            }
            catch (MigrationConversionException exception)
            {
                return ToProblem(exception);
            }
        });
    }

    private static IResult ToProblem(MigrationConversionException exception) =>
        exception.Code == "migration_not_found"
            ? Results.NotFound(new MigrationConversionProblemResponse(exception.Code, exception.Message))
            : Results.Conflict(new MigrationConversionProblemResponse(exception.Code, exception.Message));

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
