namespace HostAgent.Identity;

public sealed record ControlPlaneBootstrapStatusResponse(
    bool RequiresFirstAdminSetup,
    bool HasAnyUsers,
    bool HasAnyPowerAdmin);

public sealed record CreateFirstControlPlaneAdminRequest(
    string Username,
    string? Email,
    string Password);

public sealed record CreateControlPlaneUserRequest(
    string Username,
    string? Email,
    string Password);

public sealed record UpdateControlPlaneUserRequest(
    string Username,
    string? Email);

public sealed record AssignControlPlaneUserRoleRequest(
    string Role);

public sealed record ControlPlaneUserDto(
    Guid UserId,
    string Username,
    string? Email,
    bool IsActive,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ControlPlaneUserRolesDto(
    Guid UserId,
    string Username,
    IReadOnlyList<string> Roles);
