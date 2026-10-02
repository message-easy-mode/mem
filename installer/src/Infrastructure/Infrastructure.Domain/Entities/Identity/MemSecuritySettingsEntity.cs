namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// Singleton, server-owned security settings for the local MEM control plane.
/// These values are policy inputs only; the browser may display and request
/// changes, but endpoint enforcement always reads the persisted server state.
/// </summary>
public sealed class MemSecuritySettingsEntity
{
    public static readonly Guid SingletonId = Guid.Parse("0d5a9fa5-4d92-4b75-970e-1f7bcd8b7f73");

    public Guid Id { get; set; } = SingletonId;

    /// <summary>
    /// When true, selected destructive or access-changing actions require a
    /// fresh password-plus-current-TOTP step-up grant in addition to the normal
    /// named operator session and role authorization.
    /// </summary>
    public bool RequireHighRiskStepUp { get; set; } = true;

    /// <summary>
    /// Lifetime, in minutes, for a successful high-risk identity verification
    /// grant. The grant remains bound to one server-managed browser session and
    /// is still invalidated by logout, session revocation, security-stamp
    /// changes, MFA changes, password changes, role changes, account disablement,
    /// and expiry.
    /// </summary>
    public int HighRiskStepUpGrantMinutes { get; set; } = MemSecuritySettingsDefaults.RecommendedStepUpGrantMinutes;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public Guid? UpdatedByOperatorId { get; set; }

    /// <summary>
    /// Provider-neutral optimistic concurrency token. SQLite does not provide
    /// SQL Server-style rowversion semantics, so MEM rotates this string on
    /// every settings update.
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}

public static class MemSecuritySettingsDefaults
{
    public const bool RequireHighRiskStepUp = true;
    public const int RecommendedStepUpGrantMinutes = 15;

    public static readonly int[] AllowedHighRiskStepUpGrantMinutes = [5, 15, 30, 60];

    public static bool IsAllowedHighRiskStepUpGrantMinutes(int value) =>
        AllowedHighRiskStepUpGrantMinutes.Contains(value);
}
