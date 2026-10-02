using Carter;
using Modules.Integrations.Postgres.Contracts;
using Modules.Integrations.Postgres.Services;

namespace Modules.Integrations.Postgres.Endpoints;

public sealed class PostgresEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/services/postgres")
            .WithTags("Postgres");

        group.MapPost("/plan", (
            PostgresPlanRequest request,
            PostgresRuntimeService service) =>
        {
            return Results.Ok(service.Plan(request));
        });

        group.MapPost("/deploy", async (
            PostgresDeployRequest request,
            PostgresRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.DeployAsync(request, ct));
        });

        group.MapPost("/start", async (
            PostgresRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.StartAsync(ct));
        });

        group.MapPost("/stop", async (
            PostgresRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.StopAsync(ct));
        });

        group.MapPost("/remove", async (
            PostgresRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.RemoveAsync(ct));
        });

        group.MapGet("/", async (
            PostgresRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.GetStatusAsync(ct));
        });
    }
}