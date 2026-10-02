using Carter;
using Modules.Integrations.PgAdmin.Contracts;
using Modules.Integrations.PgAdmin.Services;

namespace Modules.Integrations.PgAdmin.Endpoints;

public sealed class PgAdminEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/services/pgadmin")
            .WithTags("PgAdmin");

        group.MapPost("/plan", (
            PgAdminPlanRequest request,
            PgAdminRuntimeService service) =>
        {
            return Results.Ok(service.Plan(request));
        });

        group.MapPost("/start", async (
            PgAdminRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.StartAsync(ct));
        });

        group.MapPost("/deploy", async (
            PgAdminDeployRequest request,
            PgAdminRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.DeployAsync(request, ct));
        });

        group.MapPost("/stop", async (
            PgAdminRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.StopAsync(ct));
        });

        group.MapPost("/remove", async (
            PgAdminRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.RemoveAsync(ct));
        });

        group.MapGet("/", async (
            PgAdminRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Ok(await service.GetStatusAsync(ct));
        });
    }
}