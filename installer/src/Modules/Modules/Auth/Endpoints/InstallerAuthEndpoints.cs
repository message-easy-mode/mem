using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Logging;
using Modules.Auth.Contracts;
using Modules.Auth.Services;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Endpoints;

public sealed class InstallerAuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/installer-auth")
            .WithTags("Installer Auth");

        group.MapPost("/unlock", async (
            UnlockInstallerRequest request,
            HttpContext httpContext,
            IInstallerTokenValidator tokenValidator,
            IMemBootstrapGrantService bootstrapGrants,
            ILogger<InstallerAuthEndpoints> logger,
            CancellationToken ct) =>
        {
            if (await bootstrapGrants.HasCompletedPlatformOwnerAsync(ct))
            {
                return Results.Conflict(new
                {
                    status = "operator_login_required"
                });
            }

            var valid = await tokenValidator.IsValidAsync(request.Token, ct);

            if (!valid)
            {
                logger.LogWarning(
                    "Control Plane setup unlock failed from {RemoteIpAddress}.",
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                return Results.Unauthorized();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "mem-installer-operator"),
                new(ClaimTypes.Name, "MEM Control Plane Setup"),
                new(InstallerAuthClaims.TransitionalInstallerUnlocked, InstallerAuthClaims.True)
            };

            var identity = new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme);

            var principal = new ClaimsPrincipal(identity);

            var authProperties = new AuthenticationProperties
            {
                IsPersistent = false,
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
                AllowRefresh = true
            };

            await httpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);

            logger.LogInformation(
                "Control Plane setup unlocked from {RemoteIpAddress}.",
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            return Results.Ok(new InstallerAuthSessionResponse(
                Authenticated: true,
                DisplayName: "MEM Control Plane Setup"));
        })
        .AllowAnonymous();

        group.MapGet("/session", (ClaimsPrincipal user) =>
        {
            var authenticated = user.Identity?.IsAuthenticated == true;

            return Results.Ok(new InstallerAuthSessionResponse(
                Authenticated: authenticated,
                DisplayName: authenticated ? user.Identity?.Name : null));
        })
        .AllowAnonymous();

        group.MapPost("/logout", async (HttpContext httpContext) =>
        {
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            return Results.Ok(new InstallerAuthSessionResponse(
                Authenticated: false,
                DisplayName: null));
        })
        .RequireAuthorization();
    }
}