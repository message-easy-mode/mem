using System.Security.Cryptography;
using System.Text;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;
using Modules.Auth.Contracts;

namespace Modules.Auth.Services.Identity;

public interface IMemCliDeviceAuthorizationService
{
    Task<MemCliDeviceAuthorizationStarted> StartAsync(
        Guid installationId,
        string verifierChallenge,
        string? deviceLabel,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemCliDeviceAuthorizationReview> ReviewAsync(
        string userCode,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemCliDeviceAuthorizationDecision> ApproveAsync(
        string userCode,
        Guid approvingOperatorId,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemCliDeviceAuthorizationDecision> DenyAsync(
        string userCode,
        Guid denyingOperatorId,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemCliDeviceAuthorizationPollResult> PollAsync(
        Guid authorizationId,
        string verifier,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemCliDeviceSessionValidationResult> ValidateAsync(
        Guid installationId,
        string deviceCredential,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemCliDeviceSessionRevocationResult> RevokeSessionAsync(
        Guid sessionId,
        string? correlationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Server-side lifecycle for browser-approved MEM CLI device sessions.
///
/// HTTP routes must derive the current installation binding server-side, apply
/// anonymous polling limits, require a named MFA-completed browser session for
/// review and denial, and require a current session-bound step-up grant before
/// approval. Only safe contract records may reach the browser.
/// </summary>
public sealed class MemCliDeviceAuthorizationService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    IMemOperatorAuditService audit,
    IOptions<MemCliDeviceSessionOptions> options,
    TimeProvider timeProvider) : IMemCliDeviceAuthorizationService
{
    private const int DigestLengthBytes = 32;
    private const int DeviceLabelMaxLength = 100;
    private const string DefaultDeviceLabel = "MEM CLI";
    private const string UserCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<MemCliDeviceAuthorizationStarted> StartAsync(
        Guid installationId,
        string verifierChallenge,
        string? deviceLabel,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        if (installationId == Guid.Empty)
        {
            throw new ArgumentException("An installation id is required.", nameof(installationId));
        }

        var normalizedChallenge = NormalizeDigest(verifierChallenge, nameof(verifierChallenge));
        var normalizedLabel = NormalizeDeviceLabel(deviceLabel);
        var now = timeProvider.GetUtcNow();
        var userCode = CreateUserCode();

        var attempt = new MemCliDeviceAuthorizationAttemptEntity
        {
            Id = Guid.NewGuid(),
            InstallationId = installationId,
            UserCodeHash = Sha256Hex(NormalizeUserCode(userCode)),
            VerifierHash = normalizedChallenge,
            DeviceLabel = normalizedLabel,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(options.Value.AuthorizationAttemptMinutes),
            Status = MemCliDeviceAuthorizationAttemptStatuses.Pending
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.MemCliDeviceAuthorizationAttempts.AddAsync(attempt, ct);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.cli-device.authorization.started",
                Outcome: "started",
                CorrelationId: correlationId,
                ReasonCode: "device_authorization_requested"),
            ct);

        await transaction.CommitAsync(ct);

        return new MemCliDeviceAuthorizationStarted(
            attempt.Id,
            userCode,
            attempt.ExpiresAtUtc);
    }

    /// <summary>
    /// Returns only the details an authenticated browser needs to let an
    /// operator review a pending device request. Terminal and unknown states
    /// intentionally disclose no device label, credential, verifier,
    /// installation binding, or operator information.
    /// </summary>
    public async Task<MemCliDeviceAuthorizationReview> ReviewAsync(
        string userCode,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var attempt = await FindAttemptByUserCodeAsync(userCode, ct);
        if (attempt is null)
        {
            return new MemCliDeviceAuthorizationReview("authorization_unavailable");
        }

        var now = timeProvider.GetUtcNow();
        if (await ExpireIfNecessaryAsync(attempt, now, correlationId, ct))
        {
            return new MemCliDeviceAuthorizationReview(
                "authorization_expired",
                ExpiresAtUtc: attempt.ExpiresAtUtc);
        }

        if (attempt.Status == MemCliDeviceAuthorizationAttemptStatuses.Pending)
        {
            return new MemCliDeviceAuthorizationReview(
                "authorization_pending",
                attempt.DeviceLabel,
                attempt.ExpiresAtUtc);
        }

        return new MemCliDeviceAuthorizationReview(
            ToDecisionStatus(attempt.Status),
            ExpiresAtUtc: attempt.ExpiresAtUtc);
    }

    public async Task<MemCliDeviceAuthorizationDecision> ApproveAsync(
        string userCode,
        Guid approvingOperatorId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var attempt = await FindAttemptByUserCodeAsync(userCode, ct);
        if (attempt is null)
        {
            return new MemCliDeviceAuthorizationDecision("authorization_unavailable");
        }

        var now = timeProvider.GetUtcNow();
        if (await ExpireIfNecessaryAsync(attempt, now, correlationId, ct))
        {
            return new MemCliDeviceAuthorizationDecision(
                "authorization_expired",
                attempt.ExpiresAtUtc);
        }

        if (attempt.Status != MemCliDeviceAuthorizationAttemptStatuses.Pending)
        {
            return new MemCliDeviceAuthorizationDecision(
                ToDecisionStatus(attempt.Status),
                attempt.ExpiresAtUtc);
        }

        var approver = await userManager.FindByIdAsync(approvingOperatorId.ToString("D"));
        if (!await IsEligibleApproverAsync(approver))
        {
            return new MemCliDeviceAuthorizationDecision("approval_unavailable");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        attempt.Status = MemCliDeviceAuthorizationAttemptStatuses.Approved;
        attempt.ApprovedAtUtc = now;
        attempt.ApprovedByOperatorId = approver!.Id;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.cli-device.authorization.approved",
                Outcome: "approved",
                ActorOperatorId: approver.Id,
                SubjectOperatorId: approver.Id,
                CorrelationId: correlationId,
                ReasonCode: "browser_operator_approval"),
            ct);

        await transaction.CommitAsync(ct);

        return new MemCliDeviceAuthorizationDecision(
            "authorization_approved",
            attempt.ExpiresAtUtc);
    }

    public async Task<MemCliDeviceAuthorizationDecision> DenyAsync(
        string userCode,
        Guid denyingOperatorId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var attempt = await FindAttemptByUserCodeAsync(userCode, ct);
        if (attempt is null)
        {
            return new MemCliDeviceAuthorizationDecision("authorization_unavailable");
        }

        var now = timeProvider.GetUtcNow();
        if (await ExpireIfNecessaryAsync(attempt, now, correlationId, ct))
        {
            return new MemCliDeviceAuthorizationDecision(
                "authorization_expired",
                attempt.ExpiresAtUtc);
        }

        if (attempt.Status != MemCliDeviceAuthorizationAttemptStatuses.Pending)
        {
            return new MemCliDeviceAuthorizationDecision(
                ToDecisionStatus(attempt.Status),
                attempt.ExpiresAtUtc);
        }

        var approver = await userManager.FindByIdAsync(denyingOperatorId.ToString("D"));
        if (!await IsEligibleApproverAsync(approver))
        {
            return new MemCliDeviceAuthorizationDecision("approval_unavailable");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        attempt.Status = MemCliDeviceAuthorizationAttemptStatuses.Denied;
        attempt.DeniedAtUtc = now;
        attempt.DeniedByOperatorId = approver!.Id;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.cli-device.authorization.denied",
                Outcome: "denied",
                ActorOperatorId: approver.Id,
                SubjectOperatorId: approver.Id,
                CorrelationId: correlationId,
                ReasonCode: "browser_operator_denial"),
            ct);

        await transaction.CommitAsync(ct);

        return new MemCliDeviceAuthorizationDecision(
            "authorization_denied",
            attempt.ExpiresAtUtc);
    }

    public async Task<MemCliDeviceAuthorizationPollResult> PollAsync(
        Guid authorizationId,
        string verifier,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var attempt = await db.MemCliDeviceAuthorizationAttempts
            .SingleOrDefaultAsync(entry => entry.Id == authorizationId, ct);

        if (attempt is null ||
            !VerifierMatches(attempt.VerifierHash, verifier))
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.cli-device.login.failed",
                    Outcome: "failed",
                    CorrelationId: correlationId,
                    ReasonCode: "invalid_device_verifier"),
                ct);

            return new MemCliDeviceAuthorizationPollResult("authorization_invalid");
        }

        var now = timeProvider.GetUtcNow();
        if (await ExpireIfNecessaryAsync(attempt, now, correlationId, ct))
        {
            return new MemCliDeviceAuthorizationPollResult("authorization_expired");
        }

        if (attempt.Status == MemCliDeviceAuthorizationAttemptStatuses.Pending)
        {
            return new MemCliDeviceAuthorizationPollResult("authorization_pending");
        }

        if (attempt.Status == MemCliDeviceAuthorizationAttemptStatuses.Denied)
        {
            return new MemCliDeviceAuthorizationPollResult("authorization_denied");
        }

        if (attempt.Status == MemCliDeviceAuthorizationAttemptStatuses.Consumed)
        {
            return new MemCliDeviceAuthorizationPollResult("authorization_consumed");
        }

        if (attempt.Status != MemCliDeviceAuthorizationAttemptStatuses.Approved ||
            attempt.ApprovedByOperatorId is not { } approvedOperatorId)
        {
            return new MemCliDeviceAuthorizationPollResult("authorization_unavailable");
        }

        var approvedOperator = await userManager.FindByIdAsync(approvedOperatorId.ToString("D"));
        if (!await IsEligibleApproverAsync(approvedOperator))
        {
            return new MemCliDeviceAuthorizationPollResult("authorization_unavailable");
        }

        var rawCredential = CreateOpaqueValue();
        var absoluteExpiresAtUtc = now.AddHours(options.Value.SessionAbsoluteHours);
        var idleExpiresAtUtc = Min(
            now.AddMinutes(options.Value.SessionIdleMinutes),
            absoluteExpiresAtUtc);

        var session = new MemCliDeviceSessionEntity
        {
            Id = Guid.NewGuid(),
            AuthorizationAttemptId = attempt.Id,
            InstallationId = attempt.InstallationId,
            OperatorId = approvedOperator!.Id,
            CredentialHash = Sha256Hex(rawCredential),
            SecurityStamp = RequireSecurityStamp(approvedOperator),
            DeviceLabel = attempt.DeviceLabel,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
            IdleExpiresAtUtc = idleExpiresAtUtc,
            AbsoluteExpiresAtUtc = absoluteExpiresAtUtc
        };

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        attempt.Status = MemCliDeviceAuthorizationAttemptStatuses.Consumed;
        attempt.ConsumedAtUtc = now;

        await db.MemCliDeviceSessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.cli-device.session.created",
                Outcome: "succeeded",
                ActorOperatorId: approvedOperator.Id,
                SubjectOperatorId: approvedOperator.Id,
                CorrelationId: correlationId,
                ReasonCode: "browser_approved_device_authorization"),
            ct);

        await transaction.CommitAsync(ct);

        return new MemCliDeviceAuthorizationPollResult(
            "authorized",
            rawCredential,
            idleExpiresAtUtc,
            absoluteExpiresAtUtc);
    }

    public async Task<MemCliDeviceSessionValidationResult> ValidateAsync(
        Guid installationId,
        string deviceCredential,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        if (installationId == Guid.Empty)
        {
            return new MemCliDeviceSessionValidationResult("invalid");
        }

        var credentialHash = TryHashOpaqueValueHex(deviceCredential);
        if (credentialHash is null)
        {
            return new MemCliDeviceSessionValidationResult("invalid");
        }

        var session = await db.MemCliDeviceSessions.SingleOrDefaultAsync(
            entry => entry.InstallationId == installationId &&
                entry.CredentialHash == credentialHash,
            ct);

        if (session is null)
        {
            return new MemCliDeviceSessionValidationResult("invalid");
        }

        var now = timeProvider.GetUtcNow();

        if (session.RevokedAtUtc is not null)
        {
            return new MemCliDeviceSessionValidationResult("revoked");
        }

        if (now >= session.IdleExpiresAtUtc || now >= session.AbsoluteExpiresAtUtc)
        {
            await RevokeAsync(
                session,
                now,
                "session_expired",
                correlationId,
                auditEventType: "identity.cli-device.session.expired",
                auditOutcome: "expired",
                ct);

            return new MemCliDeviceSessionValidationResult("expired");
        }

        var subject = await userManager.FindByIdAsync(session.OperatorId.ToString("D"));
        if (subject is null ||
            !subject.IsEnabled ||
            subject.IsBootstrapProvisioning ||
            !subject.TwoFactorEnabled)
        {
            await RevokeAsync(
                session,
                now,
                "account_unavailable",
                correlationId,
                auditEventType: "identity.cli-device.session.revoked",
                auditOutcome: "revoked",
                ct);

            return new MemCliDeviceSessionValidationResult("account_unavailable");
        }

        if (!string.Equals(
                session.SecurityStamp,
                RequireSecurityStamp(subject),
                StringComparison.Ordinal))
        {
            await RevokeAsync(
                session,
                now,
                "security_stamp_changed",
                correlationId,
                auditEventType: "identity.cli-device.session.revoked",
                auditOutcome: "revoked",
                ct);

            return new MemCliDeviceSessionValidationResult("revoked");
        }

        session.LastSeenAtUtc = now;
        session.IdleExpiresAtUtc = Min(
            now.AddMinutes(options.Value.SessionIdleMinutes),
            session.AbsoluteExpiresAtUtc);

        await db.SaveChangesAsync(ct);

        return new MemCliDeviceSessionValidationResult(
            "authenticated",
            subject.Id,
            session.Id,
            session.IdleExpiresAtUtc,
            session.AbsoluteExpiresAtUtc);
    }

    public async Task<MemCliDeviceSessionRevocationResult> RevokeSessionAsync(
        Guid sessionId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        if (sessionId == Guid.Empty)
        {
            return new MemCliDeviceSessionRevocationResult("unavailable");
        }

        var session = await db.MemCliDeviceSessions.SingleOrDefaultAsync(
            entry => entry.Id == sessionId,
            ct);

        if (session is null)
        {
            return new MemCliDeviceSessionRevocationResult("unavailable");
        }

        if (session.RevokedAtUtc is { } alreadyRevokedAtUtc)
        {
            return new MemCliDeviceSessionRevocationResult(
                "revoked",
                alreadyRevokedAtUtc);
        }

        var now = timeProvider.GetUtcNow();

        await RevokeAsync(
            session,
            now,
            "device_logout",
            correlationId,
            auditEventType: "identity.cli-device.session.revoked",
            auditOutcome: "revoked",
            ct);

        return new MemCliDeviceSessionRevocationResult(
            "revoked",
            now);
    }

    private async Task<MemCliDeviceAuthorizationAttemptEntity?> FindAttemptByUserCodeAsync(
        string userCode,
        CancellationToken ct)
    {
        var normalizedCode = TryNormalizeUserCode(userCode);
        if (normalizedCode is null)
        {
            return null;
        }

        var userCodeHash = Sha256Hex(normalizedCode);

        return await db.MemCliDeviceAuthorizationAttempts.SingleOrDefaultAsync(
            entry => entry.UserCodeHash == userCodeHash,
            ct);
    }

    private async Task<bool> ExpireIfNecessaryAsync(
        MemCliDeviceAuthorizationAttemptEntity attempt,
        DateTimeOffset now,
        string? correlationId,
        CancellationToken ct)
    {
        if (attempt.Status is not (
                MemCliDeviceAuthorizationAttemptStatuses.Pending or
                MemCliDeviceAuthorizationAttemptStatuses.Approved) ||
            now < attempt.ExpiresAtUtc)
        {
            return attempt.Status == MemCliDeviceAuthorizationAttemptStatuses.Expired;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        attempt.Status = MemCliDeviceAuthorizationAttemptStatuses.Expired;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.cli-device.authorization.expired",
                Outcome: "expired",
                CorrelationId: correlationId,
                ReasonCode: "authorization_expired"),
            ct);

        await transaction.CommitAsync(ct);

        return true;
    }

    private async Task RevokeAsync(
        MemCliDeviceSessionEntity session,
        DateTimeOffset occurredAtUtc,
        string reasonCode,
        string? correlationId,
        string auditEventType,
        string auditOutcome,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        session.RevokedAtUtc = occurredAtUtc;
        session.RevokedReasonCode = reasonCode;

        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: auditEventType,
                Outcome: auditOutcome,
                SubjectOperatorId: session.OperatorId,
                CorrelationId: correlationId,
                ReasonCode: reasonCode),
            ct);

        await transaction.CommitAsync(ct);
    }

    private async Task<bool> IsEligibleApproverAsync(MemOperator? approver)
    {
        return approver is
            {
                IsEnabled: true,
                IsBootstrapProvisioning: false,
                TwoFactorEnabled: true
            } &&
            !await userManager.IsLockedOutAsync(approver);
    }

    private static string NormalizeDigest(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value.Trim());
            if (bytes.Length != DigestLengthBytes)
            {
                throw new ArgumentException(
                    "The device verifier challenge has an invalid length.",
                    parameterName);
            }

            return WebEncoders.Base64UrlEncode(bytes);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(
                "The device verifier challenge has an invalid format.",
                parameterName,
                exception);
        }
    }

    private static string NormalizeDeviceLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultDeviceLabel;
        }

        var normalized = value.Trim();

        if (normalized.Length > DeviceLabelMaxLength ||
            normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The device label is invalid.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeUserCode(string value)
    {
        return TryNormalizeUserCode(value)
            ?? throw new ArgumentException("The device user code is invalid.", nameof(value));
    }

    private static string? TryNormalizeUserCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized.Length == 8 &&
            normalized.All(character => UserCodeAlphabet.Contains(character))
            ? normalized
            : null;
    }

    private static bool VerifierMatches(string expectedChallenge, string verifier)
    {
        var suppliedChallenge = TryHashOpaqueValue(verifier);
        if (suppliedChallenge is null)
        {
            return false;
        }

        try
        {
            var expectedBytes = WebEncoders.Base64UrlDecode(expectedChallenge);
            var suppliedBytes = WebEncoders.Base64UrlDecode(suppliedChallenge);

            return CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? TryHashOpaqueValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var opaqueBytes = WebEncoders.Base64UrlDecode(value.Trim());
            return opaqueBytes.Length == DigestLengthBytes
                ? WebEncoders.Base64UrlEncode(
                    SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? TryHashOpaqueValueHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var opaqueBytes = WebEncoders.Base64UrlDecode(value.Trim());
            return opaqueBytes.Length == DigestLengthBytes
                ? Sha256Hex(value.Trim())
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Sha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static string CreateOpaqueValue()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(DigestLengthBytes));
    }

    private static string CreateUserCode()
    {
        Span<char> code = stackalloc char[9];

        for (var index = 0; index < 4; index++)
        {
            code[index] = UserCodeAlphabet[
                RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }

        code[4] = '-';

        for (var index = 5; index < code.Length; index++)
        {
            code[index] = UserCodeAlphabet[
                RandomNumberGenerator.GetInt32(UserCodeAlphabet.Length)];
        }

        return new string(code);
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static string ToDecisionStatus(string status) => status switch
    {
        MemCliDeviceAuthorizationAttemptStatuses.Approved => "authorization_approved",
        MemCliDeviceAuthorizationAttemptStatuses.Denied => "authorization_denied",
        MemCliDeviceAuthorizationAttemptStatuses.Consumed => "authorization_consumed",
        MemCliDeviceAuthorizationAttemptStatuses.Expired => "authorization_expired",
        _ => "authorization_unavailable"
    };

    private static string RequireSecurityStamp(MemOperator subject)
    {
        return string.IsNullOrWhiteSpace(subject.SecurityStamp)
            ? throw new InvalidOperationException(
                "A CLI device session requires an Identity security stamp.")
            : subject.SecurityStamp;
    }
}
