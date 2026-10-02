using Carter;

namespace Modules.Setup.Verification;

public sealed class VerificationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/verification")
            .WithTags("Setup Verification");

        group.MapGet("/report", async (
            Guid? installationId,
            VerificationReportService service,
            CancellationToken cancellationToken) =>
        {
            var report = await service.GetReportAsync(
                installationId,
                cancellationToken);

            return Results.Json(report);
        });
    }
}