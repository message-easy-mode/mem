using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Identity;

/// <summary>
/// Authentication scheme for an opaque, browser-approved MEM CLI device
/// credential. The credential is never represented as a browser cookie and is
/// validated against the server-derived current installation on every request.
/// </summary>
public static class MemCliDeviceAuthentication
{
    public const string Scheme = "MemCliDevice";

    public const string SessionIdClaimType = "mem.cli.device-session-id";

    public const string IdleExpiresAtUtcClaimType = "mem.cli.device-idle-expires-at-utc";

    public const string AbsoluteExpiresAtUtcClaimType = "mem.cli.device-absolute-expires-at-utc";
}

/// <summary>
/// Reads only a standard Authorization: Bearer opaque device credential. The
/// raw credential is never copied into a claim, log entry, response, or error.
/// </summary>
public sealed class MemCliDeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IMemCliDeviceInstallationBindingService installationBinding,
    IMemCliDeviceAuthorizationService deviceSessions,
    UserManager<MemOperator> userManager)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var rawHeader = Request.Headers.Authorization.ToString();

        if (string.IsNullOrWhiteSpace(rawHeader))
        {
            return AuthenticateResult.NoResult();
        }

        if (!AuthenticationHeaderValue.TryParse(rawHeader, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return AuthenticateResult.Fail("invalid_cli_device_credential");
        }

        var installationId = await installationBinding.GetCurrentInstallationIdAsync(
            Context.RequestAborted);

        if (installationId is null)
        {
            return AuthenticateResult.Fail("invalid_cli_device_credential");
        }

        var validation = await deviceSessions.ValidateAsync(
            installationId.Value,
            header.Parameter.Trim(),
            Context.TraceIdentifier,
            Context.RequestAborted);

        if (!string.Equals(
                validation.Status,
                "authenticated",
                StringComparison.Ordinal) ||
            validation.OperatorId is not { } operatorId ||
            validation.SessionId is not { } sessionId)
        {
            return AuthenticateResult.Fail("invalid_cli_device_credential");
        }

        var operatorAccount = await userManager.FindByIdAsync(
            operatorId.ToString("D"));

        if (operatorAccount is null)
        {
            return AuthenticateResult.Fail("invalid_cli_device_credential");
        }

        var roles = await userManager.GetRolesAsync(operatorAccount);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, operatorAccount.Id.ToString("D")),
            new(
                ClaimTypes.Name,
                string.IsNullOrWhiteSpace(operatorAccount.UserName)
                    ? "operator"
                    : operatorAccount.UserName),
            new(
                MemCliDeviceAuthentication.SessionIdClaimType,
                sessionId.ToString("D"))
        };

        if (validation.IdleExpiresAtUtc is { } idleExpiresAtUtc)
        {
            claims.Add(new Claim(
                MemCliDeviceAuthentication.IdleExpiresAtUtcClaimType,
                idleExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (validation.AbsoluteExpiresAtUtc is { } absoluteExpiresAtUtc)
        {
            claims.Add(new Claim(
                MemCliDeviceAuthentication.AbsoluteExpiresAtUtcClaimType,
                absoluteExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture)));
        }

        claims.AddRange(
            roles
                .OrderBy(role => role, StringComparer.Ordinal)
                .Select(role => new Claim(ClaimTypes.Role, role)));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, MemCliDeviceAuthentication.Scheme));

        return AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        return Response.WriteAsJsonAsync(new { status = "auth_required" });
    }

    protected override Task HandleForbiddenAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";

        return Response.WriteAsJsonAsync(new { status = "forbidden" });
    }
}
