// HostAgent/Runtime/Coturn/CoturnRuntimeModels.cs

namespace HostAgent.Runtime.Coturn;

public sealed record CoturnEnsureRequest(
    string? ExternalIp = null,
    bool ForceRecreate = false,
    bool PublishRelayPorts = true);

public sealed record CoturnRuntimeResponse(
    string Source,
    string Status,
    string ContainerState,
    string Readiness,
    string ServiceKey,
    string ContainerName,
    string Image,
    string ApprovedImageReference,
    string? ResolvedImageId,
    bool ImageApproved,
    bool ContainerExists,
    bool Running,
    bool OwnershipVerified,
    string? ContainerId,
    string? DockerState,
    string OperatorStatus,
    bool RuntimeExact,
    CoturnDockerRuntimeEvidence? DockerRuntime,
    IReadOnlyList<string> RuntimeDrift,
    string ProtectedEvidenceAccess,
    string Realm,
    string PublicHost,
    int TurnPort,
    int RelayMinPort,
    int RelayMaxPort,
    IReadOnlyList<string> TurnUris,
    bool SecretPresent,
    string SecretSource,
    string SecretStorage,
    bool SecretFilePermissionsApplied,
    string ExpectedBaseDomain,
    string? ConfiguredBaseDomain,
    bool DomainDriftDetected,
    bool Recreated,
    string? ExternalIp,
    bool RelayPortsPublished,
    bool SecurityPolicyApplied,
    string SecurityPolicyVersion,
    IReadOnlyList<string> PublishedPorts,
    IReadOnlyList<string> RequiredProductionFirewallPorts,
    IReadOnlyList<string> Warnings,
    string? Detail)
{
    /// <summary>
    /// Browser-safe presence flag for the protected Coturn configuration.
    /// It intentionally does not expose the protected host path or content.
    /// </summary>
    public bool ConfigurationPresent { get; init; }

    /// <summary>
    /// Browser-safe structural truth that the protected configuration exists,
    /// matches the container label hash, has the required permissions, and
    /// satisfies the current Coturn security policy. No secret value or path is
    /// exposed by this projection.
    /// </summary>
    public bool ConfigurationExact { get; init; }
}


public static class CoturnOperatorStatuses
{
    public const string RuntimeReady = "runtime-ready";
    public const string VerificationLimited = "verification-limited";
    public const string NeedsAttention = "needs-attention";
    public const string RepairRequired = "repair-required";
    public const string Stopped = "stopped";
    public const string NotDeployed = "not-deployed";
    public const string Conflict = "conflict";
    public const string Unknown = "unknown";
}

public static class CoturnProtectedEvidenceAccess
{
    public const string Available = "available";
    public const string Restricted = "restricted";
    public const string Unavailable = "unavailable";
}

public sealed record CoturnDockerRuntimeEvidence(
    string RestartPolicy,
    string ExpectedRestartPolicy,
    bool RestartPolicyMatches,
    long RestartCount,
    bool Restarting,
    bool Paused,
    long ExitCode,
    bool OomKilled,
    bool Dead,
    bool StateErrorPresent,
    string? HealthStatus,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? NetworkMode,
    string ExpectedNetwork,
    bool NetworkModeMatches,
    IReadOnlyList<string> AttachedNetworks,
    IReadOnlyList<string> ExpectedNetworkAliases,
    IReadOnlyList<string> ObservedExpectedNetworkAliases,
    bool ExpectedNetworkAttached,
    bool NetworkAliasesMatch,
    bool ConfigMountPresent,
    bool ConfigMountReadOnly,
    bool ConfigMountSourceMatches,
    bool ConfigMountDestinationMatches,
    bool CommandMatches,
    bool StartupUserMatches);

public sealed record CoturnSynapseConfig(
    string PublicHost,
    string Realm,
    IReadOnlyList<string> TurnUris,
    string SharedSecret,
    string UserLifetime,
    bool AllowGuests,
    bool RelayPortsPublished,
    string ExpectedBaseDomain);

public static class CoturnCheckStatuses
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Warning = "warning";
    public const string NotRun = "not-run";
}

public sealed record CoturnCheckItem(
    string Key,
    string Status,
    string Summary,
    string? Detail = null)
{
    // Stable, optional presentation code. Older persisted checks have no code
    // and must not be treated as fresh advertised-address verification.
    public string? Code { get; init; }
}

public sealed record CoturnAdvertisedRelay(string Address, int Port);

public sealed record CoturnRelayAddressEvidence(
    bool Complete,
    IReadOnlyList<CoturnAdvertisedRelay> Relays);

public sealed record CoturnAllocationProbeResponse(
    string Status,
    string Transport,
    string Summary,
    string? LogTail)
{
    // Parsed before log-tail truncation. Contains only validated IPs and ports,
    // never TURN credentials or arbitrary process output.
    public CoturnRelayAddressEvidence? RelayAddressEvidence { get; init; }
}

public sealed record CoturnCheckResponse(
    string Source,
    string Status,
    DateTimeOffset CheckedAtUtc,
    DateTimeOffset FreshUntilUtc,
    string ContainerState,
    string Readiness,
    string PublicHost,
    string? RuntimeContainerId,
    DateTimeOffset? RuntimeStartedAtUtc,
    long? RuntimeRestartCount,
    IReadOnlyList<CoturnCheckItem> Checks,
    CoturnAllocationProbeResponse Allocation,
    IReadOnlyList<string> Warnings,
    string? Detail,
    bool EvidencePersisted = false,
    string? IncidentId = null);

public static class CoturnCheckFreshnessStatuses
{
    public const string Fresh = "fresh";
    public const string Stale = "stale";
    public const string RuntimeChanged = "runtime-changed";
    public const string NotChecked = "not-checked";
    public const string Unavailable = "unavailable";
}

public sealed record CoturnCheckFreshnessEvaluation(
    string Freshness,
    bool Fresh,
    DateTimeOffset? FreshUntilUtc);

public sealed record CoturnLatestCheckResponse(
    string Source,
    string Freshness,
    bool Fresh,
    int FreshForSeconds,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset? CheckedAtUtc,
    DateTimeOffset? FreshUntilUtc,
    string? IncidentId,
    CoturnCheckResponse? Result,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record CoturnLogsResponse(
    string Source,
    string Status,
    DateTimeOffset RetrievedAtUtc,
    string ContainerName,
    int RequestedTail,
    int ReturnedLines,
    bool Truncated,
    string Content,
    IReadOnlyList<string> Warnings);
