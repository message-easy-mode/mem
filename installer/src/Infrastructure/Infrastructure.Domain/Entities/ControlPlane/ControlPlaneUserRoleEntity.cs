using System;

namespace Infrastructure.Data.Entities.ControlPlane;

public sealed class ControlPlaneUserRoleEntity
{
    public Guid Id { get; set; }

    public Guid ControlPlaneUserId { get; set; }
    public ControlPlaneUserEntity ControlPlaneUser { get; set; } = default!;

    public string Role { get; set; } = default!;
}
