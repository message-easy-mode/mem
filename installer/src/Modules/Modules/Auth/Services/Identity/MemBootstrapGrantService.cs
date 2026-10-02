using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

public sealed class BootstrapFlowException(
    string code,
    BootstrapFlowExceptionKind kind)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;

    public BootstrapFlowExceptionKind Kind { get; } = kind;
}

public enum BootstrapFlowExceptionKind
{
    Unavailable,
    Validation,
    InvalidState
}

public interface IMemBootstrapGrantService
{
    Task<BootstrapStateResponse> GetStateAsync(CancellationToken ct = default);

    Task<bool> HasCompletedPlatformOwnerAsync(CancellationToken ct = default);

    Task<MemBootstrapGrantEntity> IssueGrantAsync(CancellationToken ct = default);

    Task<PrepareFirstMemOperatorResponse> PrepareFirstOwnerAsync(
        Guid grantId,
        CreateFirstMemOperatorRequest request,
        CancellationToken ct = default);

    Task VerifyTotpAsync(
        Guid grantId,
        string? code,
        CancellationToken ct = default);

    Task<CompleteBootstrapResponse> CompleteAsync(
        Guid grantId,
        CancellationToken ct = default);

    Task CancelAsync(
        Guid grantId,
        CancellationToken ct = default);

    Task<bool> IsGrantCurrentAsync(
        Guid grantId,
        CancellationToken ct = default);
}

/// <summary>
/// Implements the strictly scoped first-owner bootstrap state machine.
/// A raw setup token is validated by the endpoint before this service is
/// invoked; only an opaque server-side grant is persisted here.
/// </summary>
public sealed class MemBootstrapGrantService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IMemOperatorAuditService audit,
    IOptions<MemOperatorIdentityOptions> options,
    TimeProvider timeProvider) : IMemBootstrapGrantService
{
    private readonly MemOperatorIdentityOptions _options = options.Value;

    public async Task<BootstrapStateResponse> GetStateAsync(CancellationToken ct = default)
    {
        var hasCompletedOwner = await HasCompletedPlatformOwnerAsync(ct);

        return new BootstrapStateResponse(
            RequiresFirstOwnerBootstrap: !hasCompletedOwner,
            HasCompletedPlatformOwner: hasCompletedOwner);
    }

    public Task<bool> HasCompletedPlatformOwnerAsync(CancellationToken ct = default)
    {
        return (
            from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where user.IsEnabled &&
                !user.IsBootstrapProvisioning &&
                role.Name == MemOperatorRoles.PlatformOwner
            select user.Id)
            .AnyAsync(ct);
    }

    public async Task<MemBootstrapGrantEntity> IssueGrantAsync(CancellationToken ct = default)
    {
        if (await HasCompletedPlatformOwnerAsync(ct))
        {
            throw new BootstrapFlowException(
                "bootstrap_unavailable",
                BootstrapFlowExceptionKind.Unavailable);
        }

        var now = timeProvider.GetUtcNow();
        await ExpireStaleGrantsAsync(now, ct);

        // SQLite cannot translate ordering or comparisons for DateTimeOffset columns.
        // Bootstrap grants are intentionally few and short-lived, so query the SQL-
        // translatable status predicate first, then evaluate expiry/recency in memory.
        var activeGrants = await db.MemBootstrapGrants
            .Where(grant =>
                grant.Status == MemBootstrapGrantStatuses.Active ||
                grant.Status == MemBootstrapGrantStatuses.TotpVerified)
            .ToArrayAsync(ct);

        var existing = activeGrants
            .Where(grant => grant.ExpiresAtUtc > now)
            .OrderByDescending(grant => grant.CreatedAtUtc)
            .FirstOrDefault();

        if (existing is not null)
        {
            return existing;
        }

        var grant = new MemBootstrapGrantEntity
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.BootstrapGrantMinutes),
            Status = MemBootstrapGrantStatuses.Active
        };

        await db.MemBootstrapGrants.AddAsync(grant, ct);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.bootstrap.grant_issued",
                Outcome: "succeeded",
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "first-owner"),
            ct);

        return grant;
    }

    public async Task<PrepareFirstMemOperatorResponse> PrepareFirstOwnerAsync(
        Guid grantId,
        CreateFirstMemOperatorRequest request,
        CancellationToken ct = default)
    {
        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);

        if (grant.PendingOperatorId is not null)
        {
            throw new BootstrapFlowException(
                "bootstrap_owner_already_prepared",
                BootstrapFlowExceptionKind.InvalidState);
        }

        if (await HasCompletedPlatformOwnerAsync(ct))
        {
            throw new BootstrapFlowException(
                "bootstrap_unavailable",
                BootstrapFlowExceptionKind.Unavailable);
        }

        var username = RequireUsername(request.Username);
        var email = NormalizeOptionalEmail(request.Email);
        var password = RequirePassword(request.Password);
        var now = timeProvider.GetUtcNow();

        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = username,
            Email = email,
            IsEnabled = false,
            IsBootstrapProvisioning = true,
            CreatedAtUtc = now
        };

        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw ToBootstrapValidationException(created);
        }

        var resetAuthenticator = await userManager.ResetAuthenticatorKeyAsync(user);
        if (!resetAuthenticator.Succeeded)
        {
            await userManager.DeleteAsync(user);
            throw new BootstrapFlowException(
                "bootstrap_authenticator_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        var authenticatorKey = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(authenticatorKey))
        {
            await userManager.DeleteAsync(user);
            throw new BootstrapFlowException(
                "bootstrap_authenticator_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        grant.PendingOperatorId = user.Id;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.bootstrap.owner_prepared",
                Outcome: "succeeded",
                SubjectOperatorId: user.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "first-owner"),
            ct);

        return new PrepareFirstMemOperatorResponse(
            Username: user.UserName!,
            ManualEntryKey: authenticatorKey,
            AuthenticatorUri: BuildAuthenticatorUri(user.UserName!, authenticatorKey));
    }

    public async Task VerifyTotpAsync(
        Guid grantId,
        string? code,
        CancellationToken ct = default)
    {
        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);

        if (grant.Status == MemBootstrapGrantStatuses.TotpVerified)
        {
            return;
        }

        var user = await GetPendingUserOrThrowAsync(grant, ct);
        var normalizedCode = NormalizeTotpCode(code);

        var isValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            normalizedCode);

        if (!isValid)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.bootstrap.totp_verified",
                    Outcome: "failed",
                    SubjectOperatorId: user.Id,
                    CorrelationId: grant.Id.ToString("D"),
                    ReasonCode: "invalid_totp"),
                ct);

            throw new BootstrapFlowException(
                "invalid_totp",
                BootstrapFlowExceptionKind.Validation);
        }

        grant.Status = MemBootstrapGrantStatuses.TotpVerified;
        grant.TotpVerifiedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.bootstrap.totp_verified",
                Outcome: "succeeded",
                SubjectOperatorId: user.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "first-owner"),
            ct);
    }

    public async Task<CompleteBootstrapResponse> CompleteAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);

        if (grant.Status != MemBootstrapGrantStatuses.TotpVerified ||
            grant.TotpVerifiedAtUtc is null)
        {
            throw new BootstrapFlowException(
                "bootstrap_totp_verification_required",
                BootstrapFlowExceptionKind.InvalidState);
        }

        var user = await GetPendingUserOrThrowAsync(grant, ct);

        if (!await roleManager.RoleExistsAsync(MemOperatorRoles.PlatformOwner))
        {
            throw new BootstrapFlowException(
                "bootstrap_role_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        var twoFactorEnabled = await userManager.SetTwoFactorEnabledAsync(user, true);
        if (!twoFactorEnabled.Succeeded)
        {
            throw new BootstrapFlowException(
                "bootstrap_authenticator_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user,
                _options.BootstrapRecoveryCodeCount))
            .ToArray();

        if (recoveryCodes.Length != _options.BootstrapRecoveryCodeCount)
        {
            throw new BootstrapFlowException(
                "bootstrap_recovery_codes_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        var roleAdded = await userManager.AddToRoleAsync(user, MemOperatorRoles.PlatformOwner);
        if (!roleAdded.Succeeded)
        {
            throw new BootstrapFlowException(
                "bootstrap_role_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        user.IsEnabled = true;
        user.IsBootstrapProvisioning = false;

        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new BootstrapFlowException(
                "bootstrap_owner_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        grant.Status = MemBootstrapGrantStatuses.Completed;
        grant.ConsumedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.bootstrap.completed",
                Outcome: "succeeded",
                ActorOperatorId: user.Id,
                SubjectOperatorId: user.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "first-owner"),
            ct);

        return new CompleteBootstrapResponse(
            Username: user.UserName!,
            RecoveryCodes: recoveryCodes);
    }

    public async Task CancelAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        var grant = await db.MemBootstrapGrants
            .SingleOrDefaultAsync(item => item.Id == grantId, ct);

        if (grant is null ||
            !MemBootstrapGrantStatuses.IsActive(grant.Status))
        {
            return;
        }

        if (grant.PendingOperatorId is Guid pendingUserId)
        {
            var user = await userManager.FindByIdAsync(pendingUserId.ToString("D"));

            if (user is { IsBootstrapProvisioning: true, IsEnabled: false })
            {
                var deleted = await userManager.DeleteAsync(user);

                if (!deleted.Succeeded)
                {
                    throw new BootstrapFlowException(
                        "bootstrap_cancel_failed",
                        BootstrapFlowExceptionKind.InvalidState);
                }
            }
        }

        grant.Status = MemBootstrapGrantStatuses.Cancelled;
        grant.CancelledAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.bootstrap.cancelled",
                Outcome: "succeeded",
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "first-owner"),
            ct);
    }

    public async Task<bool> IsGrantCurrentAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        if (await HasCompletedPlatformOwnerAsync(ct))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var grant = await db.MemBootstrapGrants
            .SingleOrDefaultAsync(item => item.Id == grantId, ct);

        if (grant is null ||
            !MemBootstrapGrantStatuses.IsActive(grant.Status) ||
            grant.ExpiresAtUtc <= now)
        {
            if (grant is not null &&
                MemBootstrapGrantStatuses.IsActive(grant.Status) &&
                grant.ExpiresAtUtc <= now)
            {
                grant.Status = MemBootstrapGrantStatuses.Expired;
                await db.SaveChangesAsync(ct);
            }

            return false;
        }

        return true;
    }

    private async Task<MemBootstrapGrantEntity> GetCurrentGrantOrThrowAsync(
        Guid grantId,
        CancellationToken ct)
    {
        if (!await IsGrantCurrentAsync(grantId, ct))
        {
            throw new BootstrapFlowException(
                "bootstrap_grant_unavailable",
                BootstrapFlowExceptionKind.Unavailable);
        }

        return await db.MemBootstrapGrants
            .SingleAsync(item => item.Id == grantId, ct);
    }

    private async Task<MemOperator> GetPendingUserOrThrowAsync(
        MemBootstrapGrantEntity grant,
        CancellationToken ct)
    {
        if (grant.PendingOperatorId is not Guid pendingOperatorId)
        {
            throw new BootstrapFlowException(
                "bootstrap_owner_not_prepared",
                BootstrapFlowExceptionKind.InvalidState);
        }

        var user = await userManager.FindByIdAsync(pendingOperatorId.ToString("D"));

        if (user is not { IsBootstrapProvisioning: true, IsEnabled: false })
        {
            throw new BootstrapFlowException(
                "bootstrap_owner_unavailable",
                BootstrapFlowExceptionKind.InvalidState);
        }

        return user;
    }

    private async Task ExpireStaleGrantsAsync(
        DateTimeOffset now,
        CancellationToken ct)
    {
        // SQLite cannot translate DateTimeOffset comparisons. The bootstrap grant
        // table contains only short-lived first-owner records, so evaluate expiry
        // after materialising the small active-grant set.
        var activeGrants = await db.MemBootstrapGrants
            .Where(grant =>
                grant.Status == MemBootstrapGrantStatuses.Active ||
                grant.Status == MemBootstrapGrantStatuses.TotpVerified)
            .ToArrayAsync(ct);

        var staleGrants = activeGrants
            .Where(grant => grant.ExpiresAtUtc <= now)
            .ToArray();

        if (staleGrants.Length == 0)
        {
            return;
        }

        foreach (var grant in staleGrants)
        {
            grant.Status = MemBootstrapGrantStatuses.Expired;

            if (grant.PendingOperatorId is Guid pendingUserId)
            {
                var user = await userManager.FindByIdAsync(pendingUserId.ToString("D"));

                if (user is { IsBootstrapProvisioning: true, IsEnabled: false })
                {
                    var deleted = await userManager.DeleteAsync(user);

                    if (!deleted.Succeeded)
                    {
                        throw new BootstrapFlowException(
                            "bootstrap_expiry_cleanup_failed",
                            BootstrapFlowExceptionKind.InvalidState);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string RequireUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BootstrapFlowException(
                "username_required",
                BootstrapFlowExceptionKind.Validation);
        }

        var username = value.Trim();

        if (username.Length is < 3 or > 100)
        {
            throw new BootstrapFlowException(
                "username_invalid",
                BootstrapFlowExceptionKind.Validation);
        }

        return username;
    }

    private static string? NormalizeOptionalEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var email = value.Trim();

        if (email.Length > 256)
        {
            throw new BootstrapFlowException(
                "email_invalid",
                BootstrapFlowExceptionKind.Validation);
        }

        return email;
    }

    private static string RequirePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BootstrapFlowException(
                "password_required",
                BootstrapFlowExceptionKind.Validation);
        }

        return value;
    }

    private static string NormalizeTotpCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BootstrapFlowException(
                "totp_required",
                BootstrapFlowExceptionKind.Validation);
        }

        var code = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (code.Length is < 6 or > 12 ||
            !code.All(char.IsAsciiDigit))
        {
            throw new BootstrapFlowException(
                "invalid_totp",
                BootstrapFlowExceptionKind.Validation);
        }

        return code;
    }

    private static BootstrapFlowException ToBootstrapValidationException(
        IdentityResult result)
    {
        var duplicateUsername = result.Errors.Any(error =>
            string.Equals(error.Code, "DuplicateUserName", StringComparison.Ordinal));

        return new BootstrapFlowException(
            duplicateUsername ? "username_unavailable" : "password_not_accepted",
            BootstrapFlowExceptionKind.Validation);
    }

    private static string BuildAuthenticatorUri(
        string username,
        string authenticatorKey)
    {
        var issuer = "MEM Control Plane";
        var label = Uri.EscapeDataString($"{issuer}:{username}");

        return $"otpauth://totp/{label}?secret={Uri.EscapeDataString(authenticatorKey)}" +
            $"&issuer={Uri.EscapeDataString(issuer)}&digits=6";
    }
}
