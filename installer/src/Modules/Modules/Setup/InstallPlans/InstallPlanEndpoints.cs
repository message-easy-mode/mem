using Carter;
using Modules.Setup.Lifecycle;

namespace Modules.Setup.InstallPlans;

public sealed class InstallPlanEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/setup/install-plans")
            .WithTags("Setup Install Plan");

        group.MapPost("/", async (
            FirstTimeSetupAuthorityService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.BeginOrResumeAsync(cancellationToken);

            return result.Allowed
                ? Results.Ok(result.Installation)
                : Results.Json(
                    new { code = result.ReasonCode, message = result.Message },
                    statusCode: StatusCodes.Status409Conflict);
        });

        group.MapGet("/current", async (
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var installation = await service.GetCurrentAsync(cancellationToken);

            return installation is null
                ? Results.Text("null", "application/json")
                : Results.Json(installation);
        });

        group.MapGet("/{installationId:guid}", async (
            Guid installationId,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var installation = await service.GetAsync(installationId, cancellationToken);

            return installation is null
                ? Results.Json(
                    new { message = $"Installation '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(installation);
        });

        group.MapDelete("/{installationId:guid}", async (
            Guid installationId,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.RemoveAsync(installationId, cancellationToken);
            return Results.Json(result);
        });

        group.MapPut("/{installationId:guid}/config/general", async (
            Guid installationId,
            UpdateGeneralConfigRequest request,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var installation = await service.UpdateGeneralAsync(
                installationId,
                request,
                cancellationToken);

            return installation is null
                ? Results.Json(
                    new { message = $"Installation '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(installation);
        });

        group.MapPut("/{installationId:guid}/config/platform", async (
            Guid installationId,
            PlatformSetupConfig request,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var installation = await service.UpdatePlatformAsync(
                installationId,
                request,
                cancellationToken);

            return installation is null
                ? Results.Json(
                    new { message = $"Installation '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(installation);
        });

        group.MapPut("/{installationId:guid}/config/support-tools", async (
            Guid installationId,
            SupportToolsSetupConfig request,
            InstallPlanService service,
            CancellationToken cancellationToken) =>
        {
            var installation = await service.UpdateSupportToolsAsync(
                installationId,
                request,
                cancellationToken);

            return installation is null
                ? Results.Json(
                    new { message = $"Installation '{installationId}' was not found." },
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(installation);
        });
    }
}