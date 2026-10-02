namespace Modules.Auth.Contracts;

/// <summary>
/// A Platform Owner-safe directory projection. It intentionally exposes only
/// lifecycle/session readiness, identity labels, and role names; it never
/// exposes password hashes, TOTP seeds, recovery codes, security stamps, or
/// arbitrary claims.
/// </summary>
public sealed record MemOperatorDirectoryEntry(
    Guid OperatorId,
    string Username,
    string? Email,
    bool IsEnabled,
    bool IsBootstrapProvisioning,
    bool HasPassword,
    bool HasTotp,
    IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? EnrollmentGrantExpiresAtUtc,
    bool IsCurrentOperator);

/// <summary>
/// Creates a deliberately disabled local account. A later, separately reviewed
/// enrolment flow is responsible for password creation, TOTP setup, recovery
/// codes, and enabling normal sign-in.
/// </summary>
public sealed record CreatePendingMemOperatorRequest(
    string? Username,
    string? Email,
    IReadOnlyList<string>? Roles);

/// <summary>
/// Uses a nullable value so an omitted JSON property cannot accidentally be
/// interpreted as a request to disable an operator.
/// </summary>
public sealed record SetMemOperatorEnabledRequest(
    bool? IsEnabled);

public sealed record SetMemOperatorRolesRequest(
    IReadOnlyList<string>? Roles);
