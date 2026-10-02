using Carter;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Endpoints;

/// <summary>
/// Platform Owner-controlled security settings. The browser displays and
/// requests policy changes, but high-risk enforcement reads the persisted
/// server state through IMemSecuritySettingsService.
/// </summary>
public sealed class MemSecuritySettingsEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/security/settings")
            .WithTags("MEM Security Settings")
            .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapGet("", async (
            HttpContext httpContext,
            [FromServices] IMemSecuritySettingsService settings,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);

            var snapshot = await settings.GetEffectiveAsync(ct);
            return Results.Ok(ToResponse(snapshot));
        });

        group.MapPatch("/high-risk-step-up", async (
            [FromBody] UpdateMemHighRiskStepUpSettingsRequest request,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);

            // Policy changes are always protected by current password plus
            // current authenticator-app TOTP, even when the stored policy says
            // high-risk actions do not require step-up.
            var stepUpRequired = await RequireRecentStepUpAsync(httpContext, authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            if (request.Required is null || request.ReuseVerificationMinutes is null)
            {
                return Results.Json(
                    new { status = "security_settings_validation_failed" },
                    statusCode: StatusCodes.Status422UnprocessableEntity);
            }

            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var settings = httpContext.RequestServices
                .GetRequiredService<IMemSecuritySettingsService>();

            var currentOperator = await userManager.GetUserAsync(httpContext.User);
            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var snapshot = await settings.UpdateHighRiskStepUpAsync(
                    currentOperator.Id,
                    new UpdateMemSecuritySettingsCommand(
                        request.Required.Value,
                        request.ReuseVerificationMinutes.Value),
                    httpContext.TraceIdentifier,
                    ct);

                return Results.Ok(ToResponse(snapshot));
            }
            catch (MemSecuritySettingsException exception)
            {
                return ToProblem(exception);
            }
        });
    }

    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService)
    {
        var result = await authorizationService.AuthorizeAsync(
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

    private static MemSecuritySettingsResponse ToResponse(
        MemSecuritySettingsSnapshot snapshot) =>
        new(new MemHighRiskStepUpSettingsResponse(
            Required: snapshot.RequireHighRiskStepUp,
            ReuseVerificationMinutes: snapshot.HighRiskStepUpGrantMinutes,
            AllowedReuseVerificationMinutes: MemSecuritySettingsDefaults.AllowedHighRiskStepUpGrantMinutes,
            IsDefaulted: snapshot.IsDefaulted,
            UpdatedAtUtc: snapshot.UpdatedAtUtc,
            UpdatedByOperatorId: snapshot.UpdatedByOperatorId));

    private static IResult ToProblem(MemSecuritySettingsException exception)
    {
        var statusCode = exception.Kind switch
        {
            MemSecuritySettingsFailureKind.Validation =>
                StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status503ServiceUnavailable
        };

        return Results.Json(
            new { status = exception.Code },
            statusCode: statusCode);
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
