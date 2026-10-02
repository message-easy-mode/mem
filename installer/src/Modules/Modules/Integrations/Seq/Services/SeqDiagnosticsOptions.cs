namespace Modules.Integrations.Seq.Services;

/// <summary>
/// Single configuration authority for the optional Seq integration.
/// MEM's local CLEF recorder and safe diagnostic event store remain mandatory
/// and independent of Seq.
/// </summary>
public sealed class SeqDiagnosticsOptions
{
    public const string SectionName = "Diagnostics:Seq";
    public const string DefaultIngestionUrl = "http://seq:5341";
    public const string DefaultHealthUrl = "http://seq:80";
    public const int DefaultPreferredHostPort = 15341;

    public bool SinkEnabled { get; set; }

    public bool ManagementEnabled { get; set; }

    /// <summary>
    /// Internal Seq base URL used by the Serilog sink and health probe.
    /// This value is never returned to ordinary browser clients.
    /// </summary>
    public string? IngestionUrl { get; set; } = DefaultIngestionUrl;

    /// <summary>
    /// Optional operator-approved Seq browser URL. When absent after a
    /// verified guided deployment, MEM may derive a safe browser authority
    /// from the active runtime context and actual published host port.
    /// </summary>
    public string? UiUrl { get; set; }

    /// <summary>
    /// Internal Seq API URL used only for bounded health probing.
    /// This is separate from the ingestion-only listener.
    /// </summary>
    public string? HealthUrl { get; set; } = DefaultHealthUrl;

    public string ApiKeyEnvironmentVariableName { get; set; } = "MEM_SEQ_API_KEY";

    public string? ApiKeyFilePath { get; set; } =
        "/data/secrets/seq/ingestion-api-key";

    public string AdminPasswordHashEnvironmentVariableName { get; set; } =
        "MEM_SEQ_ADMIN_PASSWORD_HASH";

    public string? AdminPasswordHashFilePath { get; set; } =
        "/data/secrets/seq/admin-password-hash";

    public bool EulaAccepted { get; set; }

    public string ApprovedImageReference { get; set; } =
        "datalust/seq:2026.1.17044";

    public string ExpectedVersion { get; set; } = "2026.1.17044";

    /// <summary>
    /// Ordinary lifecycle operations must never pull an image.
    /// </summary>
    public bool AllowOperationalPull { get; set; }

    /// <summary>
    /// The explicit guided setup boundary may prepare only the approved exact
    /// Seq image. This does not relax the operational-pull prohibition.
    /// </summary>
    public bool AllowSetupPull { get; set; } = true;

    public string HostDataPath { get; set; } = "/var/lib/message-easy-mode/seq";

    public int PreferredHostPort { get; set; } = DefaultPreferredHostPort;

    public int ProbeTimeoutSeconds { get; set; } = 5;

    public int ProbeIntervalSeconds { get; set; } = 30;

    public int PasswordHashTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Username of the initial local Seq administrator created during guided
    /// bootstrap. The one-time password is never stored in this policy.
    /// </summary>
    public string AdministratorUserName { get; set; } = "admin";

    public int MaximumAdministratorPasswordLength { get; set; } = 256;

    public int ConnectionTimeoutSeconds { get; set; } = 20;

    public int ConnectionVerificationAttemptCount { get; set; } = 15;

    public int ConnectionVerificationPollIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// Number of bounded health attempts used after guided first-time deployment.
    /// Ordinary lifecycle health checks remain single, explicit probes.
    /// </summary>
    public int BootstrapHealthAttemptCount { get; set; } = 45;

    public int BootstrapHealthPollIntervalSeconds { get; set; } = 2;

    /// <summary>
    /// Server-owned root for Seq secret files created by guided setup.
    /// </summary>
    public string SecretRootPath { get; set; } = "/data/secrets/seq";

    /// <summary>
    /// Non-secret guided setup state. The file never contains a password hash,
    /// API-key token, cookie, or authorization header.
    /// </summary>
    public string BootstrapStatePath { get; set; } =
        "/data/diagnostics/seq-bootstrap.json";

    /// <summary>
    /// Server-owned preference file used to stage Seq delivery changes.
    /// The desired value is applied to configuration during the next API start,
    /// before Serilog is created. The file contains no secret material.
    /// </summary>
    public string DeliveryStatePath { get; set; } =
        "/data/diagnostics/seq-delivery.json";

    /// <summary>
    /// Safe startup-only warning set when desired delivery could not be
    /// activated from verified server-owned bootstrap state. This value is
    /// never persisted by the browser and contains no secret material.
    /// </summary>
    public string? DeliveryStartupWarningCode { get; set; }
}
