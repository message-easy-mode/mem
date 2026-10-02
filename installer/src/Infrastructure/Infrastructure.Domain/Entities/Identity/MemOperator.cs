using Microsoft.AspNetCore.Identity;

namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// The named local operator account used by the MEM control plane.
/// Authentication details, passwords, MFA keys, recovery codes, claims and
/// role memberships are owned by ASP.NET Core Identity. MEM-specific lifecycle
/// state remains explicit on this type.
/// </summary>
public sealed class MemOperator : IdentityUser<Guid>
{
    /// <summary>
    /// Disabled operators must not establish or retain a normal control-plane session.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// True only while the one-time first-owner bootstrap wizard is preparing
    /// an account. Provisioning accounts are disabled and have no MEM role until
    /// TOTP verification and recovery-code generation complete.
    /// </summary>
    public bool IsBootstrapProvisioning { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <summary>
    /// Summary/audit field for a later session-bound recent-authentication grant.
    /// It is not used as authorization by this foundation slice.
    /// </summary>
    public DateTimeOffset? LastStepUpAtUtc { get; set; }
}
