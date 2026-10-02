using Carter;
using System.Globalization;
using System.Security.Claims;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Endpoints;

/// <summary>
/// Narrow HTTP contract for a future browser-approved MEM CLI device-login
/// flow. It also exposes a device-scheme-only safe session projection used by
/// the future CLI transport. Browser approval remains bound to the named
/// operator cookie and its existing RecentStepUp requirement.
/// </summary>
public sealed class MemCliDeviceAuthorizationEndpoints : ICarterModule
{
    private const string BrowserMutationHeader = "X-MEM-Operator-Request";
    private const string BrowserMutationHeaderValue = "1";

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/cli-device")
            .WithTags("MEM CLI Device Authorization");

        // This is intentionally device-scheme-only. A browser operator cookie
        // cannot be copied into a terminal as a substitute CLI credential, and
        // an opaque CLI credential cannot be used for browser-only approval
        // routes below.
        group.MapGet("/session", (HttpContext httpContext) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var roles = httpContext.User
                .FindAll(ClaimTypes.Role)
                .Select(claim => claim.Value)
                .OrderBy(role => role, StringComparer.Ordinal)
                .ToArray();

            return Results.Ok(new MemCliDeviceSessionStatusResponse(
                Status: "authenticated",
                DisplayName: httpContext.User.Identity?.Name,
                Roles: roles,
                IdleExpiresAtUtc: ReadDateTimeOffsetClaim(
                    httpContext.User,
                    MemCliDeviceAuthentication.IdleExpiresAtUtcClaimType),
                AbsoluteExpiresAtUtc: ReadDateTimeOffsetClaim(
                    httpContext.User,
                    MemCliDeviceAuthentication.AbsoluteExpiresAtUtcClaimType)));
        })
        .RequireAuthorization(MemOperatorPolicies.CliDeviceSession);

        group.MapDelete("/session", async (
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!TryReadCurrentDeviceSessionId(
                    httpContext.User,
                    out var sessionId))
            {
                return Results.Unauthorized();
            }

            var authorizations = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            var revoked = await authorizations.RevokeSessionAsync(
                sessionId,
                httpContext.TraceIdentifier,
                ct);

            return Results.Ok(new MemCliDeviceSessionRevocationResponse(
                revoked.Status,
                revoked.RevokedAtUtc));
        })
        .RequireAuthorization(MemOperatorPolicies.CliDeviceSession);

        group.MapPost("/authorizations", async (
            [FromBody] StartMemCliDeviceAuthorizationRequest request,
            HttpContext httpContext,
            [FromServices] IConfiguration configuration,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var rateLimiter = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationRateLimiter>();

            var rateLimited = TryAcquireAnonymousBudget(
                rateLimiter.TryAcquireStart(),
                httpContext);

            if (rateLimited is not null)
            {
                return rateLimited;
            }

            // The installation boundary is never supplied by the CLI. This
            // prevents one server/profile from asking the control plane to
            // mint a credential scoped to a caller-selected installation id.
            var installationBinding = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceInstallationBindingService>();

            var installationId = await installationBinding
                .GetCurrentInstallationIdAsync(ct);

            if (installationId is null)
            {
                return SafeStatus(
                    "authorization_unavailable",
                    StatusCodes.Status503ServiceUnavailable);
            }

            var authorizations = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            try
            {
                var started = await authorizations.StartAsync(
                    installationId.Value,
                    request.VerifierChallenge ?? string.Empty,
                    request.DeviceLabel,
                    httpContext.TraceIdentifier,
                    ct);

                return Results.Ok(new StartMemCliDeviceAuthorizationResponse(
                    "authorization_started",
                    started.AuthorizationId,
                    started.UserCode,
                    started.ExpiresAtUtc,
                    ResolveBrowserApprovalUrl(configuration)));
            }
            catch (ArgumentException)
            {
                return SafeStatus(
                    "validation_failed",
                    StatusCodes.Status422UnprocessableEntity);
            }
        })
        .AllowAnonymous();

        group.MapPost("/authorizations/{authorizationId:guid}/poll", async (
            Guid authorizationId,
            [FromBody] PollMemCliDeviceAuthorizationRequest request,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var rateLimiter = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationRateLimiter>();

            var rateLimited = TryAcquireAnonymousBudget(
                rateLimiter.TryAcquirePoll(),
                httpContext);

            if (rateLimited is not null)
            {
                return rateLimited;
            }

            var authorizations = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            // PollAsync deliberately returns an opaque, stable state for an
            // unknown id or invalid verifier. It returns a raw credential only
            // once after browser approval and matching verifier proof.
            return Results.Ok(await authorizations.PollAsync(
                authorizationId,
                request.Verifier ?? string.Empty,
                httpContext.TraceIdentifier,
                ct));
        })
        .AllowAnonymous();

        group.MapPost("/authorizations/review", async (
            [FromBody] ReviewMemCliDeviceAuthorizationRequest request,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            // A short device code is entered only in the same-origin browser
            // surface. Requiring this non-simple request header prevents a
            // cross-site form post from using an existing operator cookie to
            // probe pending device labels.
            if (!HasBrowserMutationHeader(httpContext))
            {
                return SafeStatus("forbidden", StatusCodes.Status403Forbidden);
            }

            var authorizations = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            return Results.Ok(await authorizations.ReviewAsync(
                request.UserCode ?? string.Empty,
                httpContext.TraceIdentifier,
                ct));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapPost("/authorizations/approve", async (
            [FromBody] DecideMemCliDeviceAuthorizationRequest request,
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!HasBrowserMutationHeader(httpContext))
            {
                return SafeStatus("forbidden", StatusCodes.Status403Forbidden);
            }

            var stepUpRequired = await RequireRecentStepUpAsync(
                httpContext,
                authorizationService);

            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            // Resolve account and authorization services only after the current
            // browser session has satisfied RecentStepUp. The direct regression
            // test omits these services to prove the rejection path is isolated.
            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var authorizations = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            var currentOperator = await GetCurrentOperatorAsync(
                httpContext,
                userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await authorizations.ApproveAsync(
                request.UserCode ?? string.Empty,
                currentOperator.Id,
                httpContext.TraceIdentifier,
                ct));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapPost("/authorizations/deny", async (
            [FromBody] DecideMemCliDeviceAuthorizationRequest request,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!HasBrowserMutationHeader(httpContext))
            {
                return SafeStatus("forbidden", StatusCodes.Status403Forbidden);
            }

            // Denial does not mint a credential and does not broaden access.
            // It still requires the normal named MFA-completed operator session
            // enforced by the group policy and the same-origin custom header.
            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var authorizations = httpContext.RequestServices
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            var currentOperator = await GetCurrentOperatorAsync(
                httpContext,
                userManager);

            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await authorizations.DenyAsync(
                request.UserCode ?? string.Empty,
                currentOperator.Id,
                httpContext.TraceIdentifier,
                ct));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);
    }

    private static async Task<MemOperator?> GetCurrentOperatorAsync(
        HttpContext httpContext,
        UserManager<MemOperator> userManager)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return await userManager.GetUserAsync(httpContext.User);
    }

    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService)
    {
        var result = await authorizationService.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        return result.Succeeded
            ? null
            : SafeStatus("step_up_required", StatusCodes.Status403Forbidden);
    }

    private static bool TryReadCurrentDeviceSessionId(
        ClaimsPrincipal principal,
        out Guid sessionId)
    {
        sessionId = Guid.Empty;

        var value = principal.FindFirstValue(
            MemCliDeviceAuthentication.SessionIdClaimType);

        return Guid.TryParse(value, out sessionId) &&
            sessionId != Guid.Empty;
    }

    private static DateTimeOffset? ReadDateTimeOffsetClaim(
        ClaimsPrincipal user,
        string claimType)
    {
        var value = user.FindFirst(claimType)?.Value;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static IResult? TryAcquireAnonymousBudget(
        MemCliDeviceAuthorizationRateLimitLease lease,
        HttpContext httpContext)
    {
        if (lease.Acquired)
        {
            return null;
        }

        if (lease.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers["Retry-After"] =
                Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
        }

        return SafeStatus("rate_limited", StatusCodes.Status429TooManyRequests);
    }

    private static bool HasBrowserMutationHeader(HttpContext httpContext) =>
        httpContext.Request.Headers.TryGetValue(
            BrowserMutationHeader,
            out var value) &&
        string.Equals(
            value.ToString(),
            BrowserMutationHeaderValue,
            StringComparison.Ordinal);

    /// <summary>
    /// Resolves the Web approval route exclusively from the control plane's
    /// server-owned public Web base URL. The CLI never supplies this value, so
    /// a local profile/API endpoint cannot redirect the operator to a caller-
    /// selected approval page. A missing or invalid configuration yields null;
    /// later CLI login code must refuse before presenting a device code when no
    /// trusted browser approval route is available.
    /// </summary>
    private static string? ResolveBrowserApprovalUrl(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredBaseUrl = configuration["App:PublicBaseUrl"];

        if (string.IsNullOrWhiteSpace(configuredBaseUrl) ||
            !Uri.TryCreate(
                configuredBaseUrl.Trim(),
                UriKind.Absolute,
                out var baseUri) ||
            string.IsNullOrWhiteSpace(baseUri.Host) ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return null;
        }

        var isHttps = string.Equals(
            baseUri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

        var isLoopbackHttp = string.Equals(
                baseUri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            baseUri.IsLoopback;

        if (!isHttps && !isLoopbackHttp)
        {
            return null;
        }

        var normalizedBase = baseUri.GetLeftPart(UriPartial.Authority);

        return new Uri(
            new Uri(normalizedBase + "/", UriKind.Absolute),
            "cli/authorize").AbsoluteUri;
    }

    private static IResult SafeStatus(string status, int statusCode) =>
        Results.Json(new { status }, statusCode: statusCode);

    private static void SetSensitiveNoStoreHeaders(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }
}
