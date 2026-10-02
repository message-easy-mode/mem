using System.Security.Claims;
using Carter;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Endpoints;

/// <summary>
/// Anonymous endpoints for a short-lived, code-scoped pending-operator
/// enrolment journey. They authenticate only the dedicated enrolment cookie;
/// that cookie is never accepted by normal control-plane routes.
/// </summary>
public sealed class MemOperatorEnrollmentEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/enrollment")
            .WithTags("MEM Operator Enrolment");

        group.MapGet("/state", async (
            HttpContext httpContext,
            [FromServices] IMemOperatorEnrollmentService enrollment,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, enrollment, ct);

            if (grantId is null)
            {
                return Results.Ok(new MemOperatorEnrollmentStateResponse(Active: false));
            }

            try
            {
                return Results.Ok(await enrollment.GetCurrentStateAsync(grantId.Value, ct));
            }
            catch (OperatorEnrollmentException)
            {
                await httpContext.SignOutAsync(MemOperatorEnrollmentAuthentication.Scheme);
                return Results.Ok(new MemOperatorEnrollmentStateResponse(Active: false));
            }
        })
        .AllowAnonymous();

        group.MapPost("/verify", async (
            [FromBody] VerifyMemOperatorEnrollmentCodeRequest request,
            HttpContext httpContext,
            [FromServices] IMemOperatorEnrollmentService enrollment,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            try
            {
                var begun = await enrollment.BeginAsync(request.EnrollmentCode, ct);

                var claims = new[]
                {
                    new Claim(
                        MemOperatorEnrollmentAuthentication.GrantIdClaimType,
                        begun.GrantId.ToString("D"))
                };

                var identity = new ClaimsIdentity(
                    claims,
                    MemOperatorEnrollmentAuthentication.Scheme);

                await httpContext.SignInAsync(
                    MemOperatorEnrollmentAuthentication.Scheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = false,
                        IssuedUtc = DateTimeOffset.UtcNow,
                        ExpiresUtc = begun.State.ExpiresAtUtc,
                        AllowRefresh = false
                    });

                return Results.Ok(begun.State);
            }
            catch (OperatorEnrollmentException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/prepare", async (
            [FromBody] PrepareMemOperatorEnrollmentRequest request,
            HttpContext httpContext,
            [FromServices] IMemOperatorEnrollmentService enrollment,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, enrollment, ct);

            if (grantId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var prepared = await enrollment.PrepareAsync(grantId.Value, request, ct);
                return Results.Ok(prepared);
            }
            catch (OperatorEnrollmentException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/totp", async (
            [FromBody] VerifyMemOperatorEnrollmentTotpRequest request,
            HttpContext httpContext,
            [FromServices] IMemOperatorEnrollmentService enrollment,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, enrollment, ct);

            if (grantId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                await enrollment.VerifyTotpAsync(grantId.Value, request.Code, ct);
                return Results.NoContent();
            }
            catch (OperatorEnrollmentException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/complete", async (
            HttpContext httpContext,
            [FromServices] IMemOperatorEnrollmentService enrollment,
            [FromServices] UserManager<MemOperator> userManager,
            [FromServices] SignInManager<MemOperator> signInManager,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, enrollment, ct);

            if (grantId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var completed = await enrollment.CompleteAsync(grantId.Value, ct);
                var subject = await userManager.FindByNameAsync(completed.Username);

                if (subject is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Operator enrolment completion was unavailable.");
                }

                await signInManager.SignInAsync(subject, isPersistent: false);
                await httpContext.SignOutAsync(MemOperatorEnrollmentAuthentication.Scheme);
                await httpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

                return Results.Ok(completed);
            }
            catch (OperatorEnrollmentException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/cancel", async (
            HttpContext httpContext,
            [FromServices] IMemOperatorEnrollmentService enrollment,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, enrollment, ct);

            if (grantId is not null)
            {
                try
                {
                    await enrollment.CancelAsync(grantId.Value, ct);
                }
                catch (OperatorEnrollmentException exception)
                {
                    return ToProblem(exception);
                }
            }

            await httpContext.SignOutAsync(MemOperatorEnrollmentAuthentication.Scheme);
            return Results.NoContent();
        })
        .AllowAnonymous();
    }

    private static async Task<Guid?> GetCurrentGrantIdAsync(
        HttpContext httpContext,
        IMemOperatorEnrollmentService enrollment,
        CancellationToken ct)
    {
        var result = await httpContext.AuthenticateAsync(MemOperatorEnrollmentAuthentication.Scheme);

        if (!result.Succeeded)
        {
            return null;
        }

        var grantId = result.Principal?.FindFirstValue(
            MemOperatorEnrollmentAuthentication.GrantIdClaimType);

        return Guid.TryParse(grantId, out var parsedGrantId) &&
            await enrollment.IsGrantCurrentAsync(parsedGrantId, ct)
                ? parsedGrantId
                : null;
    }

    private static void SetSensitiveNoStoreHeaders(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    private static IResult ToProblem(OperatorEnrollmentException exception)
    {
        var statusCode = exception.Kind switch
        {
            OperatorEnrollmentFailureKind.Validation =>
                StatusCodes.Status422UnprocessableEntity,
            OperatorEnrollmentFailureKind.NotFound =>
                StatusCodes.Status404NotFound,
            _ => StatusCodes.Status409Conflict
        };

        return Results.Json(
            new
            {
                status = exception.Code
            },
            statusCode: statusCode);
    }
}
