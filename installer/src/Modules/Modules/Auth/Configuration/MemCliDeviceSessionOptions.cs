namespace Modules.Auth.Configuration;

/// <summary>
/// Server-owned bounds for browser-approved MEM CLI device authorizations and
/// device sessions. CLI users cannot relax these values through local profiles
/// or command-line options.
/// </summary>
public sealed class MemCliDeviceSessionOptions
{
    public const string SectionName = "MemCliDeviceSessions";

    /// <summary>
    /// Short lifetime of a pending browser approval.
    /// </summary>
    public int AuthorizationAttemptMinutes { get; set; } = 10;

    /// <summary>
    /// Maximum idle time of an approved CLI device session.
    /// </summary>
    public int SessionIdleMinutes { get; set; } = 8 * 60;

    /// <summary>
    /// Absolute maximum lifetime of an approved CLI device session.
    /// </summary>
    public int SessionAbsoluteHours { get; set; } = 7 * 24;

    /// <summary>
    /// Shared anonymous budget for starting CLI device authorizations. MEM
    /// intentionally does not partition this by forwarded IP because the
    /// private topology has not yet established a trustworthy proxy identity
    /// contract.
    /// </summary>
    public int AuthorizationStartRateLimitPermitLimit { get; set; } = 10;

    /// <summary>
    /// Duration of the shared anonymous start budget window.
    /// </summary>
    public int AuthorizationStartRateLimitWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Shared anonymous budget for CLI polling. It permits one bounded normal
    /// device flow while limiting brute-force verifier attempts and polling
    /// floods before a future per-client trusted-address contract exists.
    /// </summary>
    public int AuthorizationPollRateLimitPermitLimit { get; set; } = 60;

    /// <summary>
    /// Duration of the shared anonymous poll budget window.
    /// </summary>
    public int AuthorizationPollRateLimitWindowSeconds { get; set; } = 60;
}
