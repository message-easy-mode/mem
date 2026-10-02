namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// A server-side, one-time local-operator enrolment grant. The raw enrolment
/// code is never persisted: only its SHA-256 digest is stored. The browser
/// receives a short-lived opaque cookie containing the grant id after it
/// presents the raw code once.
/// </summary>
public sealed class MemOperatorEnrollmentGrantEntity
{
    public Guid Id { get; set; }

    public Guid OperatorId { get; set; }

    /// <summary>
    /// Uppercase hexadecimal SHA-256 digest of the high-entropy raw enrolment
    /// code. The raw code is shown to the issuing Platform Owner exactly once
    /// and is never written to the database, audit record, URL, or logs.
    /// </summary>
    public string CodeHash { get; set; } = default!;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ClaimedAtUtc { get; set; }

    public DateTimeOffset? PreparedAtUtc { get; set; }

    public DateTimeOffset? TotpVerifiedAtUtc { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public DateTimeOffset? CancelledAtUtc { get; set; }

    public string Status { get; set; } = MemOperatorEnrollmentGrantStatuses.Active;
}

public static class MemOperatorEnrollmentGrantStatuses
{
    public const string Active = "active";
    public const string Claimed = "claimed";
    public const string Prepared = "prepared";
    public const string TotpVerified = "totp_verified";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static bool IsClaimedAndCurrent(string status) =>
        status is Claimed or Prepared or TotpVerified;

    public static bool IsLive(string status) =>
        status is Active or Claimed or Prepared or TotpVerified;
}
