using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

public sealed class OperatorAdministrationException(
    string code,
    OperatorAdministrationFailureKind kind)
    : InvalidOperationException(code)
{
    public string Code { get; } = code;

    public OperatorAdministrationFailureKind Kind { get; } = kind;
}

public enum OperatorAdministrationFailureKind
{
    Validation,
    NotFound,
    Conflict,
    Unavailable
}

public interface IMemOperatorLifecycleService
{
    Task<MemOperatorDirectoryEntry> CreatePendingAsync(
        Guid actorOperatorId,
        CreatePendingMemOperatorRequest request,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemOperatorDirectoryEntry> SetEnabledAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        SetMemOperatorEnabledRequest request,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemOperatorDirectoryEntry> SetRolesAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        SetMemOperatorRolesRequest request,
        string? correlationId = null,
        CancellationToken ct = default);

    Task<MemOperatorDirectoryEntry> RevokeSessionsAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        string? correlationId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Applies privileged local-operator lifecycle changes behind the Platform Owner
/// policy. Every state-changing method rejects self-management, invalidates the
/// target Identity security stamp when appropriate, and writes structured,
/// secret-free audit records.
///
/// Pending operators are intentionally created disabled and passwordless. A
/// later enrolment flow will establish a password, TOTP factor, recovery codes,
/// and only then allow a Platform Owner to enable normal sign-in.
/// </summary>
public sealed class MemOperatorLifecycleService(
    MemDbContext db,
    UserManager<MemOperator> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IMemOperatorAuditService audit,
    IMemOperatorStepUpService stepUp,
    TimeProvider timeProvider) : IMemOperatorLifecycleService
{
    public async Task<MemOperatorDirectoryEntry> CreatePendingAsync(
        Guid actorOperatorId,
        CreatePendingMemOperatorRequest request,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = RequireUsername(request.Username);
        var email = NormalizeOptionalEmail(request.Email);
        var roles = NormalizeRoles(request.Roles);

        await EnsureRolesAvailableAsync(roles, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = username,
            Email = email,
            IsEnabled = false,
            IsBootstrapProvisioning = false,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        var created = await userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            throw ToCreateException(created);
        }

        var rolesAdded = await userManager.AddToRolesAsync(user, roles);
        if (!rolesAdded.Succeeded)
        {
            throw new OperatorAdministrationException(
                "operator_role_assignment_failed",
                OperatorAdministrationFailureKind.Unavailable);
        }

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.created",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: user.Id,
                CorrelationId: correlationId,
                ReasonCode: "enrolment_required"),
            ct);

        foreach (var role in roles)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.operator.role_granted",
                    Outcome: "succeeded",
                    ActorOperatorId: actorOperatorId,
                    SubjectOperatorId: user.Id,
                    CorrelationId: correlationId,
                    ReasonCode: role),
                ct);
        }

        await transaction.CommitAsync(ct);

        return await ToDirectoryEntryAsync(user, actorOperatorId, ct);
    }

    public async Task<MemOperatorDirectoryEntry> SetEnabledAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        SetMemOperatorEnabledRequest request,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsEnabled is null)
        {
            throw new OperatorAdministrationException(
                "operator_enabled_required",
                OperatorAdministrationFailureKind.Validation);
        }

        EnsureNotSelf(actorOperatorId, subjectOperatorId);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var subject = await GetSubjectOrThrowAsync(subjectOperatorId, ct);

        if (subject.IsEnabled == request.IsEnabled.Value)
        {
            return await ToDirectoryEntryAsync(subject, actorOperatorId, ct);
        }

        var currentRoles = await GetRolesAsync(subject, ct);

        if (request.IsEnabled.Value)
        {
            EnsureCanBeEnabled(subject, currentRoles);
        }
        else
        {
            await EnsureNotFinalActivePlatformOwnerAsync(
                subject,
                currentRoles,
                removingPlatformOwnerRole: false,
                disablingSubject: true,
                ct);
        }

        subject.IsEnabled = request.IsEnabled.Value;

        var updated = await userManager.UpdateSecurityStampAsync(subject);
        if (!updated.Succeeded)
        {
            throw new OperatorAdministrationException(
                "operator_lifecycle_update_failed",
                OperatorAdministrationFailureKind.Unavailable);
        }

        await stepUp.RevokeAllForOperatorAsync(
            actorOperatorId,
            subject.Id,
            "account_enabled_changed",
            correlationId,
            ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: request.IsEnabled.Value
                    ? "identity.operator.enabled"
                    : "identity.operator.disabled",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: subject.Id,
                CorrelationId: correlationId,
                ReasonCode: "platform_owner_action"),
            ct);

        await transaction.CommitAsync(ct);

        return await ToDirectoryEntryAsync(subject, actorOperatorId, ct);
    }

    public async Task<MemOperatorDirectoryEntry> SetRolesAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        SetMemOperatorRolesRequest request,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureNotSelf(actorOperatorId, subjectOperatorId);

        var desiredRoles = NormalizeRoles(request.Roles);
        await EnsureRolesAvailableAsync(desiredRoles, ct);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var subject = await GetSubjectOrThrowAsync(subjectOperatorId, ct);
        var currentRoles = await GetRolesAsync(subject, ct);

        var currentSet = currentRoles.ToHashSet(StringComparer.Ordinal);
        var desiredSet = desiredRoles.ToHashSet(StringComparer.Ordinal);

        var rolesToGrant = desiredRoles
            .Where(role => !currentSet.Contains(role))
            .ToArray();

        var rolesToRevoke = currentRoles
            .Where(role => !desiredSet.Contains(role))
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();

        if (rolesToGrant.Length == 0 && rolesToRevoke.Length == 0)
        {
            return await ToDirectoryEntryAsync(subject, actorOperatorId, ct);
        }

        await EnsureNotFinalActivePlatformOwnerAsync(
            subject,
            currentRoles,
            removingPlatformOwnerRole: rolesToRevoke.Contains(
                MemOperatorRoles.PlatformOwner,
                StringComparer.Ordinal),
            disablingSubject: false,
            ct);

        if (rolesToGrant.Length > 0)
        {
            var granted = await userManager.AddToRolesAsync(subject, rolesToGrant);
            if (!granted.Succeeded)
            {
                throw new OperatorAdministrationException(
                    "operator_role_assignment_failed",
                    OperatorAdministrationFailureKind.Unavailable);
            }
        }

        if (rolesToRevoke.Length > 0)
        {
            var revoked = await userManager.RemoveFromRolesAsync(subject, rolesToRevoke);
            if (!revoked.Succeeded)
            {
                throw new OperatorAdministrationException(
                    "operator_role_assignment_failed",
                    OperatorAdministrationFailureKind.Unavailable);
            }
        }

        var stampUpdated = await userManager.UpdateSecurityStampAsync(subject);
        if (!stampUpdated.Succeeded)
        {
            throw new OperatorAdministrationException(
                "operator_session_invalidation_failed",
                OperatorAdministrationFailureKind.Unavailable);
        }

        await stepUp.RevokeAllForOperatorAsync(
            actorOperatorId,
            subject.Id,
            "role_membership_changed",
            correlationId,
            ct);

        foreach (var role in rolesToGrant)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.operator.role_granted",
                    Outcome: "succeeded",
                    ActorOperatorId: actorOperatorId,
                    SubjectOperatorId: subject.Id,
                    CorrelationId: correlationId,
                    ReasonCode: role),
                ct);
        }

        foreach (var role in rolesToRevoke)
        {
            await audit.WriteAsync(
                new MemOperatorAuditEventWrite(
                    EventType: "identity.operator.role_revoked",
                    Outcome: "succeeded",
                    ActorOperatorId: actorOperatorId,
                    SubjectOperatorId: subject.Id,
                    CorrelationId: correlationId,
                    ReasonCode: role),
                ct);
        }

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.sessions_revoked",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: subject.Id,
                CorrelationId: correlationId,
                ReasonCode: "role_membership_changed"),
            ct);

        await transaction.CommitAsync(ct);

        return await ToDirectoryEntryAsync(subject, actorOperatorId, ct);
    }

    public async Task<MemOperatorDirectoryEntry> RevokeSessionsAsync(
        Guid actorOperatorId,
        Guid subjectOperatorId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        EnsureNotSelf(actorOperatorId, subjectOperatorId);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var subject = await GetSubjectOrThrowAsync(subjectOperatorId, ct);

        var updated = await userManager.UpdateSecurityStampAsync(subject);
        if (!updated.Succeeded)
        {
            throw new OperatorAdministrationException(
                "operator_session_invalidation_failed",
                OperatorAdministrationFailureKind.Unavailable);
        }

        await stepUp.RevokeAllForOperatorAsync(
            actorOperatorId,
            subject.Id,
            "operator_session_revocation",
            correlationId,
            ct);

        await audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: "identity.operator.sessions_revoked",
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                SubjectOperatorId: subject.Id,
                CorrelationId: correlationId,
                ReasonCode: "platform_owner_action"),
            ct);

        await transaction.CommitAsync(ct);

        return await ToDirectoryEntryAsync(subject, actorOperatorId, ct);
    }

    private async Task<MemOperator> GetSubjectOrThrowAsync(
        Guid subjectOperatorId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var subject = await userManager.FindByIdAsync(subjectOperatorId.ToString("D"));

        return subject ?? throw new OperatorAdministrationException(
            "operator_not_found",
            OperatorAdministrationFailureKind.NotFound);
    }

    private async Task<IReadOnlyList<string>> GetRolesAsync(
        MemOperator user,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var roles = await userManager.GetRolesAsync(user);

        return roles
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task EnsureRolesAvailableAsync(
        IReadOnlyList<string> roles,
        CancellationToken ct)
    {
        foreach (var role in roles)
        {
            ct.ThrowIfCancellationRequested();

            if (!await roleManager.RoleExistsAsync(role))
            {
                throw new OperatorAdministrationException(
                    "operator_role_unavailable",
                    OperatorAdministrationFailureKind.Unavailable);
            }
        }
    }

    private async Task EnsureNotFinalActivePlatformOwnerAsync(
        MemOperator subject,
        IReadOnlyList<string> currentRoles,
        bool removingPlatformOwnerRole,
        bool disablingSubject,
        CancellationToken ct)
    {
        var subjectIsActiveOwner =
            subject.IsEnabled &&
            !subject.IsBootstrapProvisioning &&
            currentRoles.Contains(MemOperatorRoles.PlatformOwner, StringComparer.Ordinal);

        if (!subjectIsActiveOwner ||
            (!removingPlatformOwnerRole && !disablingSubject))
        {
            return;
        }

        var activeOwnerCount = await (
            from user in db.Users
            join userRole in db.UserRoles on user.Id equals userRole.UserId
            join role in db.Roles on userRole.RoleId equals role.Id
            where user.IsEnabled &&
                  !user.IsBootstrapProvisioning &&
                  role.Name == MemOperatorRoles.PlatformOwner
            select user.Id)
            .Distinct()
            .CountAsync(ct);

        if (activeOwnerCount <= 1)
        {
            throw new OperatorAdministrationException(
                "last_active_platform_owner",
                OperatorAdministrationFailureKind.Conflict);
        }
    }

    private static void EnsureCanBeEnabled(
        MemOperator subject,
        IReadOnlyList<string> roles)
    {
        if (subject.IsBootstrapProvisioning)
        {
            throw new OperatorAdministrationException(
                "operator_bootstrap_provisioning",
                OperatorAdministrationFailureKind.Conflict);
        }

        if (string.IsNullOrWhiteSpace(subject.PasswordHash) ||
            !subject.TwoFactorEnabled ||
            roles.Count == 0)
        {
            throw new OperatorAdministrationException(
                "operator_enrolment_required",
                OperatorAdministrationFailureKind.Conflict);
        }
    }

    private static void EnsureNotSelf(
        Guid actorOperatorId,
        Guid subjectOperatorId)
    {
        if (actorOperatorId == subjectOperatorId)
        {
            throw new OperatorAdministrationException(
                "operator_self_management_not_allowed",
                OperatorAdministrationFailureKind.Conflict);
        }
    }

    private async Task<MemOperatorDirectoryEntry> ToDirectoryEntryAsync(
        MemOperator user,
        Guid currentOperatorId,
        CancellationToken ct)
    {
        var roles = await GetRolesAsync(user, ct);

        return new MemOperatorDirectoryEntry(
            OperatorId: user.Id,
            Username: user.UserName ?? user.Id.ToString("D"),
            Email: user.Email,
            IsEnabled: user.IsEnabled,
            IsBootstrapProvisioning: user.IsBootstrapProvisioning,
            HasPassword: !string.IsNullOrWhiteSpace(user.PasswordHash),
            HasTotp: user.TwoFactorEnabled,
            Roles: roles,
            CreatedAtUtc: user.CreatedAtUtc,
            LastLoginAtUtc: user.LastLoginAtUtc,
            EnrollmentGrantExpiresAtUtc: null,
            IsCurrentOperator: user.Id == currentOperatorId);
    }

    private static string RequireUsername(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OperatorAdministrationException(
                "username_required",
                OperatorAdministrationFailureKind.Validation);
        }

        var username = value.Trim();

        if (username.Length is < 3 or > 100)
        {
            throw new OperatorAdministrationException(
                "username_invalid",
                OperatorAdministrationFailureKind.Validation);
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
            throw new OperatorAdministrationException(
                "email_invalid",
                OperatorAdministrationFailureKind.Validation);
        }

        return email;
    }

    private static IReadOnlyList<string> NormalizeRoles(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            throw new OperatorAdministrationException(
                "operator_role_required",
                OperatorAdministrationFailureKind.Validation);
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            throw new OperatorAdministrationException(
                "operator_role_invalid",
                OperatorAdministrationFailureKind.Validation);
        }

        var normalized = values
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0 ||
            normalized.Any(role => !MemOperatorRoles.All.Contains(role, StringComparer.Ordinal)))
        {
            throw new OperatorAdministrationException(
                "operator_role_invalid",
                OperatorAdministrationFailureKind.Validation);
        }

        return normalized;
    }

    private static OperatorAdministrationException ToCreateException(
        IdentityResult result)
    {
        var duplicateUsername = result.Errors.Any(error =>
            string.Equals(error.Code, "DuplicateUserName", StringComparison.Ordinal));

        return new OperatorAdministrationException(
            duplicateUsername ? "username_unavailable" : "operator_create_failed",
            duplicateUsername
                ? OperatorAdministrationFailureKind.Validation
                : OperatorAdministrationFailureKind.Unavailable);
    }
}
