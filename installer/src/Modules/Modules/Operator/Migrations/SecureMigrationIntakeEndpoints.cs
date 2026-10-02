using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace Modules.Operator.Migrations;

public sealed class SecureMigrationIntakeEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/migrations/secure-intakes")
            .WithTags("Secure migration intakes")
            .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapPost("", async (
            SecureMigrationIntakeCreateRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            SecureMigrationIntakeService service,
            CancellationToken ct) =>
        {
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null)
            {
                return stepUp;
            }

            try
            {
                var intake = await service.CreateAsync(request, ct);
                SetNoStore(httpContext);
                return Results.Ok(intake);
            }
            catch (SecureMigrationIntakeException exception)
            {
                SetNoStore(httpContext);
                return Results.Json(
                    new SecureMigrationIntakeProblemResponse(exception.Code, exception.Message),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        group.MapGet("/{intakeId}", async (
            string intakeId,
            HttpContext httpContext,
            SecureMigrationIntakeService service,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);
            var intake = await service.GetAsync(intakeId, ct);
            return intake is null ? Results.NotFound() : Results.Ok(intake);
        });


        group.MapPost("/{intakeId}/package", async (
            string intakeId,
            HttpRequest request,
            HttpContext httpContext,
            IAuthorizationService authorization,
            SecureMigrationIntakeService service,
            CancellationToken ct) =>
        {
            var stepUp = await RequireRecentStepUpAsync(httpContext, authorization);
            if (stepUp is not null) return stepUp;
            if (!request.HasFormContentType)
                return Results.BadRequest(new SecureMigrationIntakeProblemResponse("secure_migration_package_required", "Choose an encrypted migration package."));
            var form = await request.ReadFormAsync(ct);
            var package = form.Files.GetFile("package");
            if (package is null)
                return Results.BadRequest(new SecureMigrationIntakeProblemResponse("secure_migration_package_required", "Choose an encrypted migration package."));
            try
            {
                var intake = await service.UploadAndValidateAsync(intakeId, package, ct);
                SetNoStore(httpContext);
                return Results.Ok(intake);
            }
            catch (SecureMigrationIntakeException exception)
            {
                SetNoStore(httpContext);
                return Results.Json(new SecureMigrationIntakeProblemResponse(exception.Code, exception.Message), statusCode: StatusCodes.Status422UnprocessableEntity);
            }
        })
        .DisableAntiforgery()
        .WithMetadata(
            new RequestSizeLimitAttribute(5L * 1024 * 1024 * 1024),
            new RequestFormLimitsAttribute
            {
                MultipartBodyLengthLimit = 5L * 1024 * 1024 * 1024,
            });

    }

    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorization)
    {
        var result = await authorization.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        if (result.Succeeded)
        {
            return null;
        }

        SetNoStore(httpContext);
        return Results.Json(
            new { status = "step_up_required" },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
