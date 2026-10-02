using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

/// <summary>
/// Rejects the temporary browser cookie unless its opaque server-side enrolment
/// grant remains in a claimed, unexpired, incomplete state. The browser cookie
/// alone is never trusted as authority to change an operator account.
/// </summary>
public sealed class MemOperatorEnrollmentCookieEvents(
    IMemOperatorEnrollmentService enrollment) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var grantId = context.Principal?.FindFirstValue(
            MemOperatorEnrollmentAuthentication.GrantIdClaimType);

        if (!Guid.TryParse(grantId, out var parsedGrantId) ||
            !await enrollment.IsGrantCurrentAsync(
                parsedGrantId,
                context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(MemOperatorEnrollmentAuthentication.Scheme);
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
