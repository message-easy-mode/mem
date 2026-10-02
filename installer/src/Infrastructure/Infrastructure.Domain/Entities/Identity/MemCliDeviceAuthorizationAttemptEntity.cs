namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// A short-lived, browser-approved CLI device authorization attempt.
///
/// The displayed user code and the CLI-held verifier are never persisted raw.
/// This record contains only one-way digests, safe lifecycle state, timestamps,
/// an installation binding, and safe device display metadata.
/// </summary>
public sealed class MemCliDeviceAuthorizationAttemptEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// The MEM installation identity that issued this attempt. A credential
    /// created from the attempt must not be accepted by a different installation.
    /// </summary>
    public Guid InstallationId { get; set; }

    /// <summary>
    /// SHA-256 digest of the short user-entered code. The raw code is returned
    /// only to the initiating CLI process and never written to audit data.
    /// </summary>
    public string UserCodeHash { get; set; } = default!;

    /// <summary>
    /// SHA-256 challenge supplied by the CLI. Polling must later prove
    /// possession of the corresponding raw high-entropy verifier.
    /// </summary>
    public string VerifierHash { get; set; } = default!;

    /// <summary>
    /// Safe, bounded display label shown only to the approving named operator.
    /// It is not trusted as an authorization input and is never written to
    /// audit reason fields.
    /// </summary>
    public string DeviceLabel { get; set; } = default!;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Guid? ApprovedByOperatorId { get; set; }

    public DateTimeOffset? DeniedAtUtc { get; set; }

    public Guid? DeniedByOperatorId { get; set; }

    public DateTimeOffset? ConsumedAtUtc { get; set; }

    public string Status { get; set; } = MemCliDeviceAuthorizationAttemptStatuses.Pending;
}

public static class MemCliDeviceAuthorizationAttemptStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const string Consumed = "consumed";
    public const string Expired = "expired";

    public static bool IsTerminal(string status) =>
        status is Denied or Consumed or Expired;
}
