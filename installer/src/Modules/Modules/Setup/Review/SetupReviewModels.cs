namespace Modules.Setup.Review;

public sealed record SetupReviewResponse(
    Guid? InstallationId,
    string InstallationStatus,
    bool CanAccept,
    bool ReviewAccepted,
    string Message,
    string? ErrorCode,
    string? PlanSha256,
    DateTime? ReviewedAtUtc,
    SetupReviewPreflightSummary Preflight,
    SetupReviewDomainSummary Domain,
    SetupReviewNpmAdministratorSummary NpmAdministrator,
    SetupReviewPlatformSummary Platform,
    IReadOnlyList<string> PlannedActions,
    IReadOnlyList<string> WillNotChange,
    IReadOnlyList<string> Blockers);

public sealed record SetupReviewPreflightSummary(
    bool Available,
    bool Ready,
    string? RunId,
    DateTimeOffset? CompletedAtUtc,
    int Passed,
    int Warnings,
    int Failed,
    int Skipped,
    int Unavailable,
    int Unknown,
    int BlockingIssueCount);

public sealed record SetupReviewDomainSummary(
    bool Validated,
    string BaseDomain,
    string WildcardCertificate,
    string DnsProvider,
    string AcmeEmail,
    string CertificateEnvironment,
    bool ProviderAccessConfirmed,
    bool ProviderCredentialStored);

public sealed record SetupReviewNpmAdministratorSummary(
    string? AdministratorEmail,
    bool CredentialStored,
    DateTime? VerifiedAtUtc);

public sealed record SetupReviewPlatformSummary(
    string NetworkName,
    string PostgresContainerName,
    string PostgresVolumeName,
    string NpmContainerName,
    int NpmHttpPort,
    int NpmHttpsPort,
    int NpmAdminPort,
    string CoturnContainerName,
    string CoturnPublicHost,
    int CoturnTurnPort,
    string CoturnRelayPortRange,
    IReadOnlyList<string> EnabledSupportTools);
