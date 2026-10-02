using System.Security.Claims;
using HostAgent.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace HostAgent.Security;

/// <summary>
/// Transitional control-plane gate for browser-originated Host Agent routes.
///
/// Before the first Platform Owner exists, the current installer-unlock cookie
/// remains a temporary setup bridge. Once SEC-AUTH-03 completes bootstrap, a
/// named Platform Owner Identity session is accepted and legacy installer
/// sessions are rejected by their cookie validation event.
///
/// SEC-AUTH-04 replaces this coarse owner-only bridge with endpoint-specific
/// role/capability policies.
/// </summary>
public static class HostAgentEndpointOperatorGuard
{
    public static IResult? ValidateCurrentControlPlaneSession(HttpContext httpContext)
    {
        return HasCurrentControlPlaneSession(httpContext.User)
            ? null
            : Results.Unauthorized();
    }

    /// <summary>
    /// Applies the ordinary current-control-plane session gate first, then
    /// applies the server-owned high-risk identity-verification policy. When
    /// the policy is Required, it requires the server-side recent step-up
    /// policy. When the policy is Not required, the ordinary current session
    /// gate above remains mandatory and this extra verification is skipped.
    /// </summary>
    public static async Task<IResult?> ValidateRecentStepUpForCurrentControlPlaneSessionAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(authorizationService);

        var sessionDenied = ValidateCurrentControlPlaneSession(httpContext);

        if (sessionDenied is not null)
        {
            return sessionDenied;
        }

        if (!await ShouldRequireHighRiskStepUpAsync(httpContext))
        {
            return null;
        }

        var stepUp = await authorizationService.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        if (stepUp.Succeeded)
        {
            return null;
        }

        // This response describes authentication state. Do not let an
        // intermediary or browser cache retain it.
        httpContext.Response.Headers.CacheControl = "no-store";

        return Results.Json(
            new HostAgentErrorResponse(
                Error: "step_up_required",
                Detail: "Fresh identity verification is required before this action."),
            statusCode: StatusCodes.Status403Forbidden);
    }


    private static async Task<bool> ShouldRequireHighRiskStepUpAsync(HttpContext httpContext)
    {
        var settings = httpContext.RequestServices.GetService<IMemSecuritySettingsService>();

        if (settings is null)
        {
            return true;
        }

        var snapshot = await settings.GetEffectiveAsync(httpContext.RequestAborted);
        return snapshot.RequireHighRiskStepUp;
    }

    public static bool HasCurrentControlPlaneSession(ClaimsPrincipal user)
    {
        return HasNamedPlatformOwnerSession(user) ||
            HasTransitionalInstallerSession(user);
    }

    public static bool HasNamedControlPlaneSession(ClaimsPrincipal user)
    {
        // A protected browser operator cookie or approved CLI device credential
        // represents a named control-plane session. Role checks remain separate
        // so endpoints can distinguish unauthenticated (401) from authenticated
        // but insufficiently privileged (403) requests.
        return user.Identities.Any(identity =>
            identity.IsAuthenticated &&
            (string.Equals(
                 identity.AuthenticationType,
                 IdentityConstants.ApplicationScheme,
                 StringComparison.Ordinal) ||
             string.Equals(
                 identity.AuthenticationType,
                 MemCliDeviceAuthentication.Scheme,
                 StringComparison.Ordinal)));
    }

    public static bool HasNamedPlatformOwnerSession(ClaimsPrincipal user)
    {
        // A browser operator cookie and an approved CLI device credential both
        // represent a named Platform Owner. The device scheme is still subject
        // to server-side expiry, installation binding, security-stamp checks,
        // and the same high-risk RecentStepUp policy. It is not a localhost or
        // host-shell bypass.
        return HasNamedControlPlaneSession(user) &&
            user.IsInRole(MemOperatorRoles.PlatformOwner);
    }

    public static bool HasTransitionalInstallerSession(ClaimsPrincipal user)
    {
        return user.Identity?.IsAuthenticated == true &&
            user.HasClaim(
                InstallerAuthClaims.TransitionalInstallerUnlocked,
                InstallerAuthClaims.True);
    }
}
