using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Modules.Integrations.Npm.Services;

namespace Modules.Setup.Domains.Ingress;

public sealed class IngressEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup/domains/ingress")
            .WithTags("Setup Domains");

        group.MapGet("/npm/status", async (
            NpmReadinessService service,
            CancellationToken cancellationToken) =>
        {
            return Results.Json(
                await service.GetReadinessAsync(cancellationToken));
        });
    }
}