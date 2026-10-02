using Carter;

namespace Modules.Setup.Review;

public sealed class SetupReviewEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/review")
            .WithTags("Setup Review");

        group.MapGet("/", async (
            SetupReviewService service,
            CancellationToken cancellationToken) =>
            Results.Json(await service.GetCurrentAsync(cancellationToken)));

        group.MapPost("/accept", async (
            SetupReviewService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AcceptAsync(cancellationToken);
            return result.ReviewAccepted
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status409Conflict);
        });
    }
}
