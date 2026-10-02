using System;

namespace Infrastructure.Data.Entities.ControlPlane;

public sealed class ControlPlaneUserEntity
{
    public Guid Id { get; set; }

    public string Username { get; set; } = default!;
    public string NormalizedUsername { get; set; } = default!;

    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }

    public string PasswordHash { get; set; } = default!;

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<ControlPlaneUserRoleEntity> Roles { get; set; } =
        new List<ControlPlaneUserRoleEntity>();
}
