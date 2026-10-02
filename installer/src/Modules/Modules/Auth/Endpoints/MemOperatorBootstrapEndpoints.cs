using System.Security.Claims;
using Carter;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Endpoints;

/// <summary>
/// Anonymous surface for the tightly scoped first-owner bootstrap only.
/// Bootstrap cookie authority is checked explicitly for every mutating
/// follow-up action and is never accepted by normal operator endpoints.
/// </summary>
public sealed class MemOperatorBootstrapEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/bootstrap")
            .WithTags("MEM Bootstrap");

        group.MapGet("/state", async (
            HttpContext httpContext,
            IMemBootstrapGrantService bootstrapGrants,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);
            return Results.Ok(await bootstrapGrants.GetStateAsync(ct));
        })
        .AllowAnonymous();

        group.MapPost("/verify", async (
            VerifyBootstrapCodeRequest request,
            HttpContext httpContext,
            IInstallerTokenValidator tokenValidator,
            IMemBootstrapGrantService bootstrapGrants,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var valid = await tokenValidator.IsValidAsync(request.Token, ct);

            if (!valid)
            {
                return Results.Unauthorized();
            }

            try
            {
                var grant = await bootstrapGrants.IssueGrantAsync(ct);

                var claims = new[]
                {
                    new Claim(
                        MemBootstrapAuthentication.GrantIdClaimType,
                        grant.Id.ToString("D"))
                };

                var identity = new ClaimsIdentity(
                    claims,
                    MemBootstrapAuthentication.Scheme);

                await httpContext.SignInAsync(
                    MemBootstrapAuthentication.Scheme,
                    new ClaimsPrincipal(identity),
                    new AuthenticationProperties
                    {
                        IsPersistent = false,
                        IssuedUtc = DateTimeOffset.UtcNow,
                        ExpiresUtc = grant.ExpiresAtUtc,
                        AllowRefresh = false
                    });

                return Results.Ok(new BootstrapGrantResponse(grant.ExpiresAtUtc));
            }
            catch (BootstrapFlowException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/first-owner", async (
            CreateFirstMemOperatorRequest request,
            HttpContext httpContext,
            IMemBootstrapGrantService bootstrapGrants,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, bootstrapGrants, ct);

            if (grantId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                return Results.Ok(await bootstrapGrants.PrepareFirstOwnerAsync(
                    grantId.Value,
                    request,
                    ct));
            }
            catch (BootstrapFlowException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/totp", async (
            VerifyBootstrapTotpRequest request,
            HttpContext httpContext,
            IMemBootstrapGrantService bootstrapGrants,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, bootstrapGrants, ct);

            if (grantId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                await bootstrapGrants.VerifyTotpAsync(grantId.Value, request.Code, ct);
                return Results.NoContent();
            }
            catch (BootstrapFlowException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/complete", async (
            HttpContext httpContext,
            IMemBootstrapGrantService bootstrapGrants,
            UserManager<MemOperator> userManager,
            SignInManager<MemOperator> signInManager,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, bootstrapGrants, ct);

            if (grantId is null)
            {
                return Results.Unauthorized();
            }

            try
            {
                var completed = await bootstrapGrants.CompleteAsync(grantId.Value, ct);
                var user = await userManager.FindByNameAsync(completed.Username);

                if (user is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError,
                        title: "Bootstrap completion was unavailable.");
                }

                await signInManager.SignInAsync(user, isPersistent: false);
                await httpContext.SignOutAsync(MemBootstrapAuthentication.Scheme);
                await httpContext.SignOutAsync(
                    Microsoft.AspNetCore.Authentication.Cookies
                        .CookieAuthenticationDefaults.AuthenticationScheme);

                return Results.Ok(completed);
            }
            catch (BootstrapFlowException exception)
            {
                return ToProblem(exception);
            }
        })
        .AllowAnonymous();

        group.MapPost("/cancel", async (
            HttpContext httpContext,
            IMemBootstrapGrantService bootstrapGrants,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var grantId = await GetCurrentGrantIdAsync(httpContext, bootstrapGrants, ct);

            if (grantId is not null)
            {
                try
                {
                    await bootstrapGrants.CancelAsync(grantId.Value, ct);
                }
                catch (BootstrapFlowException exception)
                {
                    return ToProblem(exception);
                }
            }

            await httpContext.SignOutAsync(MemBootstrapAuthentication.Scheme);
            return Results.NoContent();
        })
        .AllowAnonymous();
    }

    private static void SetSensitiveNoStoreHeaders(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    private static async Task<Guid?> GetCurrentGrantIdAsync(
        HttpContext httpContext,
        IMemBootstrapGrantService bootstrapGrants,
        CancellationToken ct)
    {
        var result = await httpContext.AuthenticateAsync(MemBootstrapAuthentication.Scheme);

        if (!result.Succeeded)
        {
            return null;
        }

        var grantId = result.Principal?.FindFirstValue(
            MemBootstrapAuthentication.GrantIdClaimType);

        return Guid.TryParse(grantId, out var parsedGrantId) &&
            await bootstrapGrants.IsGrantCurrentAsync(parsedGrantId, ct)
                ? parsedGrantId
                : null;
    }

    private static IResult ToProblem(BootstrapFlowException exception)
    {
        var statusCode = exception.Kind switch
        {
            BootstrapFlowExceptionKind.Validation => StatusCodes.Status422UnprocessableEntity,
            BootstrapFlowExceptionKind.Unavailable => StatusCodes.Status409Conflict,
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
