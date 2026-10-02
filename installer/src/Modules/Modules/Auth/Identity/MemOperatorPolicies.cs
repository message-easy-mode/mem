namespace Modules.Auth.Identity;

/// <summary>
/// Named server-side policies for the local Identity authority model.
/// SEC-AUTH-02A registers them but does not yet bind existing operational
/// endpoints; SEC-AUTH-04 maps endpoint capabilities deliberately.
/// </summary>
public static class MemOperatorPolicies
{
    public const string ReadSafeStatus = "mem.operator.read-safe-status";
    public const string Operate = "mem.operator.operate";
    public const string ManagePlatform = "mem.operator.manage-platform";

    /// <summary>
    /// Requires an authenticated opaque MEM CLI device session. This is kept
    /// separate from browser-cookie policies so a CLI credential cannot be
    /// presented as a substitute for browser-only approval, recovery, or
    /// password-plus-TOTP step-up endpoints.
    /// </summary>
    public const string CliDeviceSession = "mem.operator.cli-device-session";

    /// <summary>
    /// Allows the existing browser operator roles to manage migration intakes,
    /// while allowing only a Platform Owner CLI device session to drive the
    /// automated MM-05C target-import workflow. This does not authorize
    /// browser-only step-up, recovery, bootstrap, or acceptance surfaces.
    /// </summary>
    public const string MigrationIntakeOperate =
        "mem.operator.migration-intake-operate";

    /// <summary>
    /// Requires fresh, session-bound password plus current-TOTP confirmation.
    /// It is intentionally separate from role policies so SEC-AUTH-06B can
    /// compose it with the exact high-risk capability being protected.
    /// </summary>
    public const string RecentStepUp = "mem.operator.recent-step-up";
}
