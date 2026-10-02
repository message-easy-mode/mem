using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Modules.Auth;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

/// <summary>
/// Applies Identity security-stamp validation and MEM's account-enabled check
/// to the future named-operator cookie. No normal user is issued this cookie
/// until SEC-AUTH-03/04; registering it now freezes the safe session contract.
/// </summary>
public sealed class MemOperatorCookieEvents(
    ISecurityStampValidator securityStampValidator,
    IMemOperatorSessionValidator sessionValidator) : CookieAuthenticationEvents
{
    public override Task SigningIn(CookieSigningInContext context)
    {
        if (context.Principal is not null)
        {
            var sessionId = MemOperatorSessionClaims.EnsureSessionId(
                context.Principal,
                context.Properties);

            MemOperatorSessionClaims.RememberSigningInSessionId(
                context.HttpContext,
                sessionId);
        }

        return Task.CompletedTask;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        await securityStampValidator.ValidateAsync(context);

        if (context.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var allowed = await sessionValidator.IsCurrentSessionAllowedAsync(
            context.Principal,
            context.HttpContext.RequestAborted);

        if (allowed)
        {
            return;
        }

        context.RejectPrincipal();

        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (InstallerAuthHttpRequestHelpers.IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (InstallerAuthHttpRequestHelpers.IsApiRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    }
}
