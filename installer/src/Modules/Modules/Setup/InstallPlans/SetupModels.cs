using Modules.Setup.InstallRuns;

namespace Modules.Setup.InstallPlans;

public sealed record InstallPlanResponse(
    Guid Id,
    string Status,
    string? ConfigJson,
    string? FrozenConfigJson,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record InstallStepExecutionResponse(
    Guid Id,
    string StepName,
    int Sequence,
    string Status,
    string? Message,
    string? ErrorMessage,
    int AttemptCount,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    InstallProgressSnapshot? Progress = null);

public sealed record RunInstallPlanResponse(
    Guid InstallationId,
    bool Accepted,
    string Status,
    string Message);

public sealed record RemoveInstallPlanResponse(
    Guid InstallationId,
    bool Success,
    string Message);

public sealed record InstallPlan(
    GeneralSetupConfig General,
    PlatformSetupConfig Platform,
    PublicAccessSetupConfig PublicAccess,
    SupportToolsSetupConfig SupportTools,
    PreflightSetupConfig? Preflight = null,
    ReviewSetupConfig? Review = null);

public sealed record ReviewSetupConfig(
    DateTime AcceptedAtUtc,
    string PlanSha256);

public sealed record PreflightSetupConfig(
    string RunId,
    DateTimeOffset CompletedAtUtc,
    string RunStatus,
    int Passed,
    int Warnings,
    int Failed,
    int Skipped,
    int Unavailable,
    int Unknown,
    int BlockingIssueCount,
    IReadOnlyList<string> WarningCheckKeys,
    IReadOnlyList<string> UnavailableCheckKeys);

public sealed record GeneralSetupConfig(
    string Mode,
    string? InstallName,
    string? PublicBaseHostname);

public sealed record PlatformSetupConfig(
    PostgresSetupConfig Postgres,
    IngressSetupConfig Ingress,
    MemApiSetupConfig MemApi,
    MemWebSetupConfig MemWeb);

public sealed record PostgresSetupConfig(
    bool Enabled,
    string ContainerName,
    string DatabaseName,
    string Username,
    bool UseDockerVolume,
    string VolumeName);

public sealed record IngressSetupConfig(
    string Provider,
    bool Enabled,
    string ContainerName,
    int HttpPort,
    int HttpsPort,
    int AdminPort,
    bool UseExistingIfDetected);

public sealed record MemApiSetupConfig(
    bool Enabled,
    string ContainerName,
    string Image,
    int InternalPort);

public sealed record MemWebSetupConfig(
    bool Enabled,
    string ContainerName,
    string Image,
    int InternalPort);

public sealed record PublicAccessSetupConfig(
    string Domain,
    string Zone,
    string AcmeEmail,
    string DnsProvider,
    bool UseStaging,
    string? CertificateId,
    int? NpmCertificateId,
    string? ProxyHostDomain,
    string? ForwardHost,
    int? ForwardPort,
    string ForwardScheme,
    bool CertificateValidated,
    bool ImportedToNpm,
    bool ProxyHostVerified,
    DateTime? LastVerifiedAtUtc,
    DomainPreparationSetupConfig? Preparation = null);

public sealed record DomainPreparationSetupConfig(
    string Status,
    DateTime ValidatedAtUtc,
    bool ProviderAccessConfirmed,
    bool ProviderCredentialStored);

public sealed record SupportToolsSetupConfig(
    SeqSetupConfig Seq,
    PgAdminSetupConfig PgAdmin,
    PortainerSetupConfig Portainer);

public sealed record SeqSetupConfig(
    bool Enabled,
    string ContainerName,
    int HostPort);

public sealed record PgAdminSetupConfig(
    bool Enabled,
    string ContainerName,
    int HostPort);

public sealed record PortainerSetupConfig(
    bool Enabled,
    bool UseExistingIfDetected,
    string ContainerName,
    int HostPort);

public sealed record UpdateGeneralConfigRequest(
    string Mode,
    string? InstallName,
    string? PublicBaseHostname);