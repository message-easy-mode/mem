namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// A server-side, short-lived first-owner bootstrap grant. The raw setup
/// code is never stored here: the bootstrap cookie carries only this opaque
/// grant identifier and the grant is valid only while the server-side record
/// remains active.
/// </summary>
public sealed class MemBootstrapGrantEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? TotpVerifiedAtUtc { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    /// <summary>
    /// The disabled, unassigned Identity user being enrolled. It is populated
    /// only after a password and authenticator key have been prepared.
    /// </summary>
    public Guid? PendingOperatorId { get; set; }

    public string Status { get; set; } = MemBootstrapGrantStatuses.Active;
}

public static class MemBootstrapGrantStatuses
{
    public const string Active = "active";
    public const string TotpVerified = "totp_verified";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static bool IsActive(string status) =>
        status is Active or TotpVerified;
}
