using Carter;

namespace Modules.Setup.Secrets;

public sealed class NpmAdminCredentialEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/npm-administrator")
            .WithTags("Setup NPM Administrator");

        group.MapGet("/", async (
            NpmAdminCredentialService service,
            CancellationToken cancellationToken) =>
            Results.Json(await service.GetCurrentAsync(cancellationToken)));

        group.MapPost("/", async (
            NpmAdminCredentialRequest request,
            NpmAdminCredentialService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.StoreCurrentAsync(request, cancellationToken);
            return result.CredentialStored
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status400BadRequest);
        });
    }
}
