namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// A server-side, session-bound recent-authentication grant. It intentionally
/// stores only identifiers, timestamps, a security-stamp snapshot, and safe
/// lifecycle codes; it never stores passwords, TOTP values, recovery codes,
/// cookie material, or a bearer token.
/// </summary>
public sealed class MemOperatorStepUpGrantEntity
{
    public Guid Id { get; set; }

    public Guid OperatorId { get; set; }

    /// <summary>
    /// Random identifier held only in the protected current Identity cookie.
    /// It binds the grant to one browser session rather than the operator as a
    /// whole.
    /// </summary>
    public Guid SessionId { get; set; }

    public string SecurityStamp { get; set; } = default!;

    public DateTimeOffset IssuedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? RevokedReasonCode { get; set; }
}
