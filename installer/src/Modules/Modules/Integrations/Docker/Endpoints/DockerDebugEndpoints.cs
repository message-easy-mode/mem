using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Integrations.Docker.Contracts;
using Modules.Integrations.Docker.Services;

namespace Modules.Integrations.Docker.Endpoints;

public sealed class DockerDebugEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/services/docker")
            .WithTags("Docker Debug");

        group.MapGet("/ping", async (
                DockerDebugService service,
                CancellationToken ct) =>
            {
                var result = await service.PingAsync(ct);
                return Results.Ok(result);
            })
            .WithName("Docker_Ping")
            .Produces<DockerPingResponse>(StatusCodes.Status200OK)
            .WithSummary("Ping Docker through Docker.DotNet");

        group.MapGet("/containers", async (
                DockerDebugService service,
                CancellationToken ct) =>
            {
                var result = await service.ListContainersAsync(ct);
                return Results.Ok(result);
            })
            .WithName("Docker_ListContainers")
            .Produces<DockerContainersResponse>(StatusCodes.Status200OK)
            .WithSummary("List all containers visible to Docker");

        group.MapGet("/containers/prefix/{prefix}", async (
                string prefix,
                DockerDebugService service,
                CancellationToken ct) =>
            {
                var result = await service.ListByPrefixAsync(prefix, ct);
                return Results.Ok(result);
            })
            .WithName("Docker_ListContainersByPrefix")
            .Produces<DockerContainersResponse>(StatusCodes.Status200OK)
            .WithSummary("List containers whose names start with the given prefix");

        group.MapGet("/containers/name/{name}", async (
                string name,
                DockerDebugService service,
                CancellationToken ct) =>
            {
                var result = await service.InspectByNameAsync(name, ct);
                return result.Container is null ? Results.NotFound() : Results.Ok(result);
            })
            .WithName("Docker_InspectContainerByName")
            .Produces<DockerInspectResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .WithSummary("Inspect a single container by exact name");
    }
}