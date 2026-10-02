using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Runtime.Maintenance;

public sealed class RuntimeReconciliationReportService
{
    private readonly MemDbContext _db;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly NpmProxyHostService _npmProxyHostService;
    private readonly ILogger<RuntimeReconciliationReportService> _log;

    public RuntimeReconciliationReportService(
        MemDbContext db,
        RuntimeStackManifestStore manifestStore,
        NpmProxyHostService npmProxyHostService,
        ILogger<RuntimeReconciliationReportService> log)
    {
        _db = db;
        _manifestStore = manifestStore;
        _npmProxyHostService = npmProxyHostService;
        _log = log;
    }

    public async Task<RuntimeReconciliationReport> BuildAsync(CancellationToken ct)
    {
        var checkedAtUtc = DateTimeOffset.UtcNow;
        var warnings = new List<string>();

        var manifests = await _manifestStore.ListAsync(ct);
        var manifestIds = manifests
            .Select(x => x.StackId)
            .ToHashSet();
        var stackRows = await _db.RuntimeStacks
            .AsNoTracking()
            .OrderBy(x => x.Slug)
            .ToArrayAsync(ct);

        var activeRows = stackRows
            .Where(x => !RuntimeReconciliationClassifier.IsDestroyedRuntimeStackStatus(
                x.Status,
                x.LastVerifiedStatus,
                x.Slug))
            .OrderBy(x => x.Slug, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var destroyedRows = stackRows
            .Where(x => RuntimeReconciliationClassifier.IsDestroyedRuntimeStackStatus(
                x.Status,
                x.LastVerifiedStatus,
                x.Slug))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Select(x => new RuntimeReconciliationDestroyedStack(
                StackId: x.Id,
                Slug: x.Slug,
                Status: x.Status,
                LastVerifiedStatus: x.LastVerifiedStatus,
                LastVerifiedAtUtc: x.LastVerifiedAtUtc,
                UpdatedAtUtc: x.UpdatedAtUtc))
            .ToArray();

        var activeStackIds = activeRows
            .Select(x => x.Id)
            .ToHashSet();

        var activeServiceRows = await _db.RuntimeServiceInstances
            .AsNoTracking()
            .Where(x => activeStackIds.Contains(x.RuntimeStackId))
            .Select(x => new
            {
                x.RuntimeStackId,
                x.ServiceKey
            })
            .ToArrayAsync(ct);

        var activeRouteRows = await _db.RuntimeRoutes
            .AsNoTracking()
            .Where(x => activeStackIds.Contains(x.RuntimeStackId))
            .Join(
                _db.RuntimeStacks.AsNoTracking(),
                route => route.RuntimeStackId,
                stack => stack.Id,
                (route, stack) => new
                {
                    route.Id,
                    route.RuntimeStackId,
                    StackSlug = stack.Slug,
                    route.ServiceKey,
                    route.Provider,
                    route.PublicHost,
                    route.ForwardHost,
                    route.ForwardPort,
                    route.NpmCertificateId,
                    route.ProviderRouteId,
                    route.Status
                })
            .OrderBy(x => x.StackSlug)
            .ThenBy(x => x.ServiceKey)
            .ThenBy(x => x.PublicHost)
            .ToArrayAsync(ct);

        var activeRoutes = activeRouteRows
            .Select(x => new RuntimeReconciliationRoute(
                RouteId: x.Id,
                RuntimeStackId: x.RuntimeStackId,
                StackSlug: x.StackSlug,
                ServiceKey: x.ServiceKey,
                Provider: x.Provider,
                PublicHost: x.PublicHost,
                ForwardHost: x.ForwardHost,
                ForwardPort: x.ForwardPort,
                NpmCertificateId: x.NpmCertificateId,
                ProviderRouteId: x.ProviderRouteId,
                Status: x.Status))
            .ToArray();

        var activeRouteHosts = activeRoutes
            .Select(x => x.PublicHost)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<NpmProxyHost> npmProxyHosts = [];
        var npmInspectionAvailable = true;

        try
        {
            npmProxyHosts = await _npmProxyHostService.ListAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            npmInspectionAvailable = false;
            _log.LogWarning(
                ex,
                "NPM proxy hosts could not be listed during runtime reconciliation.");
            warnings.Add($"NPM proxy hosts could not be listed: {ex.Message}");
        }

        var classifiedNpmHosts = RuntimeReconciliationClassifier.ClassifyNpmProxyHosts(
            npmProxyHosts,
            activeRouteHosts);
        var orphanedNpmHosts = RuntimeReconciliationClassifier.FindOrphanedMemStackProxyHosts(
            classifiedNpmHosts);

        var activeStacks = activeRows
            .Select(stack =>
            {
                var hasManifest = manifestIds.Contains(stack.Id);
                var stackRoutes = activeRoutes
                    .Where(route => route.RuntimeStackId == stack.Id)
                    .ToArray();
                var stackServices = activeServiceRows
                    .Where(service => service.RuntimeStackId == stack.Id)
                    .ToArray();
                var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
                    hasManifest: hasManifest,
                    matrixServiceCount: stackServices.Count(service =>
                        string.Equals(
                            service.ServiceKey,
                            ServiceKeys.Matrix,
                            StringComparison.OrdinalIgnoreCase)),
                    elementServiceCount: stackServices.Count(service =>
                        string.Equals(
                            service.ServiceKey,
                            ServiceKeys.ElementWeb,
                            StringComparison.OrdinalIgnoreCase)),
                    routes: stackRoutes,
                    npmHosts: classifiedNpmHosts,
                    npmInspectionAvailable: npmInspectionAvailable);

                return new RuntimeReconciliationActiveStack(
                    StackId: stack.Id,
                    Slug: stack.Slug,
                    Status: stack.Status,
                    LastVerifiedStatus: stack.LastVerifiedStatus,
                    LastVerifiedAtUtc: stack.LastVerifiedAtUtc,
                    MatrixPublicBaseUrl: stack.MatrixPublicBaseUrl,
                    ElementPublicBaseUrl: stack.ElementPublicBaseUrl,
                    HasManifest: hasManifest,
                    RecordedServiceCount: decision.RecordedServiceCount,
                    RecordedRouteCount: decision.RecordedRouteCount,
                    MatchingNpmRouteCount: decision.MatchingNpmRouteCount,
                    MissingNpmRouteCount: decision.MissingNpmRouteCount,
                    MismatchedNpmRouteCount: decision.MismatchedNpmRouteCount,
                    ReconciliationState: decision.State,
                    CanRemoveFromReconciliation: decision.CanRemove,
                    RemovalActionCode: decision.ActionCode,
                    Reason: decision.Reason);
            })
            .ToArray();

        var activeDbRowsWithoutManifestCount = activeStacks.Count(x => !x.HasManifest);
        var removableManifestlessStackCount = activeStacks.Count(x =>
            !x.HasManifest && x.CanRemoveFromReconciliation);
        var activeDbIds = activeRows
            .Select(x => x.Id)
            .ToHashSet();
        var manifestWithoutActiveDbRowCount = manifests.Count(x =>
            !activeDbIds.Contains(x.StackId));

        var status = warnings.Count > 0
            ? "degraded"
            : orphanedNpmHosts.Count > 0 ||
              activeDbRowsWithoutManifestCount > 0 ||
              manifestWithoutActiveDbRowCount > 0
                ? "needs_attention"
                : "ok";

        return new RuntimeReconciliationReport(
            Source: "control-plane",
            Status: status,
            CheckedAtUtc: checkedAtUtc,
            Summary: new RuntimeReconciliationSummary(
                ActiveStackCount: activeStacks.Length,
                DestroyedStackHistoryCount: destroyedRows.Length,
                ActiveRouteCount: activeRoutes.Length,
                NpmProxyHostCount: classifiedNpmHosts.Count,
                MemManagedNpmProxyHostCount: classifiedNpmHosts.Count(x =>
                    x.LooksLikeMemStackRoute),
                OrphanedNpmProxyHostCount: orphanedNpmHosts.Count,
                ManifestCount: manifests.Count,
                ActiveDatabaseRowsWithoutManifestCount: activeDbRowsWithoutManifestCount,
                ManifestWithoutActiveDatabaseRowCount: manifestWithoutActiveDbRowCount,
                RemovableManifestlessStackCount: removableManifestlessStackCount),
            ActiveStacks: activeStacks,
            DestroyedStacks: destroyedRows,
            ActiveRoutes: activeRoutes,
            NpmProxyHosts: classifiedNpmHosts,
            OrphanedNpmProxyHosts: orphanedNpmHosts,
            Warnings: warnings,
            Detail: status switch
            {
                "ok" => "Runtime database, manifests, active routes, and NPM proxy hosts are aligned.",
                "needs_attention" => "Runtime reconciliation found evidence that needs operator review. Bounded removal is available only where MEM can reconstruct and revalidate ownership.",
                _ => "Runtime reconciliation completed with warnings. Some external state could not be inspected."
            });
    }
}
