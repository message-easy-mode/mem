using Carter;
using System.Globalization;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Configuration;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Infrastructure.Persistence;

namespace Modules.Auth.Endpoints;

/// <summary>
/// Minimal named-operator sign-in/session surface required once the first
/// Platform Owner bootstrap has completed. SEC-AUTH-04D adds layered login
/// abuse resistance while keeping failures opaque to the browser.
/// </summary>
public sealed class MemOperatorAuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("MEM Operator Auth");

        group.MapGet("/session", async (
            HttpContext httpContext,
            IMemBootstrapGrantService bootstrapGrants,
            UserManager<MemOperator> userManager,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var identityResult = await httpContext.AuthenticateAsync(
                IdentityConstants.ApplicationScheme);

            if (identityResult.Succeeded &&
                identityResult.Principal?.Identity?.IsAuthenticated == true)
            {
                var user = await userManager.GetUserAsync(identityResult.Principal);

                if (user is { IsEnabled: true, IsBootstrapProvisioning: false })
                {
                    var roles = await userManager.GetRolesAsync(user);

                    return Results.Ok(new MemOperatorSessionResponse(
                        Authenticated: true,
                        AuthenticationKind: "operator",
                        DisplayName: user.UserName,
                        Roles: roles.OrderBy(role => role).ToArray(),
                        RequiresFirstOwnerBootstrap: false,
                        HasCompletedPlatformOwner: true));
                }
            }

            var installerResult = await httpContext.AuthenticateAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

            if (installerResult.Succeeded &&
                installerResult.Principal?.Identity?.IsAuthenticated == true &&
                installerResult.Principal.HasClaim(
                    InstallerAuthClaims.TransitionalInstallerUnlocked,
                    InstallerAuthClaims.True))
            {
                var state = await bootstrapGrants.GetStateAsync(ct);

                return Results.Ok(new MemOperatorSessionResponse(
                    Authenticated: true,
                    AuthenticationKind: "installer_transition",
                    DisplayName: installerResult.Principal.Identity.Name,
                    Roles: [],
                    RequiresFirstOwnerBootstrap: state.RequiresFirstOwnerBootstrap,
                    HasCompletedPlatformOwner: state.HasCompletedPlatformOwner));
            }

            var unauthenticatedState = await bootstrapGrants.GetStateAsync(ct);

            return Results.Ok(new MemOperatorSessionResponse(
                Authenticated: false,
                AuthenticationKind: null,
                DisplayName: null,
                Roles: [],
                RequiresFirstOwnerBootstrap: unauthenticatedState.RequiresFirstOwnerBootstrap,
                HasCompletedPlatformOwner: unauthenticatedState.HasCompletedPlatformOwner));
        })
        .AllowAnonymous();

        group.MapPost("/login", async (
            MemOperatorLoginRequest request,
            SignInManager<MemOperator> signInManager,
            UserManager<MemOperator> userManager,
            IMemOperatorLoginRateLimiter loginRateLimiter,
            IMemOperatorAuditService audit,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!TryAcquireLoginAttempt(
                    loginRateLimiter,
                    httpContext,
                    out var rateLimited))
            {
                return rateLimited;
            }

            var username = request.Username?.Trim();
            var password = request.Password;

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: null,
                    httpContext: httpContext,
                    reasonCode: "invalid_request",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            // We look up the subject only for safe audit attribution and to
            // distinguish a newly-triggered lockout from an existing one. The
            // response below remains the same opaque 401 for unknown and
            // incorrect credentials.
            var user = await userManager.FindByNameAsync(username);
            var wasLockedOut = user is not null &&
                await userManager.IsLockedOutAsync(user);

            var result = user is null
                ? await signInManager.PasswordSignInAsync(
                    username,
                    password,
                    isPersistent: false,
                    lockoutOnFailure: true)
                : await signInManager.PasswordSignInAsync(
                    user,
                    password,
                    isPersistent: false,
                    lockoutOnFailure: true);

            if (result.RequiresTwoFactor)
            {
                // A disabled or incomplete account must never disclose a
                // usable MFA continuation screen, even after its password was
                // supplied correctly.
                if (user is not
                    {
                        IsEnabled: true,
                        IsBootstrapProvisioning: false,
                        TwoFactorEnabled: true
                    })
                {
                    await signInManager.SignOutAsync();

                    await WriteLoginFailureAsync(
                        audit: audit,
                        subjectOperatorId: user?.Id,
                        httpContext: httpContext,
                        reasonCode: "account_unavailable",
                        isLockedOut: false,
                        lockoutTriggered: false,
                        ct: ct);

                    return Results.Unauthorized();
                }

                return Results.Ok(new MemOperatorLoginResponse("mfa_required"));
            }

            if (!result.Succeeded)
            {
                var lockoutTriggered = result.IsLockedOut &&
                    !wasLockedOut &&
                    user is not null;

                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: user?.Id,
                    httpContext: httpContext,
                    reasonCode: result.IsLockedOut
                        ? lockoutTriggered
                            ? "lockout"
                            : "account_locked"
                        : "invalid_credentials",
                    isLockedOut: result.IsLockedOut,
                    lockoutTriggered: lockoutTriggered,
                    ct: ct);

                return Results.Unauthorized();
            }

            // All v0.1.1 MEM operators must use TOTP. This guard keeps a
            // future incorrectly-provisioned non-MFA account from gaining a
            // normal session.
            if (user is not
                {
                    IsEnabled: true,
                    IsBootstrapProvisioning: false,
                    TwoFactorEnabled: true
                })
            {
                await signInManager.SignOutAsync();

                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: user?.Id,
                    httpContext: httpContext,
                    reasonCode: "account_mfa_not_ready",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            user.LastLoginAtUtc = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(user);

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.login.succeeded",
                    Outcome: "succeeded",
                    ActorOperatorId: user.Id,
                    SubjectOperatorId: user.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "password_and_existing_mfa_session"),
                ct);

            var roles = await userManager.GetRolesAsync(user);

            return Results.Ok(new MemOperatorLoginResponse(
                "authenticated",
                new MemOperatorSessionResponse(
                    Authenticated: true,
                    AuthenticationKind: "operator",
                    DisplayName: user.UserName,
                    Roles: roles.OrderBy(role => role).ToArray(),
                    RequiresFirstOwnerBootstrap: false,
                    HasCompletedPlatformOwner: true)));
        })
        .AllowAnonymous();

        group.MapPost("/login/totp", async (
            VerifyMemOperatorTotpRequest request,
            SignInManager<MemOperator> signInManager,
            UserManager<MemOperator> userManager,
            IMemOperatorLoginRateLimiter loginRateLimiter,
            IMemOperatorStepUpService stepUp,
            IMemOperatorAuditService audit,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!TryAcquireLoginAttempt(
                    loginRateLimiter,
                    httpContext,
                    out var rateLimited))
            {
                return rateLimited;
            }

            var code = request.Code?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
            var pendingUser = await signInManager.GetTwoFactorAuthenticationUserAsync();

            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser?.Id,
                    httpContext: httpContext,
                    reasonCode: "invalid_totp",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            if (pendingUser is null ||
                !pendingUser.IsEnabled ||
                pendingUser.IsBootstrapProvisioning)
            {
                await signInManager.SignOutAsync();

                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser?.Id,
                    httpContext: httpContext,
                    reasonCode: "mfa_challenge_unavailable",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            var wasLockedOut = await userManager.IsLockedOutAsync(pendingUser);

            var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
                code,
                isPersistent: false,
                rememberClient: false);

            if (!result.Succeeded)
            {
                var lockoutTriggered = result.IsLockedOut && !wasLockedOut;

                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser.Id,
                    httpContext: httpContext,
                    reasonCode: result.IsLockedOut
                        ? lockoutTriggered
                            ? "lockout"
                            : "account_locked"
                        : "invalid_totp",
                    isLockedOut: result.IsLockedOut,
                    lockoutTriggered: lockoutTriggered,
                    ct: ct);

                return Results.Unauthorized();
            }

            pendingUser.LastLoginAtUtc = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(pendingUser);

            if (!MemOperatorSessionClaims.TryGetSigningInSessionId(httpContext, out var signedInSessionId))
            {
                await signInManager.SignOutAsync();

                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Operator sign-in could not be bound to a browser session.");
            }

            try
            {
                await stepUp.IssueForSessionAsync(
                    pendingUser,
                    signedInSessionId,
                    httpContext.TraceIdentifier,
                    ct);
            }
            catch (InvalidOperationException)
            {
                await signInManager.SignOutAsync();

                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Operator sign-in could not record recent verification.");
            }

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.login.succeeded",
                    Outcome: "succeeded",
                    ActorOperatorId: pendingUser.Id,
                    SubjectOperatorId: pendingUser.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "password_and_totp"),
                ct);

            var roles = await userManager.GetRolesAsync(pendingUser);

            return Results.Ok(new MemOperatorLoginResponse(
                "authenticated",
                new MemOperatorSessionResponse(
                    Authenticated: true,
                    AuthenticationKind: "operator",
                    DisplayName: pendingUser.UserName,
                    Roles: roles.OrderBy(role => role).ToArray(),
                    RequiresFirstOwnerBootstrap: false,
                    HasCompletedPlatformOwner: true)));
        })
        .AllowAnonymous();

        // A recovery code is an MFA completion factor only. The caller must
        // already hold the short-lived server-managed two-factor pending
        // cookie that is issued after a successful password stage. It cannot
        // be used as a standalone username + recovery-code sign-in route.
        group.MapPost("/login/recovery-code", async (
            VerifyMemOperatorRecoveryCodeRequest request,
            SignInManager<MemOperator> signInManager,
            UserManager<MemOperator> userManager,
            IMemOperatorLoginRateLimiter loginRateLimiter,
            IMemOperatorAuditService audit,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!TryAcquireLoginAttempt(
                    loginRateLimiter,
                    httpContext,
                    out var rateLimited))
            {
                return rateLimited;
            }

            var code = request.Code?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
            var pendingUser = await signInManager.GetTwoFactorAuthenticationUserAsync();

            if (string.IsNullOrWhiteSpace(code))
            {
                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser?.Id,
                    httpContext: httpContext,
                    reasonCode: "invalid_recovery_code",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            if (pendingUser is null ||
                !pendingUser.IsEnabled ||
                pendingUser.IsBootstrapProvisioning ||
                !pendingUser.TwoFactorEnabled)
            {
                await signInManager.SignOutAsync();

                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser?.Id,
                    httpContext: httpContext,
                    reasonCode: "mfa_challenge_unavailable",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            // Identity intentionally does not apply its usual pre-sign-in
            // lockout check when redeeming a high-entropy recovery code. MEM
            // must still honour an account lockout that was triggered by the
            // password or authenticator stage; a recovery code cannot bypass
            // that security control while a valid MFA-pending cookie exists.
            if (await userManager.IsLockedOutAsync(pendingUser))
            {
                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser.Id,
                    httpContext: httpContext,
                    reasonCode: "account_locked",
                    isLockedOut: true,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(code);

            if (!result.Succeeded)
            {
                await WriteLoginFailureAsync(
                    audit: audit,
                    subjectOperatorId: pendingUser.Id,
                    httpContext: httpContext,
                    reasonCode: "invalid_recovery_code",
                    isLockedOut: false,
                    lockoutTriggered: false,
                    ct: ct);

                return Results.Unauthorized();
            }

            pendingUser.LastLoginAtUtc = DateTimeOffset.UtcNow;
            await userManager.UpdateAsync(pendingUser);

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.mfa.recovery-code.used",
                    Outcome: "succeeded",
                    ActorOperatorId: pendingUser.Id,
                    SubjectOperatorId: pendingUser.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "login_mfa_completion"),
                ct);

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.login.succeeded",
                    Outcome: "succeeded",
                    ActorOperatorId: pendingUser.Id,
                    SubjectOperatorId: pendingUser.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "password_and_recovery_code"),
                ct);

            var roles = await userManager.GetRolesAsync(pendingUser);

            return Results.Ok(new MemOperatorLoginResponse(
                "authenticated",
                new MemOperatorSessionResponse(
                    Authenticated: true,
                    AuthenticationKind: "operator",
                    DisplayName: pendingUser.UserName,
                    Roles: roles.OrderBy(role => role).ToArray(),
                    RequiresFirstOwnerBootstrap: false,
                    HasCompletedPlatformOwner: true)));
        })
        .AllowAnonymous();

        // Replacement recovery codes are an account-security mutation. They
        // are available only to the current named operator, only after a
        // session-bound password-plus-TOTP step-up, and are returned once in
        // this no-store response. The browser must never persist them.
        group.MapPost("/recovery-codes/regenerate", async (
            HttpContext httpContext,
            [FromServices] IAuthorizationService authorizationService,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var stepUpRequired = await RequireRecentStepUpAsync(
                httpContext,
                authorizationService);

            if (stepUpRequired is not null)
            {
                return stepUpRequired;
            }

            // Resolve the account and persistence services only after recent
            // step-up succeeds. This keeps a direct step-up denial independent
            // of the code-generation service graph.
            var userManager = httpContext.RequestServices
                .GetRequiredService<UserManager<MemOperator>>();
            var options = httpContext.RequestServices
                .GetRequiredService<IOptions<MemOperatorIdentityOptions>>();
            var audit = httpContext.RequestServices
                .GetRequiredService<IMemOperatorAuditService>();
            var db = httpContext.RequestServices
                .GetRequiredService<MemDbContext>();

            var currentOperator = await userManager.GetUserAsync(httpContext.User);

            if (currentOperator is not
                {
                    IsEnabled: true,
                    IsBootstrapProvisioning: false,
                    TwoFactorEnabled: true
                } ||
                await userManager.IsLockedOutAsync(currentOperator))
            {
                return Results.Json(
                    new { status = "recovery_code_regeneration_unavailable" },
                    statusCode: StatusCodes.Status409Conflict);
            }

            var recoveryCodeCount = options.Value.OperatorRecoveryCodeCount;

            // Identity persists the replacement set through the same scoped EF
            // context. Keep its update and the required audit record in one
            // transaction so an audit failure cannot leave the account with
            // invalidated prior codes and no one-time replacement response.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var generated = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                currentOperator,
                recoveryCodeCount);

            var recoveryCodes = generated?
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .ToArray();

            if (recoveryCodes is null || recoveryCodes.Length != recoveryCodeCount)
            {
                return Results.Json(
                    new { status = "recovery_code_regeneration_unavailable" },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.mfa.recovery-codes.regenerated",
                    Outcome: "succeeded",
                    ActorOperatorId: currentOperator.Id,
                    SubjectOperatorId: currentOperator.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "self_service_step_up"),
                ct);

            await transaction.CommitAsync(ct);

            return Results.Ok(new MemOperatorRecoveryCodesResponse(
                Status: "recovery_codes_regenerated",
                RecoveryCodes: recoveryCodes));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        // Self-service password rotation is intentionally separate from host
        // recovery. The caller can change only its own password and must have
        // a fresh server-side password-plus-TOTP grant in this exact browser
        // session. The general high-risk-step-up setting cannot bypass this
        // account-security boundary.
        group.MapPost("/password", async (
            ChangeMemOperatorPasswordRequest request,
            HttpContext httpContext,
            UserManager<MemOperator> userManager,
            IMemOperatorStepUpService stepUp,
            IMemOperatorAuditService audit,
            MemDbContext db,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            if (!await stepUp.HasActiveGrantAsync(httpContext.User, ct))
            {
                return PasswordChangeRejected(
                    "step_up_required",
                    StatusCodes.Status403Forbidden);
            }

            var currentOperator = await userManager.GetUserAsync(httpContext.User);

            if (currentOperator is not
                {
                    IsEnabled: true,
                    IsBootstrapProvisioning: false,
                    TwoFactorEnabled: true
                } ||
                !await userManager.HasPasswordAsync(currentOperator))
            {
                return PasswordChangeRejected(
                    "account_unavailable",
                    StatusCodes.Status403Forbidden);
            }

            var newPassword = request.NewPassword;
            var confirmation = request.ConfirmPassword;

            if (string.IsNullOrEmpty(newPassword))
            {
                return PasswordChangeRejected(
                    "new_password_required",
                    StatusCodes.Status400BadRequest);
            }

            if (!string.Equals(newPassword, confirmation, StringComparison.Ordinal))
            {
                return PasswordChangeRejected(
                    "password_confirmation_mismatch",
                    StatusCodes.Status400BadRequest);
            }

            // A successful response must mean the old password no longer
            // authenticates. Rejecting reuse also makes password rotation an
            // unambiguous credential change rather than a session-only reset.
            if (await userManager.CheckPasswordAsync(currentOperator, newPassword))
            {
                return PasswordChangeRejected(
                    "new_password_must_differ",
                    StatusCodes.Status400BadRequest);
            }

            var rolesBefore = (await userManager.GetRolesAsync(currentOperator))
                .OrderBy(role => role, StringComparer.Ordinal)
                .ToArray();
            var authenticatorKeyBefore = await userManager.GetAuthenticatorKeyAsync(currentOperator);
            var recoveryCodeCountBefore = await userManager.CountRecoveryCodesAsync(currentOperator);
            var usernameBefore = currentOperator.UserName;
            var emailBefore = currentOperator.Email;
            var createdAtBefore = currentOperator.CreatedAtUtc;
            var lastLoginAtBefore = currentOperator.LastLoginAtUtc;
            var lastStepUpAtBefore = currentOperator.LastStepUpAtUtc;

            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var resetToken = await userManager.GeneratePasswordResetTokenAsync(currentOperator);
            var reset = await userManager.ResetPasswordAsync(
                currentOperator,
                resetToken,
                newPassword);

            if (!reset.Succeeded)
            {
                return PasswordChangeRejected(
                    "password_not_accepted",
                    StatusCodes.Status400BadRequest);
            }

            // ResetPasswordAsync normally rotates the stamp through Identity's
            // password-hash path. Rotate it explicitly as the MEM revocation
            // boundary so every pre-change browser and CLI/device session is
            // stale even if framework internals change later.
            var stamp = await userManager.UpdateSecurityStampAsync(currentOperator);
            if (!stamp.Succeeded)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Password change could not revoke existing sessions.");
            }

            await stepUp.RevokeAllForOperatorAsync(
                currentOperator.Id,
                currentOperator.Id,
                "password_changed",
                httpContext.TraceIdentifier,
                ct);

            var rolesAfter = (await userManager.GetRolesAsync(currentOperator))
                .OrderBy(role => role, StringComparer.Ordinal)
                .ToArray();
            var authenticatorKeyAfter = await userManager.GetAuthenticatorKeyAsync(currentOperator);
            var recoveryCodeCountAfter = await userManager.CountRecoveryCodesAsync(currentOperator);

            if (!currentOperator.TwoFactorEnabled ||
                !string.Equals(authenticatorKeyBefore, authenticatorKeyAfter, StringComparison.Ordinal) ||
                recoveryCodeCountBefore != recoveryCodeCountAfter ||
                !rolesBefore.SequenceEqual(rolesAfter, StringComparer.Ordinal) ||
                !string.Equals(usernameBefore, currentOperator.UserName, StringComparison.Ordinal) ||
                !string.Equals(emailBefore, currentOperator.Email, StringComparison.Ordinal) ||
                createdAtBefore != currentOperator.CreatedAtUtc ||
                lastLoginAtBefore != currentOperator.LastLoginAtUtc ||
                lastStepUpAtBefore != currentOperator.LastStepUpAtUtc ||
                !currentOperator.IsEnabled ||
                currentOperator.IsBootstrapProvisioning)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Password change could not preserve operator account state.");
            }

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.password.changed",
                    Outcome: "succeeded",
                    ActorOperatorId: currentOperator.Id,
                    SubjectOperatorId: currentOperator.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "self_service_password_change"),
                ct);

            await transaction.CommitAsync(ct);

            // The security-stamp rotation above invalidates every pre-change
            // browser session, including this request's cookie. Expire the
            // browser authentication cookies in this same successful response
            // so the next anonymous login does not arrive with stale Identity
            // state that can be rejected before password validation runs.
            await ClearBrowserAuthenticationAsync(httpContext);

            return Results.Ok(new MemOperatorPasswordChangeResponse("password_changed"));
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        // A recovery code deliberately cannot satisfy this route. Recovery
        // codes may complete the ordinary MFA sign-in challenge, but changes
        // with a high-risk step-up requirement must be confirmed with the
        // current password and authenticator-app TOTP factor.
        group.MapPost("/step-up", async (
            VerifyMemOperatorStepUpRequest request,
            UserManager<MemOperator> userManager,
            IMemOperatorStepUpService stepUp,
            IMemOperatorAuditService audit,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            var currentOperator = await userManager.GetUserAsync(httpContext.User);
            if (currentOperator is null)
            {
                return Results.Unauthorized();
            }

            var isCurrentlyLockedOut = await userManager.IsLockedOutAsync(currentOperator);

            if (currentOperator is not
                {
                    IsEnabled: true,
                    IsBootstrapProvisioning: false,
                    TwoFactorEnabled: true
                } ||
                isCurrentlyLockedOut)
            {
                await WriteStepUpFailureAsync(
                    audit,
                    currentOperator,
                    httpContext,
                    reasonCode: isCurrentlyLockedOut
                        ? "account_locked"
                        : "step_up_unavailable",
                    isLockedOut: isCurrentlyLockedOut,
                    lockoutTriggered: false,
                    ct);

                return StepUpRejected();
            }

            var password = request.Password;
            var code = request.Code?.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

            var passwordAccepted = !string.IsNullOrWhiteSpace(password) &&
                await userManager.CheckPasswordAsync(currentOperator, password);

            var totpAccepted = passwordAccepted &&
                !string.IsNullOrWhiteSpace(code) &&
                await userManager.VerifyTwoFactorTokenAsync(
                    currentOperator,
                    TokenOptions.DefaultAuthenticatorProvider,
                    code);

            if (!totpAccepted)
            {
                var wasLockedOut = await userManager.IsLockedOutAsync(currentOperator);
                var accessFailure = await userManager.AccessFailedAsync(currentOperator);

                if (!accessFailure.Succeeded)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Step-up verification was unavailable.");
                }

                var isLockedOut = await userManager.IsLockedOutAsync(currentOperator);
                var lockoutTriggered = isLockedOut && !wasLockedOut;

                await WriteStepUpFailureAsync(
                    audit,
                    currentOperator,
                    httpContext,
                    reasonCode: isLockedOut
                        ? lockoutTriggered
                            ? "lockout"
                            : "account_locked"
                        : "invalid_credentials",
                    isLockedOut: isLockedOut,
                    lockoutTriggered: lockoutTriggered,
                    ct);

                return StepUpRejected();
            }

            var resetFailures = await userManager.ResetAccessFailedCountAsync(currentOperator);
            if (!resetFailures.Succeeded)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "Step-up verification was unavailable.");
            }

            try
            {
                var grant = await stepUp.IssueAsync(
                    currentOperator,
                    httpContext.User,
                    httpContext.TraceIdentifier,
                    ct);

                return Results.Ok(new MemOperatorStepUpResponse(
                    "step_up_authenticated",
                    grant.ExpiresAtUtc));
            }
            catch (InvalidOperationException)
            {
                // A current application cookie without its protected session
                // binding must not be treated as recently authenticated.
                return StepUpRejected();
            }
        })
        .RequireAuthorization(MemOperatorPolicies.ReadSafeStatus);

        group.MapPost("/logout", async (
            HttpContext httpContext,
            IMemOperatorStepUpService stepUp,
            ILogger<MemOperatorAuthEndpoints> logger,
            CancellationToken ct) =>
        {
            SetSensitiveNoStoreHeaders(httpContext);

            try
            {
                await stepUp.RevokeCurrentSessionAsync(
                    httpContext.User,
                    "logout",
                    httpContext.TraceIdentifier,
                    ct);
            }
            catch (Exception exception)
            {
                // Logout must still clear the browser's normal session if an
                // audit/database outage prevents best-effort step-up cleanup.
                // A retained grant remains unusable without this exact
                // protected cookie session id and expires shortly.
                logger.LogWarning(
                    exception,
                    "MEM could not record step-up revocation during operator logout.");
            }

            await ClearBrowserAuthenticationAsync(httpContext);

            return Results.NoContent();
        })
        .AllowAnonymous();
    }

    private static async Task ClearBrowserAuthenticationAsync(HttpContext httpContext)
    {
        // Keep the explicit browser-authority cleanup identical for normal
        // logout and password rotation. These are cookie schemes only; CLI
        // device authority is revoked separately by the security-stamp
        // contract and must never be represented as a browser cookie.
        await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        await httpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);
        await httpContext.SignOutAsync(IdentityConstants.TwoFactorRememberMeScheme);
        await httpContext.SignOutAsync(MemBootstrapAuthentication.Scheme);
        await httpContext.SignOutAsync(MemOperatorEnrollmentAuthentication.Scheme);
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static void SetSensitiveNoStoreHeaders(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        httpContext.Response.Headers.Pragma = "no-cache";
    }

    private static bool TryAcquireLoginAttempt(
        IMemOperatorLoginRateLimiter loginRateLimiter,
        HttpContext httpContext,
        out IResult rateLimited)
    {
        var lease = loginRateLimiter.TryAcquire();

        if (lease.Acquired)
        {
            rateLimited = Results.NoContent();
            return true;
        }

        httpContext.Response.Headers["Cache-Control"] = "no-store";

        if (lease.RetryAfter is { } retryAfter)
        {
            httpContext.Response.Headers["Retry-After"] =
                Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
        }

        rateLimited = Results.Json(
            new MemOperatorLoginResponse("rate_limited"),
            statusCode: StatusCodes.Status429TooManyRequests);

        return false;
    }

    /// <summary>
    /// A stable, cache-safe refusal used by account-security actions that
    /// require a fresh session-bound step-up grant. The endpoint's ordinary
    /// role policy remains separate and is enforced by ASP.NET authorization.
    /// </summary>
    private static async Task<IResult?> RequireRecentStepUpAsync(
        HttpContext httpContext,
        IAuthorizationService authorizationService)
    {
        var settings = httpContext.RequestServices.GetService<IMemSecuritySettingsService>();
        if (settings is not null)
        {
            var snapshot = await settings.GetEffectiveAsync(httpContext.RequestAborted);
            if (!snapshot.RequireHighRiskStepUp)
            {
                return null;
            }
        }

        var result = await authorizationService.AuthorizeAsync(
            httpContext.User,
            resource: null,
            policyName: MemOperatorPolicies.RecentStepUp);

        if (result.Succeeded)
        {
            return null;
        }

        SetSensitiveNoStoreHeaders(httpContext);

        return Results.Json(
            new { status = "step_up_required" },
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static IResult PasswordChangeRejected(string status, int statusCode)
    {
        return Results.Json(
            new MemOperatorPasswordChangeResponse(status),
            statusCode: statusCode);
    }

    private static IResult StepUpRejected()
    {
        return Results.Json(
            new MemOperatorStepUpResponse("step_up_failed"),
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static async Task WriteStepUpFailureAsync(
        IMemOperatorAuditService audit,
        MemOperator subject,
        HttpContext httpContext,
        string reasonCode,
        bool isLockedOut,
        bool lockoutTriggered,
        CancellationToken ct)
    {
        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.step-up.failed",
                Outcome: isLockedOut ? "locked_out" : "failed",
                ActorOperatorId: subject.Id,
                SubjectOperatorId: subject.Id,
                CorrelationId: httpContext.TraceIdentifier,
                ReasonCode: reasonCode),
            ct);

        if (lockoutTriggered)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.lockout.triggered",
                    Outcome: "triggered",
                    SubjectOperatorId: subject.Id,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "step_up_credential_failure_threshold"),
                ct);
        }
    }

    private static async Task WriteLoginFailureAsync(
        IMemOperatorAuditService audit,
        Guid? subjectOperatorId,
        HttpContext httpContext,
        string reasonCode,
        bool isLockedOut,
        bool lockoutTriggered,
        CancellationToken ct)
    {
        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.login.failed",
                Outcome: isLockedOut ? "locked_out" : "failed",
                SubjectOperatorId: subjectOperatorId,
                CorrelationId: httpContext.TraceIdentifier,
                ReasonCode: reasonCode),
            ct);

        if (lockoutTriggered && subjectOperatorId is { } operatorId)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.lockout.triggered",
                    Outcome: "triggered",
                    SubjectOperatorId: operatorId,
                    CorrelationId: httpContext.TraceIdentifier,
                    ReasonCode: "credential_failure_threshold"),
                ct);
        }
    }
}
