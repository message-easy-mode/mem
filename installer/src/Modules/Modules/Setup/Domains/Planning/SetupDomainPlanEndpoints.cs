using Carter;

namespace Modules.Setup.Domains.Planning;

public sealed class SetupDomainPlanEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/domains/plan")
            .WithTags("Setup Domains");

        group.MapGet("/", async (
            SetupDomainPlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.GetCurrentAsync(cancellationToken);
            return Results.Json(result);
        });

        group.MapPost("/validate", async (
            SetupDomainPlanRequest request,
            SetupDomainPlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ValidateAndSaveAsync(request, cancellationToken);
            return Results.Json(result);
        });
    }
}
