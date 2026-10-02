using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Identity;

namespace Modules.Operator.Npm;

/// <summary>
/// Platform Owner-only installed-state NPM credential management. Password
/// reveal and replacement are additionally protected by the existing
/// session-bound password-plus-TOTP step-up policy.
/// </summary>
public sealed class NpmCredentialManagementEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/operator/npm")
            .WithTags("Operator Nginx Proxy Manager")
            .RequireAuthorization(MemOperatorPolicies.ManagePlatform);

        group.MapGet("", async (
            HttpContext httpContext,
            [FromServices] NpmCredentialManagementService service,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);

            try
            {
                return Results.Ok(await service.GetAsync(ct));
            }
            catch (OperatorNpmCredentialException exception)
            {
                return ToProblem(exception);
            }
        });

        group.MapPost("/credential/reveal", async (
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            [FromServices] NpmCredentialManagementService service,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);

            var stepUpRequired = await RequireRecentStepUpAsync(
                httpContext,
                authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            try
            {
                var revealed = await service.RevealAsync(
                    CurrentOperatorId(httpContext.User),
                    httpContext.TraceIdentifier,
                    ct);

                // This is intentionally one-time response material for the
                // current browser render. It must never be cached.
                SetNoStore(httpContext);
                return Results.Ok(revealed);
            }
            catch (OperatorNpmCredentialException exception)
            {
                return ToProblem(exception);
            }
        });

        group.MapPut("/credential", async (
            [FromBody] UpdateOperatorNpmCredentialRequest request,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            [FromServices] NpmCredentialManagementService service,
            CancellationToken ct) =>
        {
            SetNoStore(httpContext);

            var stepUpRequired = await RequireRecentStepUpAsync(
                httpContext,
                authorizationService);
            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            try
            {
                var updated = await service.UpdateAsync(
                    request,
                    CurrentOperatorId(httpContext.User),
                    httpContext.TraceIdentifier,
                    ct);

                return Results.Ok(updated);
            }
            catch (OperatorNpmCredentialException exception)
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

    private static Guid? CurrentOperatorId(ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IResult ToProblem(OperatorNpmCredentialException exception)
    {
        var statusCode = exception.Kind switch
        {
            OperatorNpmCredentialFailureKind.Validation =>
                StatusCodes.Status422UnprocessableEntity,
            OperatorNpmCredentialFailureKind.CredentialRejected =>
                StatusCodes.Status422UnprocessableEntity,
            OperatorNpmCredentialFailureKind.VerificationUnavailable =>
                StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status409Conflict
        };

        return Results.Json(
            new
            {
                status = exception.Code,
                message = exception.Message
            },
            statusCode: statusCode);
    }

    private static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
