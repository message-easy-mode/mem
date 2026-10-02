namespace Mem.Cli.Models;

public sealed record RuntimeReconciliationSummary(
    int ActiveStackCount,
    int DestroyedStackHistoryCount,
    int ActiveRouteCount,
    int NpmProxyHostCount,
    int MemManagedNpmProxyHostCount,
    int OrphanedNpmProxyHostCount,
    int ManifestCount,
    int ActiveDatabaseRowsWithoutManifestCount,
    int ManifestWithoutActiveDatabaseRowCount);

public sealed record RuntimeReconciliationRoute(
    Guid RouteId,
    Guid RuntimeStackId,
    string StackSlug,
    string ServiceKey,
    string Provider,
    string PublicHost,
    string? ProviderRouteId,
    string Status);

public sealed record RuntimeReconciliationNpmProxyHost(
    int ProxyHostId,
    IReadOnlyList<string> DomainNames,
    string? ForwardHost,
    int? ForwardPort,
    bool Enabled,
    bool NginxOnline,
    bool LooksLikeMemStackRoute,
    bool MatchesActiveRoute,
    IReadOnlyList<string> MatchedActiveRouteHosts,
    string? Reason);

public sealed record RuntimeReconciliationReport(
    string Source,
    string Status,
    DateTimeOffset CheckedAtUtc,
    RuntimeReconciliationSummary Summary,
    IReadOnlyList<RuntimeReconciliationRoute> ActiveRoutes,
    IReadOnlyList<RuntimeReconciliationNpmProxyHost> OrphanedNpmProxyHosts,
    IReadOnlyList<string> Warnings,
    string? Detail);

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
