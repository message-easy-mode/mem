using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

/// <summary>
/// Rejects a browser bootstrap cookie unless its opaque server-side grant is
/// still active. This ensures expiry, cancellation, completion, and a newly
/// created Platform Owner take effect without trusting the cookie alone.
/// </summary>
public sealed class MemBootstrapCookieEvents(
    IMemBootstrapGrantService bootstrapGrants) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var grantId = context.Principal?.FindFirstValue(
            MemBootstrapAuthentication.GrantIdClaimType);

        if (!Guid.TryParse(grantId, out var parsedGrantId) ||
            !await bootstrapGrants.IsGrantCurrentAsync(
                parsedGrantId,
                context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(MemBootstrapAuthentication.Scheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
