using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Services;

/// <summary>
/// The legacy installer cookie is valid only while no completed Platform Owner
/// exists. Completing the first-owner bootstrap revokes this generic session
/// on its next request; it can never remain a second normal administrator path.
/// </summary>
public sealed class InstallerAuthCookieEvents(
    IMemBootstrapGrantService bootstrapGrants) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (!await bootstrapGrants.HasCompletedPlatformOwnerAsync(
                context.HttpContext.RequestAborted))
        {
            return;
        }

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
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
