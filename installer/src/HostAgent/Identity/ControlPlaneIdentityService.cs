using Infrastructure.Data.Entities.ControlPlane;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Identity;

public sealed class ControlPlaneIdentityService
{
    private readonly MemDbContext _db;
    private readonly ControlPlanePasswordHasher _passwordHasher;

    public ControlPlaneIdentityService(
        MemDbContext db,
        ControlPlanePasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<ControlPlaneBootstrapStatusResponse> GetBootstrapStatusAsync(
        CancellationToken ct)
    {
        var hasAnyUsers = await _db.Set<ControlPlaneUserEntity>()
            .AnyAsync(ct);

        var hasAnyPowerAdmin = await HasAnyPowerAdminAsync(ct);

        return new ControlPlaneBootstrapStatusResponse(
            RequiresFirstAdminSetup: !hasAnyPowerAdmin,
            HasAnyUsers: hasAnyUsers,
            HasAnyPowerAdmin: hasAnyPowerAdmin);
    }

    public async Task<ControlPlaneUserDto> CreateFirstAdminAsync(
        CreateFirstControlPlaneAdminRequest request,
        CancellationToken ct)
    {
        if (await HasAnyPowerAdminAsync(ct))
        {
            throw new InvalidOperationException("First control-plane admin has already been created.");
        }

        var user = await CreateUserCoreAsync(
            username: request.Username,
            email: request.Email,
            password: request.Password,
            roles: [ControlPlaneRoles.PowerAdmin],
            ct);

        await _db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    public async Task<IReadOnlyList<ControlPlaneUserDto>> ListUsersAsync(
        CancellationToken ct)
    {
        var users = await _db.Set<ControlPlaneUserEntity>()
            .Include(x => x.Roles)
            .OrderBy(x => x.Username)
            .ToArrayAsync(ct);

        return users
            .Select(ToDto)
            .ToArray();
    }

    public async Task<ControlPlaneUserDto> CreateUserAsync(
        CreateControlPlaneUserRequest request,
        CancellationToken ct)
    {
        var user = await CreateUserCoreAsync(
            username: request.Username,
            email: request.Email,
            password: request.Password,
            roles: [],
            ct);

        await _db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    public async Task<ControlPlaneUserDto> UpdateUserAsync(
        Guid userId,
        UpdateControlPlaneUserRequest request,
        CancellationToken ct)
    {
        var user = await GetUserOrThrowAsync(userId, ct);

        var username = RequireUsername(request.Username);
        var normalizedUsername = NormalizeRequired(username);

        var existingByUsername = await _db.Set<ControlPlaneUserEntity>()
            .FirstOrDefaultAsync(x => x.NormalizedUsername == normalizedUsername, ct);

        if (existingByUsername is not null && existingByUsername.Id != user.Id)
        {
            throw new InvalidOperationException("Username is already in use.");
        }

        var email = NormalizeOptionalRaw(request.Email);
        var normalizedEmail = NormalizeOptional(email);

        if (normalizedEmail is not null)
        {
            var existingByEmail = await _db.Set<ControlPlaneUserEntity>()
                .FirstOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, ct);

            if (existingByEmail is not null && existingByEmail.Id != user.Id)
            {
                throw new InvalidOperationException("Email is already in use.");
            }
        }

        user.Username = username;
        user.NormalizedUsername = normalizedUsername;
        user.Email = email;
        user.NormalizedEmail = normalizedEmail;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    public async Task<ControlPlaneUserRolesDto> AssignRoleAsync(
        Guid userId,
        AssignControlPlaneUserRoleRequest request,
        CancellationToken ct)
    {
        var user = await GetUserOrThrowAsync(userId, ct);
        var role = NormalizeRoleOrThrow(request.Role);

        if (user.Roles.All(x => !string.Equals(x.Role, role, StringComparison.OrdinalIgnoreCase)))
        {
            user.Roles.Add(new ControlPlaneUserRoleEntity
            {
                Id = Guid.NewGuid(),
                ControlPlaneUserId = user.Id,
                Role = role
            });

            user.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }

        return ToRolesDto(user);
    }

    public async Task<ControlPlaneUserRolesDto> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct)
    {
        var user = await GetUserOrThrowAsync(userId, ct);
        var normalizedRole = NormalizeRoleOrThrow(role);

        if (string.Equals(normalizedRole, ControlPlaneRoles.PowerAdmin, StringComparison.OrdinalIgnoreCase))
        {
            var hasAnotherPowerAdmin = await _db.Set<ControlPlaneUserEntity>()
                .Where(x => x.Id != user.Id)
                .AnyAsync(x => x.Roles.Any(r => r.Role == ControlPlaneRoles.PowerAdmin), ct);

            if (!hasAnotherPowerAdmin)
            {
                throw new InvalidOperationException("Cannot remove the last power admin.");
            }
        }

        var existing = user.Roles.FirstOrDefault(x =>
            string.Equals(x.Role, normalizedRole, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            user.Roles.Remove(existing);
            user.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
        }

        return ToRolesDto(user);
    }

    public async Task<ControlPlaneUserDto> ActivateAsync(
        Guid userId,
        CancellationToken ct)
    {
        var user = await GetUserOrThrowAsync(userId, ct);

        user.IsActive = true;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    public async Task<ControlPlaneUserDto> DeactivateAsync(
        Guid userId,
        CancellationToken ct)
    {
        var user = await GetUserOrThrowAsync(userId, ct);

        if (user.Roles.Any(x => x.Role == ControlPlaneRoles.PowerAdmin))
        {
            var hasAnotherPowerAdmin = await _db.Set<ControlPlaneUserEntity>()
                .Where(x => x.Id != user.Id && x.IsActive)
                .AnyAsync(x => x.Roles.Any(r => r.Role == ControlPlaneRoles.PowerAdmin), ct);

            if (!hasAnotherPowerAdmin)
            {
                throw new InvalidOperationException("Cannot deactivate the last active power admin.");
            }
        }

        user.IsActive = false;
        user.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    private async Task<ControlPlaneUserEntity> CreateUserCoreAsync(
        string username,
        string? email,
        string password,
        IReadOnlyList<string> roles,
        CancellationToken ct)
    {
        username = RequireUsername(username);
        email = NormalizeOptionalRaw(email);

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            throw new InvalidOperationException("Password must be at least 8 characters.");
        }

        var normalizedUsername = NormalizeRequired(username);

        if (await _db.Set<ControlPlaneUserEntity>().AnyAsync(x => x.NormalizedUsername == normalizedUsername, ct))
        {
            throw new InvalidOperationException("Username is already in use.");
        }

        var normalizedEmail = NormalizeOptional(email);

        if (normalizedEmail is not null &&
            await _db.Set<ControlPlaneUserEntity>().AnyAsync(x => x.NormalizedEmail == normalizedEmail, ct))
        {
            throw new InvalidOperationException("Email is already in use.");
        }

        var now = DateTime.UtcNow;

        var user = new ControlPlaneUserEntity
        {
            Id = Guid.NewGuid(),
            Username = username,
            NormalizedUsername = normalizedUsername,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(password),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        foreach (var role in roles)
        {
            user.Roles.Add(new ControlPlaneUserRoleEntity
            {
                Id = Guid.NewGuid(),
                ControlPlaneUserId = user.Id,
                Role = NormalizeRoleOrThrow(role)
            });
        }

        await _db.Set<ControlPlaneUserEntity>().AddAsync(user, ct);

        return user;
    }

    private async Task<bool> HasAnyPowerAdminAsync(
        CancellationToken ct)
    {
        return await _db.Set<ControlPlaneUserEntity>()
            .AnyAsync(x => x.Roles.Any(r => r.Role == ControlPlaneRoles.PowerAdmin), ct);
    }

    private async Task<ControlPlaneUserEntity> GetUserOrThrowAsync(
        Guid userId,
        CancellationToken ct)
    {
        return await _db.Set<ControlPlaneUserEntity>()
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.Id == userId, ct)
            ?? throw new InvalidOperationException("Control-plane user was not found.");
    }

    private static ControlPlaneUserDto ToDto(
        ControlPlaneUserEntity user)
    {
        return new ControlPlaneUserDto(
            UserId: user.Id,
            Username: user.Username,
            Email: user.Email,
            IsActive: user.IsActive,
            Roles: user.Roles
                .Select(x => x.Role)
                .OrderBy(x => x)
                .ToArray(),
            CreatedAtUtc: user.CreatedAtUtc,
            UpdatedAtUtc: user.UpdatedAtUtc);
    }

    private static ControlPlaneUserRolesDto ToRolesDto(
        ControlPlaneUserEntity user)
    {
        return new ControlPlaneUserRolesDto(
            UserId: user.Id,
            Username: user.Username,
            Roles: user.Roles
                .Select(x => x.Role)
                .OrderBy(x => x)
                .ToArray());
    }

    private static string RequireUsername(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Username is required.");
        }

        value = value.Trim();

        if (value.Length < 3)
        {
            throw new InvalidOperationException("Username must be at least 3 characters.");
        }

        if (value.Length > 100)
        {
            throw new InvalidOperationException("Username is too long.");
        }

        return value;
    }

    private static string NormalizeRequired(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeOptionalRaw(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string NormalizeRoleOrThrow(string role)
    {
        if (!ControlPlaneRoles.IsValid(role))
        {
            throw new InvalidOperationException($"Unknown control-plane role '{role}'.");
        }

        return ControlPlaneRoles.Normalize(role);
    }
}
