using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Modules.Auth.Identity;

/// <summary>
/// Supplies a non-secret, per-browser-session identifier inside the protected
/// Identity application cookie. The identifier exists only to bind a later
/// server-side step-up grant to this exact authenticated browser session.
/// It is not exposed through API responses, browser storage, URLs, or logs.
/// </summary>
public static class MemOperatorSessionClaims
{
    public const string SessionIdClaimType = "mem.operator.session_id";

    private const string SessionIdPropertyKey = "mem.operator.session_id";

    private const string SigningInSessionIdItemKey = "mem.operator.signing_in_session_id";

    public static Guid EnsureSessionId(ClaimsPrincipal principal, AuthenticationProperties properties)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(properties);

        properties.Items.TryGetValue(SessionIdPropertyKey, out var persistedSessionId);

        var sessionId = ReadSessionId(persistedSessionId)
            ?? ReadSessionId(principal.FindFirstValue(SessionIdClaimType))
            ?? Guid.NewGuid();

        properties.Items[SessionIdPropertyKey] = sessionId.ToString("D");

        ReplaceSessionClaim(principal, sessionId);


        return sessionId;
    }

    /// <summary>
    /// Records the freshly minted session id for the current sign-in response.
    /// This is request-local only: it never leaves server memory and lets the
    /// endpoint attach follow-on server-side state, such as a recent step-up
    /// grant, to the exact protected cookie being issued.
    /// </summary>
    public static void RememberSigningInSessionId(HttpContext httpContext, Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Items[SigningInSessionIdItemKey] = sessionId;
    }

    public static bool TryGetSigningInSessionId(HttpContext httpContext, out Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Items.TryGetValue(SigningInSessionIdItemKey, out var value) &&
            value is Guid persistedSessionId)
        {
            sessionId = persistedSessionId;
            return true;
        }

        sessionId = default;
        return false;
    }

    /// <summary>
    /// ASP.NET Core's security-stamp validator rebuilds the principal during a
    /// cookie refresh. Preserve the existing protected session id so a valid
    /// refresh renews the same browser session rather than losing its binding.
    /// </summary>
    public static void PreserveOnSecurityStampRefresh(
        SecurityStampRefreshingPrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sessionId = ReadSessionId(
            context.CurrentPrincipal?.FindFirstValue(SessionIdClaimType));

        if (sessionId is null || context.NewPrincipal is null)
        {
            return;
        }

        ReplaceSessionClaim(context.NewPrincipal, sessionId.Value);
    }

    public static bool TryGetSessionId(ClaimsPrincipal principal, out Guid sessionId)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(SessionIdClaimType);

        return Guid.TryParse(value, out sessionId);
    }

    private static void ReplaceSessionClaim(ClaimsPrincipal principal, Guid sessionId)
    {
        foreach (var identity in principal.Identities
                     .OfType<ClaimsIdentity>()
                     .Where(identity => identity.IsAuthenticated))
        {
            foreach (var existing in identity.FindAll(SessionIdClaimType).ToArray())
            {
                identity.RemoveClaim(existing);
            }

            identity.AddClaim(new Claim(SessionIdClaimType, sessionId.ToString("D")));
        }
    }

    private static Guid? ReadSessionId(string? value)
    {
        return Guid.TryParse(value, out var sessionId)
            ? sessionId
            : null;
    }
}
