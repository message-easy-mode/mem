namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// A server-side named-operator CLI device session.
///
/// The opaque credential is generated only after browser approval and returned
/// once to the proving CLI process. This model stores only its SHA-256 digest,
/// never the credential itself.
/// </summary>
public sealed class MemCliDeviceSessionEntity
{
    public Guid Id { get; set; }

    public Guid AuthorizationAttemptId { get; set; }

    public Guid InstallationId { get; set; }

    public Guid OperatorId { get; set; }

    /// <summary>
    /// SHA-256 digest of the opaque high-entropy CLI device credential.
    /// </summary>
    public string CredentialHash { get; set; } = default!;

    /// <summary>
    /// Snapshot used to fail closed when password, role, enablement, MFA, or
    /// explicit session revocation changes the Identity security stamp.
    /// </summary>
    public string SecurityStamp { get; set; } = default!;

    public string DeviceLabel { get; set; } = default!;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public DateTimeOffset IdleExpiresAtUtc { get; set; }

    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevokedReasonCode { get; set; }
}
