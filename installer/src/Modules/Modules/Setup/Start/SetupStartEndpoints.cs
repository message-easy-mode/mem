using Carter;
using Modules.Setup.Lifecycle;

namespace Modules.Setup.Start;

public sealed class SetupStartEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/start")
            .WithTags("Setup Start");

        group.MapGet("/status", async (
            HttpContext httpContext,
            SetupStartService service,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var status = await service.GetAsync(cancellationToken);
            return Results.Ok(status);
        });

        group.MapPost("/begin", async (
            HttpContext httpContext,
            FirstTimeSetupAuthorityService service,
            CancellationToken cancellationToken) =>
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            var result = await service.BeginOrResumeAsync(cancellationToken);

            return result.Allowed
                ? Results.Ok(result.Installation)
                : Results.Json(
                    new { code = result.ReasonCode, message = result.Message },
                    statusCode: StatusCodes.Status409Conflict);
        });
    }
}