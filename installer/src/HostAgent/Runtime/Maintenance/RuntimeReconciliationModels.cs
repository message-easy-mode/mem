namespace HostAgent.Runtime.Maintenance;

public sealed record RuntimeReconciliationReport(
    string Source,
    string Status,
    DateTimeOffset CheckedAtUtc,
    RuntimeReconciliationSummary Summary,
    IReadOnlyList<RuntimeReconciliationActiveStack> ActiveStacks,
    IReadOnlyList<RuntimeReconciliationDestroyedStack> DestroyedStacks,
    IReadOnlyList<RuntimeReconciliationRoute> ActiveRoutes,
    IReadOnlyList<RuntimeReconciliationNpmProxyHost> NpmProxyHosts,
    IReadOnlyList<RuntimeReconciliationNpmProxyHost> OrphanedNpmProxyHosts,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeReconciliationSummary(
    int ActiveStackCount,
    int DestroyedStackHistoryCount,
    int ActiveRouteCount,
    int NpmProxyHostCount,
    int MemManagedNpmProxyHostCount,
    int OrphanedNpmProxyHostCount,
    int ManifestCount,
    int ActiveDatabaseRowsWithoutManifestCount,
    int ManifestWithoutActiveDatabaseRowCount,
    int RemovableManifestlessStackCount);

public sealed record RuntimeReconciliationActiveStack(
    Guid StackId,
    string Slug,
    string Status,
    string? LastVerifiedStatus,
    DateTime? LastVerifiedAtUtc,
    string? MatrixPublicBaseUrl,
    string? ElementPublicBaseUrl,
    bool HasManifest,
    int RecordedServiceCount,
    int RecordedRouteCount,
    int MatchingNpmRouteCount,
    int MissingNpmRouteCount,
    int MismatchedNpmRouteCount,
    string ReconciliationState,
    bool CanRemoveFromReconciliation,
    string? RemovalActionCode,
    string? Reason);

public sealed record RuntimeReconciliationDestroyedStack(
    Guid StackId,
    string Slug,
    string Status,
    string? LastVerifiedStatus,
    DateTime? LastVerifiedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record RuntimeReconciliationRoute(
    Guid RouteId,
    Guid RuntimeStackId,
    string StackSlug,
    string ServiceKey,
    string Provider,
    string PublicHost,
    string ForwardHost,
    int ForwardPort,
    int? NpmCertificateId,
    string? ProviderRouteId,
    string Status);

public sealed record RuntimeReconciliationNpmProxyHost(
    int ProxyHostId,
    IReadOnlyList<string> DomainNames,
    string? ForwardHost,
    int? ForwardPort,
    int? CertificateId,
    bool Enabled,
    bool NginxOnline,
    bool LooksLikeMemStackRoute,
    bool MatchesActiveRoute,
    IReadOnlyList<string> MatchedActiveRouteHosts,
    string? Reason);

public sealed record RuntimeReconciliationStackRecoveryDecision(
    string State,
    bool CanRemove,
    string? ActionCode,
    int RecordedServiceCount,
    int RecordedRouteCount,
    int MatchingNpmRouteCount,
    int MissingNpmRouteCount,
    int MismatchedNpmRouteCount,
    string Reason);

public sealed record RuntimeReconciliationNpmProxyHostCleanupRequest(
    IReadOnlyList<int> ProxyHostIds,
    string? ConfirmationText);

public sealed record RuntimeReconciliationNpmProxyHostCleanupResponse(
    string Source,
    string Status,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<int> RequestedProxyHostIds,
    int DeletedCount,
    int AlreadyMissingCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<RuntimeReconciliationNpmProxyHostCleanupResult> Results,
    string? Detail);

public sealed record RuntimeReconciliationNpmProxyHostCleanupResult(
    int ProxyHostId,
    IReadOnlyList<string> DomainNames,
    string Status,
    string? Reason);
