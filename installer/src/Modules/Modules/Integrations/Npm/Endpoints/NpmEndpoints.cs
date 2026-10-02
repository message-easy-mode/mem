using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace Modules.Integrations.Npm.Endpoints;

public sealed class NpmEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/services/npm")
            .WithTags("NPM");

        group.MapGet("/status", async (
            NpmReadinessService service,
            CancellationToken ct) =>
        {
            return Results.Json(await service.GetReadinessAsync(ct));
        });

        group.MapPost("/plan", (
            NpmPlanRequest request,
            NpmRuntimeService service) =>
        {
            return Results.Json(service.Plan(request));
        });


        group.MapPost("/deploy", async (
            NpmDeployRequest request,
            NpmRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Json(await service.DeployAsync(request, ct));
        });

        group.MapPost("/stop", async (
            NpmRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Json(await service.StopAsync(ct));
        });

        group.MapPost("/remove", async (
            NpmRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Json(await service.RemoveAsync(ct));
        });

        group.MapGet("/", async (
            NpmRuntimeService service,
            CancellationToken ct) =>
        {
            return Results.Json(await service.GetStatusAsync(ct));
        });

        group.MapPost("/proxy-hosts", async (
            CreateNpmProxyHostRequest request,
            NpmProxyHostService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.EnsureProxyHostAsync(request, ct);
                return Results.Json(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    new { error = ex.Message, statusCode = ex.StatusCode?.ToString() },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapGet("/proxy-hosts", async (
            string domain,
            NpmProxyHostService service,
            CancellationToken ct) =>
        {
            try
            {
                var result = await service.GetByDomainAsync(domain, ct);
                return result is null
                    ? Results.NotFound()
                    : Results.Json(result);
            }
            catch (ArgumentException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    new { error = ex.Message, statusCode = ex.StatusCode?.ToString() },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        group.MapDelete("/proxy-hosts", async (
            string domain,
            NpmProxyHostService service,
            CancellationToken ct) =>
        {
            try
            {
                await service.DeleteByDomainIfExistsAsync(domain, ct);
                return Results.Json(new { ok = true });
            }
            catch (ArgumentException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(
                    new { error = ex.Message },
                    statusCode: StatusCodes.Status400BadRequest);
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(
                    new { error = ex.Message, statusCode = ex.StatusCode?.ToString() },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }
}