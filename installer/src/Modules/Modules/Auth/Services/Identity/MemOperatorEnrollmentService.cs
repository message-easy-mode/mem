using System.Security.Cryptography;
using System.Text;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

public sealed class OperatorEnrollmentException(
    string code,
    OperatorEnrollmentFailureKind kind)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;

    public OperatorEnrollmentFailureKind Kind { get; } = kind;
}

public enum OperatorEnrollmentFailureKind
{
    Validation,
    NotFound,
    Conflict,
    Unavailable
}

public interface IMemOperatorEnrollmentService
{
    Task<IssueMemOperatorEnrollmentGrantResponse> IssueAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemOperatorEnrollmentStateResponse> GetCurrentStateAsync(
        Guid grantId,
        CancellationToken ct = default);

    Task<BeginMemOperatorEnrollmentResponse> BeginAsync(
        string? enrollmentCode,
        CancellationToken ct = default);

    Task<PrepareMemOperatorEnrollmentResponse> PrepareAsync(
        Guid grantId,
        PrepareMemOperatorEnrollmentRequest request,
        CancellationToken ct = default);

    Task VerifyTotpAsync(
        Guid grantId,
        string? code,
        CancellationToken ct = default);

    Task<CompleteMemOperatorEnrollmentResponse> CompleteAsync(
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
/// Handles one-time enrolment for a pending, disabled local operator.
///
/// A Platform Owner receives the high-entropy raw code once. The database
/// stores only its SHA-256 digest. Entering the code consumes its public use
/// immediately and establishes a narrowly scoped, short-lived enrolment cookie
/// that can only prepare password/TOTP/recovery-code setup for that exact
/// pending operator.
/// </summary>
public sealed class MemOperatorEnrollmentService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    IMemOperatorAuditService audit,
    IOptions<MemOperatorIdentityOptions> options,
    TimeProvider timeProvider) : IMemOperatorEnrollmentService
{
    private readonly MemOperatorIdentityOptions _options = options.Value;

    public async Task<IssueMemOperatorEnrollmentGrantResponse> IssueAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        EnsureNotSelf(actorOperatorId, subjectOperatorId);

        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await ExpireStaleGrantsAsync(now, ct);

        var subject = await GetSubjectOrThrowAsync(subjectOperatorId, ct);
        var roles = await GetRolesAsync(subject, ct);

        EnsurePendingSubjectIsEligible(subject, roles);

        var liveGrants = await db.MemOperatorEnrollmentGrants
            .Where(grant =>
                grant.OperatorId == subject.Id &&
                (grant.Status == MemOperatorEnrollmentGrantStatuses.Active ||
                 grant.Status == MemOperatorEnrollmentGrantStatuses.Claimed ||
                 grant.Status == MemOperatorEnrollmentGrantStatuses.Prepared ||
                 grant.Status == MemOperatorEnrollmentGrantStatuses.TotpVerified))
            .ToArrayAsync(ct);

        foreach (var existing in liveGrants)
        {
            existing.Status = MemOperatorEnrollmentGrantStatuses.Cancelled;
            existing.CancelledAtUtc = now;

            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.operator.enrollment_grant_cancelled",
                    Outcome: "succeeded",
                    ActorOperatorId: actorOperatorId,
                    SubjectOperatorId: subject.Id,
                    CorrelationId: existing.Id.ToString("D"),
                    ReasonCode: "reissued"),
                ct);
        }

        await ResetPartialEnrollmentAsync(subject, ct);

        var rawCode = CreateEnrollmentCode();

        var grant = new MemOperatorEnrollmentGrantEntity
        {
            Id = Guid.NewGuid(),
            OperatorId = subject.Id,
            CodeHash = ComputeCodeHash(rawCode),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.EnrollmentGrantMinutes),
            Status = MemOperatorEnrollmentGrantStatuses.Active
        };

        await db.MemOperatorEnrollmentGrants.AddAsync(grant, ct);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.enrollment_grant_issued",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: subject.Id,
                CorrelationId: correlationId ?? grant.Id.ToString("D"),
                ReasonCode: "pending_operator"),
            ct);

        await transaction.CommitAsync(ct);

        return new IssueMemOperatorEnrollmentGrantResponse(
            OperatorId: subject.Id,
            Username: subject.UserName ?? subject.Id.ToString("D"),
            EnrollmentCode: rawCode,
            ExpiresAtUtc: grant.ExpiresAtUtc);
    }

    public async Task<MemOperatorEnrollmentStateResponse> GetCurrentStateAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);
        var subject = await GetSubjectOrThrowAsync(grant.OperatorId, ct);

        return new MemOperatorEnrollmentStateResponse(
            Active: true,
            Username: subject.UserName ?? subject.Id.ToString("D"),
            Stage: GetStage(grant.Status),
            ExpiresAtUtc: grant.ExpiresAtUtc);
    }

    public async Task<BeginMemOperatorEnrollmentResponse> BeginAsync(
        string? enrollmentCode,
        CancellationToken ct = default)
    {
        var normalizedCode = RequireEnrollmentCode(enrollmentCode);
        var codeHash = ComputeCodeHash(normalizedCode);
        var now = timeProvider.GetUtcNow();

        await ExpireStaleGrantsAsync(now, ct);

        var grant = await db.MemOperatorEnrollmentGrants
            .SingleOrDefaultAsync(item =>
                item.CodeHash == codeHash &&
                item.Status == MemOperatorEnrollmentGrantStatuses.Active,
                ct);

        if (grant is null || grant.ExpiresAtUtc <= now)
        {
            if (grant is not null && grant.Status == MemOperatorEnrollmentGrantStatuses.Active)
            {
                grant.Status = MemOperatorEnrollmentGrantStatuses.Expired;
                await db.SaveChangesAsync(ct);
            }

            throw new OperatorEnrollmentException(
                "enrollment_code_invalid",
                OperatorEnrollmentFailureKind.Conflict);
        }

        var subject = await GetSubjectOrThrowAsync(grant.OperatorId, ct);
        var roles = await GetRolesAsync(subject, ct);

        try
        {
            EnsurePendingSubjectIsEligible(subject, roles);
        }
        catch (OperatorEnrollmentException)
        {
            grant.Status = MemOperatorEnrollmentGrantStatuses.Cancelled;
            grant.CancelledAtUtc = now;
            await db.SaveChangesAsync(ct);

            throw new OperatorEnrollmentException(
                "enrollment_code_invalid",
                OperatorEnrollmentFailureKind.Conflict);
        }

        grant.Status = MemOperatorEnrollmentGrantStatuses.Claimed;
        grant.ClaimedAtUtc = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new OperatorEnrollmentException(
                "enrollment_code_invalid",
                OperatorEnrollmentFailureKind.Conflict);
        }

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.enrollment_started",
                Outcome: "succeeded",
                SubjectOperatorId: subject.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "code_claimed"),
            ct);

        return new BeginMemOperatorEnrollmentResponse(
            GrantId: grant.Id,
            State: new MemOperatorEnrollmentStateResponse(
                Active: true,
                Username: subject.UserName ?? subject.Id.ToString("D"),
                Stage: GetStage(grant.Status),
                ExpiresAtUtc: grant.ExpiresAtUtc));
    }

    public async Task<PrepareMemOperatorEnrollmentResponse> PrepareAsync(
        Guid grantId,
        PrepareMemOperatorEnrollmentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);
        var subject = await GetSubjectOrThrowAsync(grant.OperatorId, ct);

        if (grant.Status == MemOperatorEnrollmentGrantStatuses.Prepared)
        {
            return await BuildAuthenticatorResponseAsync(subject, ct);
        }

        if (grant.Status != MemOperatorEnrollmentGrantStatuses.Claimed)
        {
            throw new OperatorEnrollmentException(
                "enrollment_password_stage_required",
                OperatorEnrollmentFailureKind.Conflict);
        }

        if (!string.IsNullOrWhiteSpace(subject.PasswordHash) || subject.TwoFactorEnabled)
        {
            throw new OperatorEnrollmentException(
                "enrollment_account_not_pending",
                OperatorEnrollmentFailureKind.Conflict);
        }

        var password = RequirePassword(request.Password);

        var passwordSet = await userManager.AddPasswordAsync(subject, password);
        if (!passwordSet.Succeeded)
        {
            throw ToPasswordException(passwordSet);
        }

        var keyReset = await userManager.ResetAuthenticatorKeyAsync(subject);
        if (!keyReset.Succeeded)
        {
            await userManager.RemovePasswordAsync(subject);

            throw new OperatorEnrollmentException(
                "enrollment_authenticator_unavailable",
                OperatorEnrollmentFailureKind.Unavailable);
        }

        grant.Status = MemOperatorEnrollmentGrantStatuses.Prepared;
        grant.PreparedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.enrollment_password_prepared",
                Outcome: "succeeded",
                SubjectOperatorId: subject.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "totp_required"),
            ct);

        await transaction.CommitAsync(ct);

        return await BuildAuthenticatorResponseAsync(subject, ct);
    }

    public async Task VerifyTotpAsync(
        Guid grantId,
        string? code,
        CancellationToken ct = default)
    {
        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);

        if (grant.Status == MemOperatorEnrollmentGrantStatuses.TotpVerified)
        {
            return;
        }

        if (grant.Status != MemOperatorEnrollmentGrantStatuses.Prepared)
        {
            throw new OperatorEnrollmentException(
                "enrollment_authenticator_stage_required",
                OperatorEnrollmentFailureKind.Conflict);
        }

        var subject = await GetSubjectOrThrowAsync(grant.OperatorId, ct);
        var normalizedCode = NormalizeTotpCode(code);

        var isValid = await userManager.VerifyTwoFactorTokenAsync(
            subject,
            TokenOptions.DefaultAuthenticatorProvider,
            normalizedCode);

        if (!isValid)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.operator.enrollment_totp_verified",
                    Outcome: "failed",
                    SubjectOperatorId: subject.Id,
                    CorrelationId: grant.Id.ToString("D"),
                    ReasonCode: "invalid_totp"),
                ct);

            throw new OperatorEnrollmentException(
                "invalid_totp",
                OperatorEnrollmentFailureKind.Validation);
        }

        grant.Status = MemOperatorEnrollmentGrantStatuses.TotpVerified;
        grant.TotpVerifiedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.enrollment_totp_verified",
                Outcome: "succeeded",
                SubjectOperatorId: subject.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "pending_operator"),
            ct);
    }

    public async Task<CompleteMemOperatorEnrollmentResponse> CompleteAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var grant = await GetCurrentGrantOrThrowAsync(grantId, ct);

        if (grant.Status != MemOperatorEnrollmentGrantStatuses.TotpVerified ||
            grant.TotpVerifiedAtUtc is null)
        {
            throw new OperatorEnrollmentException(
                "enrollment_totp_verification_required",
                OperatorEnrollmentFailureKind.Conflict);
        }

        var subject = await GetSubjectOrThrowAsync(grant.OperatorId, ct);
        var roles = await GetRolesAsync(subject, ct);

        EnsurePendingSubjectIsEligible(subject, roles);

        if (string.IsNullOrWhiteSpace(subject.PasswordHash))
        {
            throw new OperatorEnrollmentException(
                "enrollment_password_required",
                OperatorEnrollmentFailureKind.Conflict);
        }

        var twoFactorEnabled = await userManager.SetTwoFactorEnabledAsync(subject, true);
        if (!twoFactorEnabled.Succeeded)
        {
            throw new OperatorEnrollmentException(
                "enrollment_authenticator_unavailable",
                OperatorEnrollmentFailureKind.Unavailable);
        }

        var recoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                subject,
                _options.EnrollmentRecoveryCodeCount))
            .ToArray();

        if (recoveryCodes.Length != _options.EnrollmentRecoveryCodeCount)
        {
            throw new OperatorEnrollmentException(
                "enrollment_recovery_codes_unavailable",
                OperatorEnrollmentFailureKind.Unavailable);
        }

        subject.IsEnabled = true;

        var updated = await userManager.UpdateAsync(subject);
        if (!updated.Succeeded)
        {
            throw new OperatorEnrollmentException(
                "enrollment_completion_unavailable",
                OperatorEnrollmentFailureKind.Unavailable);
        }

        grant.Status = MemOperatorEnrollmentGrantStatuses.Completed;
        grant.ConsumedAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.enrollment_completed",
                Outcome: "succeeded",
                ActorOperatorId: subject.Id,
                SubjectOperatorId: subject.Id,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "password_totp_recovery_codes"),
            ct);

        await transaction.CommitAsync(ct);

        return new CompleteMemOperatorEnrollmentResponse(
            Username: subject.UserName ?? subject.Id.ToString("D"),
            RecoveryCodes: recoveryCodes);
    }

    public async Task CancelAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        var grant = await db.MemOperatorEnrollmentGrants
            .SingleOrDefaultAsync(item => item.Id == grantId, ct);

        if (grant is null || !MemOperatorEnrollmentGrantStatuses.IsClaimedAndCurrent(grant.Status))
        {
            return;
        }

        var subject = await userManager.FindByIdAsync(grant.OperatorId.ToString("D"));

        if (subject is { IsEnabled: false, IsBootstrapProvisioning: false })
        {
            await ResetPartialEnrollmentAsync(subject, ct);
        }

        grant.Status = MemOperatorEnrollmentGrantStatuses.Cancelled;
        grant.CancelledAtUtc = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.enrollment_grant_cancelled",
                Outcome: "succeeded",
                SubjectOperatorId: grant.OperatorId,
                CorrelationId: grant.Id.ToString("D"),
                ReasonCode: "enrollee_cancelled"),
            ct);
    }

    public async Task<bool> IsGrantCurrentAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();

        var grant = await db.MemOperatorEnrollmentGrants
            .SingleOrDefaultAsync(item => item.Id == grantId, ct);

        if (grant is null ||
            !MemOperatorEnrollmentGrantStatuses.IsClaimedAndCurrent(grant.Status) ||
            grant.ExpiresAtUtc <= now)
        {
            if (grant is not null &&
                MemOperatorEnrollmentGrantStatuses.IsClaimedAndCurrent(grant.Status) &&
                grant.ExpiresAtUtc <= now)
            {
                grant.Status = MemOperatorEnrollmentGrantStatuses.Expired;
                await db.SaveChangesAsync(ct);
            }

            return false;
        }

        return true;
    }

    private async Task<MemOperatorEnrollmentGrantEntity> GetCurrentGrantOrThrowAsync(
        Guid grantId,
        CancellationToken ct)
    {
        if (!await IsGrantCurrentAsync(grantId, ct))
        {
            throw new OperatorEnrollmentException(
                "enrollment_grant_unavailable",
                OperatorEnrollmentFailureKind.Conflict);
        }

        return await db.MemOperatorEnrollmentGrants
            .SingleAsync(item => item.Id == grantId, ct);
    }

    private async Task<MemOperator> GetSubjectOrThrowAsync(
        Guid operatorId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await userManager.FindByIdAsync(operatorId.ToString("D"))
            ?? throw new OperatorEnrollmentException(
                "operator_not_found",
                OperatorEnrollmentFailureKind.NotFound);
    }

    private async Task<IReadOnlyList<string>> GetRolesAsync(
        MemOperator subject,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var roles = await userManager.GetRolesAsync(subject);

        return roles
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<PrepareMemOperatorEnrollmentResponse> BuildAuthenticatorResponseAsync(
        MemOperator subject,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = await userManager.GetAuthenticatorKeyAsync(subject);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new OperatorEnrollmentException(
                "enrollment_authenticator_unavailable",
                OperatorEnrollmentFailureKind.Unavailable);
        }

        var username = subject.UserName ?? subject.Id.ToString("D");

        return new PrepareMemOperatorEnrollmentResponse(
            Username: username,
            ManualEntryKey: key,
            AuthenticatorUri: BuildAuthenticatorUri(username, key));
    }

    private async Task ResetPartialEnrollmentAsync(
        MemOperator subject,
        CancellationToken ct)
    {
        if (subject.IsEnabled || subject.IsBootstrapProvisioning)
        {
            throw new OperatorEnrollmentException(
                "enrollment_account_not_pending",
                OperatorEnrollmentFailureKind.Conflict);
        }

        var changed = false;

        if (!string.IsNullOrWhiteSpace(subject.PasswordHash))
        {
            var passwordRemoved = await userManager.RemovePasswordAsync(subject);
            if (!passwordRemoved.Succeeded)
            {
                throw new OperatorEnrollmentException(
                    "enrollment_reset_failed",
                    OperatorEnrollmentFailureKind.Unavailable);
            }

            changed = true;
        }

        if (subject.TwoFactorEnabled)
        {
            var twoFactorDisabled = await userManager.SetTwoFactorEnabledAsync(subject, false);
            if (!twoFactorDisabled.Succeeded)
            {
                throw new OperatorEnrollmentException(
                    "enrollment_reset_failed",
                    OperatorEnrollmentFailureKind.Unavailable);
            }

            changed = true;
        }

        if (!changed)
        {
            return;
        }

        var keyReset = await userManager.ResetAuthenticatorKeyAsync(subject);
        if (!keyReset.Succeeded)
        {
            throw new OperatorEnrollmentException(
                "enrollment_reset_failed",
                OperatorEnrollmentFailureKind.Unavailable);
        }

        var stampUpdated = await userManager.UpdateSecurityStampAsync(subject);
        if (!stampUpdated.Succeeded)
        {
            throw new OperatorEnrollmentException(
                "enrollment_reset_failed",
                OperatorEnrollmentFailureKind.Unavailable);
        }
    }

    private async Task ExpireStaleGrantsAsync(
        DateTimeOffset now,
        CancellationToken ct)
    {
        // SQLite EF Core cannot translate DateTimeOffset comparisons. Grant
        // records are short-lived and intentionally few, so filter status in SQL
        // and evaluate expiry after materialisation.
        var liveGrants = await db.MemOperatorEnrollmentGrants
            .Where(grant =>
                grant.Status == MemOperatorEnrollmentGrantStatuses.Active ||
                grant.Status == MemOperatorEnrollmentGrantStatuses.Claimed ||
                grant.Status == MemOperatorEnrollmentGrantStatuses.Prepared ||
                grant.Status == MemOperatorEnrollmentGrantStatuses.TotpVerified)
            .ToArrayAsync(ct);

        var stale = liveGrants
            .Where(grant => grant.ExpiresAtUtc <= now)
            .ToArray();

        if (stale.Length == 0)
        {
            return;
        }

        foreach (var grant in stale)
        {
            grant.Status = MemOperatorEnrollmentGrantStatuses.Expired;
        }

        await db.SaveChangesAsync(ct);
    }

    private static void EnsurePendingSubjectIsEligible(
        MemOperator subject,
        IReadOnlyList<string> roles)
    {
        if (subject.IsBootstrapProvisioning ||
            subject.IsEnabled ||
            roles.Count == 0)
        {
            throw new OperatorEnrollmentException(
                "enrollment_account_not_pending",
                OperatorEnrollmentFailureKind.Conflict);
        }
    }

    private static void EnsureNotSelf(Guid actorOperatorId, Guid subjectOperatorId)
    {
        if (actorOperatorId == subjectOperatorId)
        {
            throw new OperatorEnrollmentException(
                "operator_self_management_not_allowed",
                OperatorEnrollmentFailureKind.Conflict);
        }
    }

    private static string RequireEnrollmentCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OperatorEnrollmentException(
                "enrollment_code_required",
                OperatorEnrollmentFailureKind.Validation);
        }

        var code = value.Trim();

        if (code.Length is < 32 or > 200)
        {
            throw new OperatorEnrollmentException(
                "enrollment_code_invalid",
                OperatorEnrollmentFailureKind.Conflict);
        }

        return code;
    }

    private static string RequirePassword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OperatorEnrollmentException(
                "password_required",
                OperatorEnrollmentFailureKind.Validation);
        }

        return value;
    }

    private static string NormalizeTotpCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OperatorEnrollmentException(
                "totp_required",
                OperatorEnrollmentFailureKind.Validation);
        }

        var code = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);

        if (code.Length is < 6 or > 12 || !code.All(char.IsAsciiDigit))
        {
            throw new OperatorEnrollmentException(
                "invalid_totp",
                OperatorEnrollmentFailureKind.Validation);
        }

        return code;
    }

    private static OperatorEnrollmentException ToPasswordException(IdentityResult result)
    {
        return new OperatorEnrollmentException(
            "password_not_accepted",
            OperatorEnrollmentFailureKind.Validation);
    }

    private static string CreateEnrollmentCode()
    {
        return $"mem_enrol_{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
    }

    private static string ComputeCodeHash(string code)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
    }

    private static string GetStage(string status)
    {
        return status switch
        {
            MemOperatorEnrollmentGrantStatuses.Claimed => "password",
            MemOperatorEnrollmentGrantStatuses.Prepared => "totp",
            MemOperatorEnrollmentGrantStatuses.TotpVerified => "recovery",
            _ => throw new OperatorEnrollmentException(
                "enrollment_grant_unavailable",
                OperatorEnrollmentFailureKind.Conflict)
        };
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
