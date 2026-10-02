using System.Security.Claims;

namespace Modules.Operator.Dashboard;

/// <summary>
/// Browser-safe, machine-oriented Home dashboard projection. This contract is
/// intentionally composed from existing durable state and narrow live
/// observations; it must not contain raw errors, host paths, Docker IDs,
/// internal targets, credentials, or validation identifiers.
/// </summary>
public sealed record DashboardOverviewResponse(
    string Source,
    DateTimeOffset GeneratedAtUtc,
    int SuggestedRefreshSeconds,
    DashboardCapabilities Capabilities,
    DashboardHero Hero,
    DashboardOnboarding Onboarding,
    DashboardPlatformSummary Platform,
    DashboardPublicAccessSummary PublicAccess,
    DashboardStacksSummary Stacks,
    DashboardRecoverySummary Recovery,
    DashboardHostSummary Host,
    DashboardActivitySummary Activity,
    IReadOnlyList<DashboardNotice> Notices);

public sealed record DashboardCapabilities(
    bool CanOperate,
    bool CanManagePlatform);

public sealed record DashboardHero(
    string State,
    string HeadlineCode,
    DashboardAction? Action);

public sealed record DashboardAction(
    string Code,
    string? StackSlug = null,
    string? RestoreSessionId = null);

public sealed record DashboardOnboarding(
    string State,
    IReadOnlyList<string> CompletedStepCodes,
    IReadOnlyList<DashboardOnboardingStep> NextSteps);

public sealed record DashboardOnboardingStep(
    string Code,
    string State,
    DashboardAction? Action);

public sealed record DashboardPlatformSummary(
    string State,
    IReadOnlyList<DashboardServiceSummary> Services,
    int RequiredServiceCount,
    int RunningRequiredServiceCount,
    DashboardDockerSummary Docker);

public sealed record DashboardDockerSummary(
    string State,
    DateTimeOffset? ObservedAtUtc);

public sealed record DashboardServiceSummary(
    string Key,
    string Requirement,
    string State,
    DateTimeOffset? ObservedAtUtc);

public sealed record DashboardPublicAccessSummary(
    string State,
    string? MainDomain,
    DashboardCertificateSummary Certificate,
    DashboardIngressSummary Ingress,
    DateTimeOffset? LastPlatformRouteVerificationAtUtc);

public sealed record DashboardCertificateSummary(
    string State,
    string? CommonName,
    DateTimeOffset? ExpiresAtUtc,
    string? RenewalState = null,
    DateTimeOffset? RenewalNextAttemptAtUtc = null);

public sealed record DashboardIngressSummary(
    string State,
    DateTimeOffset? ObservedAtUtc);

public sealed record DashboardStacksSummary(
    int Total,
    int HealthyLastVerifiedCount,
    int AttentionCount,
    IReadOnlyList<DashboardStackSummary> Items,
    bool Truncated);

public sealed record DashboardStackSummary(
    string StackId,
    string Slug,
    DashboardLastVerification LastVerification,
    string? MatrixPublicBaseUrl,
    string? ElementPublicBaseUrl);

public sealed record DashboardLastVerification(
    string State,
    DateTimeOffset? VerifiedAtUtc);

public sealed record DashboardRecoverySummary(
    string State,
    int ManagedStackCount,
    int StacksWithValidRecoveryPointCount,
    int CatalogEntryCount,
    int AvailableCatalogEntryCount,
    int ValidCatalogEntryCount,
    int WarningCatalogEntryCount,
    int InvalidCatalogEntryCount,
    DateTimeOffset? LatestCapturedAtUtc,
    long? TotalPayloadBytes,
    int ActiveRestoreCount,
    int AttentionRestoreCount,
    string? PriorityRestoreSessionId);

public sealed record DashboardHostSummary(
    string State,
    DateTimeOffset? ObservedAtUtc,
    string? UnavailableReasonCode,
    string? OperatingSystem,
    string? Architecture,
    long? CpuCount,
    long? MemoryTotalBytes,
    string? DockerServerVersion,
    long? ContainerCount,
    long? ImageCount,
    DashboardDiskUsage? Disk,
    string? StorageUnavailableReasonCode);

public sealed record DashboardDiskUsage(
    long UsedBytes,
    long TotalBytes,
    string Scope);

public sealed record DashboardActivitySummary(
    string State,
    IReadOnlyList<DashboardActivityItem> Items);

public sealed record DashboardActivityItem(
    string Id,
    string Kind,
    string Severity,
    DateTimeOffset OccurredAtUtc,
    string TitleCode,
    string? DetailCode,
    string? StackSlug,
    string? RestoreSessionId);

public sealed record DashboardNotice(
    string Id,
    string Severity,
    string Code,
    DashboardAction? Action);

internal static class DashboardStates
{
    public const string Ready = "ready";
    public const string Attention = "attention";
    public const string Degraded = "degraded";
    public const string VerificationLimited = "verification_limited";
    public const string Unavailable = "unavailable";
    public const string Unknown = "unknown";

    public const string Available = "available";
    public const string Empty = "empty";

    public const string Running = "running";
    public const string Stopped = "stopped";
    public const string NotDeployed = "not_deployed";

    public const string Responsive = "responsive";

    public const string Valid = "valid";
    public const string Renewing = "renewing";
    public const string Expiring = "expiring";
    public const string Expired = "expired";
    public const string Missing = "missing";
    public const string Staging = "staging";
    public const string NotConfigured = "not_configured";

    public const string Passed = "passed";
    public const string Failed = "failed";

    public const string NotApplicable = "not_applicable";
    public const string NoRecoveryPoint = "no_recovery_point";
    public const string PartialCoverage = "partial_coverage";
    public const string Covered = "covered";

    public const string Required = "required";
    public const string Optional = "optional";

    public const string HostObservationFailed = "host_observation_failed";
    public const string HostStorageContainerizedUnavailable = "containerized_host_filesystem_unavailable";
    public const string HostStorageUnavailable = "host_filesystem_unavailable";
    public const string MemDataStorageUnavailable = "mem_data_filesystem_unavailable";
}

internal static class DashboardActions
{
    public const string CreateChatServer = "create_chat_server";
    public const string ManageDomains = "manage_domains";
    public const string OpenServices = "open_services";
    public const string OpenBackups = "open_backups";
    public const string OpenRestores = "open_restores";
    public const string OpenRestoreWorkspace = "open_restore_workspace";
    public const string OpenDiagnostics = "open_diagnostics";
}

internal static class DashboardCapabilityResolver
{
    public static DashboardCapabilities Resolve(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var canOperate =
            principal.IsInRole(Modules.Auth.Identity.MemOperatorRoles.PlatformOwner) ||
            principal.IsInRole(Modules.Auth.Identity.MemOperatorRoles.Operator);

        var canManagePlatform = principal.IsInRole(
            Modules.Auth.Identity.MemOperatorRoles.PlatformOwner);

        return new DashboardCapabilities(canOperate, canManagePlatform);
    }
}
