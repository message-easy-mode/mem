namespace Modules.Auth.Configuration;

/// <summary>
/// Stable, local-control-plane defaults for ASP.NET Core Identity.
/// Values may be tuned through configuration, but the extension refuses
/// values weaker than this foundation's minimums.
/// </summary>
public sealed class MemOperatorIdentityOptions
{
    public const string SectionName = "MemOperatorIdentity";

    public string CookieName { get; set; } = "mem_operator_auth";

    public int CookieIdleMinutes { get; set; } = 30;

    public string BootstrapCookieName { get; set; } = "mem_bootstrap_auth";

    public int BootstrapGrantMinutes { get; set; } = 15;

    public int BootstrapRecoveryCodeCount { get; set; } = 10;

    /// <summary>
    /// Cookie name for a short-lived, one-time local-operator enrolment grant.
    /// It is never a normal operator session.
    /// </summary>
    public string EnrollmentCookieName { get; set; } = "mem_operator_enrollment";

    public int EnrollmentGrantMinutes { get; set; } = 15;

    public int EnrollmentRecoveryCodeCount { get; set; } = 10;

    /// <summary>
    /// Number of replacement recovery codes issued when a fully enrolled
    /// operator regenerates their own set after a fresh step-up.
    /// </summary>
    public int OperatorRecoveryCodeCount { get; set; } = 10;

    public int PasswordMinimumLength { get; set; } = 14;

    public int PasswordRequiredUniqueChars { get; set; } = 4;

    public int LockoutMinutes { get; set; } = 15;

    public int LockoutMaxFailedAccessAttempts { get; set; } = 5;

    /// <summary>
    /// Shared fixed-window budget for anonymous password and TOTP submissions.
    /// This intentionally limits the low-throughput MEM control plane as a
    /// whole rather than trusting unverified forwarded client-IP headers.
    /// </summary>
    public int LoginRateLimitPermitLimit { get; set; } = 20;

    /// <summary>
    /// Duration of the shared anonymous sign-in request window.
    /// </summary>
    public int LoginRateLimitWindowSeconds { get; set; } = 60;

    /// <summary>
    /// Lifetime of a successful password-plus-current-TOTP step-up grant. The
    /// grant is also bound to the current protected browser session and
    /// invalidated when the Identity security stamp changes.
    /// </summary>
    public int StepUpGrantMinutes { get; set; } = 15;

    /// <summary>
    /// Zero validates the Identity security stamp on every operator-cookie request.
    /// The control plane is low-throughput and security-sensitive, so this is the
    /// intentionally conservative v0.1.1 default.
    /// </summary>
    public int SecurityStampValidationSeconds { get; set; } = 0;
}
