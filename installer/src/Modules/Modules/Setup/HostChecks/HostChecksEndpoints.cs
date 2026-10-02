using Carter;

namespace Modules.Setup.HostChecks;

public sealed class HostChecksEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/host-checks")
            .WithTags("Setup Host Checks");

        group.MapPost("/runs", async (
            HostChecksService service,
            CancellationToken cancellationToken) =>
        {
            var run = await service.RunAsync(cancellationToken);
            return Results.Json(run);
        });

        group.MapGet("/runs/current", (HostChecksService service) =>
        {
            var run = service.GetLatest();

            return run is null
                ? Results.Text("null", "application/json")
                : Results.Json(run);
        });

        group.MapGet("/runs/{runId}", (string runId, HostChecksService service) =>
        {
            var run = service.GetById(runId);

            return run is null
                ? Results.Json(
                    new { message = $"Host check run '{runId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(run);
        });
    }
}