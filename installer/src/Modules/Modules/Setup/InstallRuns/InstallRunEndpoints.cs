using Carter;
using Modules.Setup.InstallPlans;
using Modules.Setup.Verification;

namespace Modules.Setup.InstallRuns;

public sealed class InstallRunEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/install-runs")
            .WithTags("Setup Install Run");

        group.MapGet("/{installationId:guid}/steps", async (
            Guid installationId,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var steps = await service.GetStepsAsync(
                installationId,
                cancellationToken);

            return Results.Json(steps);
        });

        group.MapPost("/{installationId:guid}/run", async (
            Guid installationId,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RunAsync(
                installationId,
                cancellationToken);

            if (result is null)
            {
                return Results.Json(
                    new { message = $"Install plan '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            return result.Accepted
                ? Results.Json(result)
                : Results.Json(result, statusCode: StatusCodes.Status409Conflict);
        });

        group.MapGet("/{installationId:guid}/verification-report", async (
            Guid installationId,
            VerificationReportService service,
            CancellationToken cancellationToken) =>
        {
            var report = await service.GetReportAsync(
                installationId,
                cancellationToken);

            return Results.Json(report);
        });

        group.MapPost("/{installationId:guid}/handoff/complete", async (
            Guid installationId,
            SetupHandoffService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CompleteAsync(
                installationId,
                cancellationToken);

            if (result is null)
            {
                return Results.Json(
                    new { message = $"Install run '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound);
            }

            return result.Completed
                ? Results.Json(result)
                : Results.Json(
                    result,
                    statusCode: StatusCodes.Status409Conflict);
        });

        group.MapGet("/{installationId:guid}/handoff", async (
            Guid installationId,
            SetupHandoffService service,
            CancellationToken cancellationToken) =>
        {
            var handoff = await service.GetAsync(
                installationId,
                cancellationToken);

            return handoff is null
                ? Results.Json(
                    new { message = $"Install run '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(handoff);
        });
    }
}