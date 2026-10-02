using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

public sealed record MemOperatorHostPasswordRecoveryResult(
    Guid OperatorId,
    string Username,
    bool TotpPreserved,
    int RecoveryCodeCount,
    IReadOnlyList<string> Roles);

public sealed class MemOperatorHostRecoveryException(
    string code,
    string message,
    IReadOnlyList<IdentityError>? identityErrors = null) : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyList<IdentityError> IdentityErrors { get; } =
        identityErrors ?? Array.Empty<IdentityError>();
}

public interface IMemOperatorHostRecoveryService
{
    Task<MemOperatorHostPasswordRecoveryResult> ResetPlatformOwnerPasswordAsync(
        string username,
        string newPassword,
        string? correlationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Host-authoritative emergency password recovery for an already-enrolled
/// Platform Owner. This service is intentionally not exposed through HTTP.
///
/// The password is reset through ASP.NET Core Identity's normal reset-token
/// path, so the configured MEM password policy remains authoritative. TOTP,
/// recovery codes, roles and other account state are preserved. Lockout is
/// cleared and the security stamp is rotated, revoking existing sessions.
/// </summary>
public sealed class MemOperatorHostRecoveryService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    IMemOperatorAuditService audit) : IMemOperatorHostRecoveryService
{
    public async Task<MemOperatorHostPasswordRecoveryResult> ResetPlatformOwnerPasswordAsync(
        string username,
        string newPassword,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedUsername = username?.Trim();

        if (string.IsNullOrWhiteSpace(normalizedUsername))
        {
            throw new MemOperatorHostRecoveryException(
                "owner_username_required",
                "A Platform Owner username is required.");
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            throw new MemOperatorHostRecoveryException(
                "new_password_required",
                "A new password is required.");
        }

        var user = await userManager.FindByNameAsync(normalizedUsername);

        if (user is null)
        {
            throw new MemOperatorHostRecoveryException(
                "platform_owner_not_found",
                $"No MEM Platform Owner named '{normalizedUsername}' was found.");
        }

        if (user.IsBootstrapProvisioning)
        {
            throw new MemOperatorHostRecoveryException(
                "platform_owner_enrollment_incomplete",
                "The selected account has not completed Platform Owner enrollment.");
        }

        if (!user.IsEnabled)
        {
            throw new MemOperatorHostRecoveryException(
                "platform_owner_disabled",
                "The selected Platform Owner is disabled. Password recovery does not enable accounts.");
        }

        if (!await userManager.IsInRoleAsync(user, MemOperatorRoles.PlatformOwner))
        {
            throw new MemOperatorHostRecoveryException(
                "platform_owner_role_required",
                "Host password recovery is restricted to Platform Owner accounts.");
        }

        var rolesBefore = (await userManager.GetRolesAsync(user))
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
        var authenticatorKeyBefore = await userManager.GetAuthenticatorKeyAsync(user);
        var twoFactorBefore = user.TwoFactorEnabled;
        var recoveryCodeCountBefore = await userManager.CountRecoveryCodesAsync(user);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await userManager.ResetPasswordAsync(
            user,
            resetToken,
            newPassword);

        if (!reset.Succeeded)
        {
            throw new MemOperatorHostRecoveryException(
                "password_policy_rejected",
                "The new password was rejected by the MEM password policy.",
                reset.Errors.ToArray());
        }

        if (userManager.SupportsUserLockout)
        {
            var resetFailures = await userManager.ResetAccessFailedCountAsync(user);
            EnsureSucceeded(resetFailures, "lockout_reset_failed");

            var clearLockout = await userManager.SetLockoutEndDateAsync(user, null);
            EnsureSucceeded(clearLockout, "lockout_reset_failed");
        }

        // ResetPasswordAsync normally updates the security stamp through the
        // Identity password-hash path. Rotate it explicitly as the recovery
        // boundary so every pre-existing protected browser session is revoked
        // even if framework internals change.
        var stamp = await userManager.UpdateSecurityStampAsync(user);
        EnsureSucceeded(stamp, "session_revocation_failed");

        var rolesAfter = (await userManager.GetRolesAsync(user))
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
        var authenticatorKeyAfter = await userManager.GetAuthenticatorKeyAsync(user);
        var recoveryCodeCountAfter = await userManager.CountRecoveryCodesAsync(user);

        if (twoFactorBefore != user.TwoFactorEnabled ||
            !string.Equals(
                authenticatorKeyBefore,
                authenticatorKeyAfter,
                StringComparison.Ordinal) ||
            recoveryCodeCountBefore != recoveryCodeCountAfter ||
            !rolesBefore.SequenceEqual(rolesAfter, StringComparer.Ordinal))
        {
            throw new MemOperatorHostRecoveryException(
                "account_state_preservation_failed",
                "Password recovery changed account state outside the approved password/session boundary.");
        }

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.platform_owner.host_password_recovered",
                Outcome: "succeeded",
                ActorOperatorId: null,
                SubjectOperatorId: user.Id,
                CorrelationId: correlationId,
                ReasonCode: "host_authoritative_password_reset"),
            ct);

        await transaction.CommitAsync(ct);

        return new MemOperatorHostPasswordRecoveryResult(
            OperatorId: user.Id,
            Username: user.UserName ?? normalizedUsername,
            TotpPreserved: user.TwoFactorEnabled,
            RecoveryCodeCount: recoveryCodeCountAfter,
            Roles: rolesAfter);
    }

    private static void EnsureSucceeded(
        IdentityResult result,
        string code)
    {
        if (result.Succeeded)
        {
            return;
        }

        throw new MemOperatorHostRecoveryException(
            code,
            "MEM Identity could not complete the host recovery operation.",
            result.Errors.ToArray());
    }
}
