using System.Security.Claims;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

public sealed record MemOperatorStepUpGrant(
    DateTimeOffset ExpiresAtUtc);

public interface IMemOperatorStepUpService
{
    Task<MemOperatorStepUpGrant> IssueAsync(
        MemOperator subject,
        ClaimsPrincipal currentPrincipal,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemOperatorStepUpGrant> IssueForSessionAsync(
        MemOperator subject,
        Guid sessionId,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<bool> HasActiveGrantAsync(
        ClaimsPrincipal currentPrincipal,
        CancellationToken ct = default);

    Task RevokeCurrentSessionAsync(
        ClaimsPrincipal currentPrincipal,
        string reasonCode,
        string? correlationId = null,
        CancellationToken ct = default);

    Task RevokeAllForOperatorAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        string reasonCode,
        string? correlationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Issues and validates short-lived recent-authentication grants. A grant is
/// not a credential: it is valid only while the current protected Identity
/// cookie carries the matching session id, the account remains enabled, and
/// the persisted Identity security stamp has not changed.
/// </summary>
public sealed class MemOperatorStepUpService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    IMemOperatorAuditService audit,
    IOptions<MemOperatorIdentityOptions> options,
    IMemSecuritySettingsService securitySettings,
    TimeProvider timeProvider) : IMemOperatorStepUpService
{
    private readonly int _fallbackGrantMinutes = options.Value.StepUpGrantMinutes;

    public async Task<MemOperatorStepUpGrant> IssueAsync(
        MemOperator subject,
        ClaimsPrincipal currentPrincipal,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(currentPrincipal);

        if (!MemOperatorSessionClaims.TryGetSessionId(currentPrincipal, out var sessionId))
        {
            throw new InvalidOperationException("Current operator session binding is unavailable.");
        }

        return await IssueCoreAsync(subject, sessionId, correlationId, ct);
    }

    public async Task<MemOperatorStepUpGrant> IssueForSessionAsync(
        MemOperator subject,
        Guid sessionId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        if (sessionId == Guid.Empty)
        {
            throw new InvalidOperationException("Current operator session binding is unavailable.");
        }

        return await IssueCoreAsync(subject, sessionId, correlationId, ct);
    }

    private async Task<MemOperatorStepUpGrant> IssueCoreAsync(
        MemOperator subject,
        Guid sessionId,
        string? correlationId,
        CancellationToken ct)
    {
        var securityStamp = RequireSecurityStamp(subject);
        var now = timeProvider.GetUtcNow();
        var settings = await securitySettings.GetEffectiveAsync(ct);
        var grantMinutes = MemSecuritySettingsDefaults.IsAllowedHighRiskStepUpGrantMinutes(
            settings.HighRiskStepUpGrantMinutes)
            ? settings.HighRiskStepUpGrantMinutes
            : _fallbackGrantMinutes;
        var expiresAtUtc = now.AddMinutes(grantMinutes);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var grant = await db.MemOperatorStepUpGrants.SingleOrDefaultAsync(
            entry => entry.OperatorId == subject.Id && entry.SessionId == sessionId,
            ct);

        if (grant is null)
        {
            grant = new MemOperatorStepUpGrantEntity
            {
                Id = Guid.NewGuid(),
                OperatorId = subject.Id,
                SessionId = sessionId
            };

            await db.MemOperatorStepUpGrants.AddAsync(grant, ct);
        }

        grant.SecurityStamp = securityStamp;
        grant.IssuedAtUtc = now;
        grant.ExpiresAtUtc = expiresAtUtc;
        grant.RevokedAtUtc = null;
        grant.RevokedReasonCode = null;

        subject.LastStepUpAtUtc = now;

        var updated = await userManager.UpdateAsync(subject);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("Current operator step-up state could not be recorded.");
        }

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.step-up.succeeded",
                Outcome: "succeeded",
                ActorOperatorId: subject.Id,
                SubjectOperatorId: subject.Id,
                CorrelationId: correlationId,
                ReasonCode: "password_and_totp"),
            ct);

        await transaction.CommitAsync(ct);

        return new MemOperatorStepUpGrant(expiresAtUtc);
    }

    public async Task<bool> HasActiveGrantAsync(
        ClaimsPrincipal currentPrincipal,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currentPrincipal);

        if (!MemOperatorSessionClaims.TryGetSessionId(currentPrincipal, out var sessionId))
        {
            return false;
        }

        var operatorIdText = userManager.GetUserId(currentPrincipal);
        if (!Guid.TryParse(operatorIdText, out var operatorId))
        {
            return false;
        }

        var user = await userManager.FindByIdAsync(operatorId.ToString("D"));
        if (user is not { IsEnabled: true, IsBootstrapProvisioning: false, TwoFactorEnabled: true } ||
            await userManager.IsLockedOutAsync(user))
        {
            return false;
        }

        var grant = await db.MemOperatorStepUpGrants.SingleOrDefaultAsync(
            entry => entry.OperatorId == operatorId && entry.SessionId == sessionId,
            ct);

        if (grant is not null)
        {
            // Step-up grants may be revoked by a settings update in another request
            // scope. Refresh before making an authorization decision so a long-lived
            // test scope or caller cannot keep accepting a stale tracked entity.
            await db.Entry(grant).ReloadAsync(ct);
        }

        if (grant is null || grant.RevokedAtUtc is not null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();
        var currentSecurityStamp = RequireSecurityStamp(user);
        var settings = await securitySettings.GetEffectiveAsync(ct);

        if (settings.RequireHighRiskStepUp &&
            settings.UpdatedAtUtc is { } policyUpdatedAtUtc &&
            grant.IssuedAtUtc < policyUpdatedAtUtc)
        {
            await RevokeGrantAsync(
                grant,
                actorOperatorId: operatorId,
                reasonCode: "security_settings_changed",
                correlationId: null,
                occurredAtUtc: now,
                ct);

            return false;
        }

        var effectiveExpiresAtUtc = GetEffectiveGrantExpiry(grant, settings);

        if (effectiveExpiresAtUtc <= now)
        {
            await RevokeGrantAsync(
                grant,
                actorOperatorId: operatorId,
                reasonCode: "expired",
                correlationId: null,
                occurredAtUtc: now,
                ct);

            return false;
        }

        if (!string.Equals(
                grant.SecurityStamp,
                currentSecurityStamp,
                StringComparison.Ordinal))
        {
            await RevokeGrantAsync(
                grant,
                actorOperatorId: operatorId,
                reasonCode: "security_stamp_changed",
                correlationId: null,
                occurredAtUtc: now,
                ct);

            return false;
        }

        return true;
    }


    private DateTimeOffset GetEffectiveGrantExpiry(
        MemOperatorStepUpGrantEntity grant,
        MemSecuritySettingsSnapshot settings)
    {
        var currentWindowMinutes = MemSecuritySettingsDefaults.IsAllowedHighRiskStepUpGrantMinutes(
            settings.HighRiskStepUpGrantMinutes)
            ? settings.HighRiskStepUpGrantMinutes
            : _fallbackGrantMinutes;

        var currentPolicyExpiry = grant.IssuedAtUtc.AddMinutes(currentWindowMinutes);

        return grant.ExpiresAtUtc <= currentPolicyExpiry
            ? grant.ExpiresAtUtc
            : currentPolicyExpiry;
    }

    public async Task RevokeCurrentSessionAsync(
        ClaimsPrincipal currentPrincipal,
        string reasonCode,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(currentPrincipal);

        if (!MemOperatorSessionClaims.TryGetSessionId(currentPrincipal, out var sessionId))
        {
            return;
        }

        var operatorIdText = userManager.GetUserId(currentPrincipal);
        if (!Guid.TryParse(operatorIdText, out var operatorId))
        {
            return;
        }

        var grant = await db.MemOperatorStepUpGrants.SingleOrDefaultAsync(
            entry => entry.OperatorId == operatorId && entry.SessionId == sessionId,
            ct);

        if (grant is null || grant.RevokedAtUtc is not null)
        {
            return;
        }

        await RevokeGrantAsync(
            grant,
            actorOperatorId: operatorId,
            reasonCode: RequireReasonCode(reasonCode),
            correlationId: correlationId,
            occurredAtUtc: timeProvider.GetUtcNow(),
            ct);
    }

    public async Task RevokeAllForOperatorAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        string reasonCode,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var normalizedReasonCode = RequireReasonCode(reasonCode);
        var grants = await db.MemOperatorStepUpGrants
            .Where(entry => entry.OperatorId == subjectOperatorId && entry.RevokedAtUtc == null)
            .ToArrayAsync(ct);

        if (grants.Length == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();

        foreach (var grant in grants)
        {
            grant.RevokedAtUtc = now;
            grant.RevokedReasonCode = normalizedReasonCode;
        }

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.step-up.revoked",
                Outcome: "revoked",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: subjectOperatorId,
                CorrelationId: correlationId,
                ReasonCode: normalizedReasonCode),
            ct);
    }

    private async Task RevokeGrantAsync(
        MemOperatorStepUpGrantEntity grant,
        Guid actorOperatorId,
        string reasonCode,
        string? correlationId,
        DateTimeOffset occurredAtUtc,
        CancellationToken ct)
    {
        grant.RevokedAtUtc = occurredAtUtc;
        grant.RevokedReasonCode = reasonCode;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: reasonCode == "expired"
                    ? "identity.step-up.expired"
                    : "identity.step-up.revoked",
                Outcome: reasonCode == "expired" ? "expired" : "revoked",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: grant.OperatorId,
                CorrelationId: correlationId,
                ReasonCode: reasonCode),
            ct);
    }

    private static string RequireSecurityStamp(MemOperator subject)
    {
        return string.IsNullOrWhiteSpace(subject.SecurityStamp)
            ? throw new InvalidOperationException("Current operator security stamp is unavailable.")
            : subject.SecurityStamp;
    }

    private static string RequireReasonCode(string reasonCode)
    {
        return string.IsNullOrWhiteSpace(reasonCode) || reasonCode.Length > 100
            ? throw new ArgumentException("A safe step-up revocation reason is required.", nameof(reasonCode))
            : reasonCode;
    }
}
