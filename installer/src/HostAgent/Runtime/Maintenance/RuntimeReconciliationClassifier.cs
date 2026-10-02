using HostAgent.Runtime.ServiceRuntime;
using Modules.Integrations.Npm.Contracts;

namespace HostAgent.Runtime.Maintenance;

public static class RuntimeReconciliationClassifier
{
    public static IReadOnlyList<RuntimeReconciliationNpmProxyHost> ClassifyNpmProxyHosts(
        IReadOnlyList<NpmProxyHost> proxyHosts,
        IReadOnlySet<string> activeRouteHosts)
    {
        var normalizedActiveHosts = new HashSet<string>(
            activeRouteHosts
                .Select(NormalizeHost)
                .Where(x => x.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        return proxyHosts
            .Select(host => ClassifyNpmProxyHost(host, normalizedActiveHosts))
            .OrderBy(
                x => x.DomainNames.FirstOrDefault() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProxyHostId)
            .ToArray();
    }

    public static IReadOnlyList<RuntimeReconciliationNpmProxyHost> FindOrphanedMemStackProxyHosts(
        IReadOnlyList<RuntimeReconciliationNpmProxyHost> classifiedHosts)
    {
        return classifiedHosts
            .Where(x => x.LooksLikeMemStackRoute && !x.MatchesActiveRoute)
            .OrderBy(
                x => x.DomainNames.FirstOrDefault() ?? string.Empty,
                StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProxyHostId)
            .ToArray();
    }

    public static RuntimeReconciliationStackRecoveryDecision ClassifyActiveStackRecovery(
        bool hasManifest,
        int matrixServiceCount,
        int elementServiceCount,
        IReadOnlyList<RuntimeReconciliationRoute> routes,
        IReadOnlyList<RuntimeReconciliationNpmProxyHost> npmHosts,
        bool npmInspectionAvailable)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(npmHosts);

        var recordedServiceCount = matrixServiceCount + elementServiceCount;
        var relevantRoutes = routes
            .Where(route =>
                string.Equals(
                    route.ServiceKey,
                    ServiceKeys.Matrix,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    route.ServiceKey,
                    ServiceKeys.ElementWeb,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var routeEvidence = relevantRoutes
            .Select(route => ClassifyRouteEvidence(route, npmHosts))
            .ToArray();
        var matchingNpmRouteCount = routeEvidence.Count(x => x == RouteEvidence.Match);
        var missingNpmRouteCount = routeEvidence.Count(x => x == RouteEvidence.Missing);
        var mismatchedNpmRouteCount = routeEvidence.Count(x => x == RouteEvidence.Mismatch);

        RuntimeReconciliationStackRecoveryDecision Decision(
            string state,
            bool canRemove,
            string? actionCode,
            string reason) =>
            new(
                State: state,
                CanRemove: canRemove,
                ActionCode: actionCode,
                RecordedServiceCount: recordedServiceCount,
                RecordedRouteCount: relevantRoutes.Length,
                MatchingNpmRouteCount: matchingNpmRouteCount,
                MissingNpmRouteCount: missingNpmRouteCount,
                MismatchedNpmRouteCount: mismatchedNpmRouteCount,
                Reason: reason);

        if (hasManifest)
        {
            return Decision(
                state: "manifest_present",
                canRemove: false,
                actionCode: null,
                reason: "The stack has an active runtime manifest and should be managed from Chat servers.");
        }

        if (!npmInspectionAvailable)
        {
            return Decision(
                state: "inspection_unavailable",
                canRemove: false,
                actionCode: null,
                reason: "NPM inspection was unavailable, so MEM cannot safely classify this unlisted stack for removal.");
        }

        if (matrixServiceCount != 1 || elementServiceCount > 1)
        {
            return Decision(
                state: "ownership_ambiguous",
                canRemove: false,
                actionCode: null,
                reason: "The durable service records are incomplete or ambiguous. MEM will not reconstruct destructive ownership automatically.");
        }

        var matrixRouteCount = relevantRoutes.Count(route =>
            string.Equals(
                route.ServiceKey,
                ServiceKeys.Matrix,
                StringComparison.OrdinalIgnoreCase));
        var elementRouteCount = relevantRoutes.Count(route =>
            string.Equals(
                route.ServiceKey,
                ServiceKeys.ElementWeb,
                StringComparison.OrdinalIgnoreCase));
        var unsupportedProvider = relevantRoutes.Any(route =>
            !string.Equals(route.Provider, "npm", StringComparison.OrdinalIgnoreCase));

        if (matrixRouteCount > 1 ||
            elementRouteCount > 1 ||
            unsupportedProvider ||
            (relevantRoutes.Length > 0 &&
             relevantRoutes.Length != recordedServiceCount))
        {
            return Decision(
                state: "ownership_ambiguous",
                canRemove: false,
                actionCode: null,
                reason: "The durable service and route records do not form one exact Matrix/Element ownership set, or they use an unsupported route provider. MEM will not reconstruct destructive ownership automatically.");
        }

        if (mismatchedNpmRouteCount > 0)
        {
            return Decision(
                state: "ownership_ambiguous",
                canRemove: false,
                actionCode: null,
                reason: "An NPM hostname exists but its upstream or certificate identity does not match the durable MEM route record. Automatic removal is withheld.");
        }

        if (relevantRoutes.Length == 0)
        {
            return Decision(
                state: "failed_creation_candidate",
                canRemove: true,
                actionCode: "cleanup_failed_creation",
                reason: "The stack has durable service records but no public routes or runtime manifest. It can be removed as a failed or incomplete creation while retaining database and files.");
        }

        if (missingNpmRouteCount > 0)
        {
            return Decision(
                state: "interrupted_destroy_candidate",
                canRemove: true,
                actionCode: "finish_interrupted_removal",
                reason: "The stack has no runtime manifest and one or more persisted routes are already absent from NPM. This is consistent with an interrupted removal.");
        }

        return Decision(
            state: "unlisted_runtime",
            canRemove: true,
            actionCode: "remove_unlisted_runtime",
            reason: "The stack has no runtime manifest, but its durable service records and NPM routes remain internally consistent. It can be deliberately retired from Runtime Reconciliation while retaining database and files.");
    }

    public static IReadOnlyList<RuntimeReconciliationNpmProxyHostCleanupResult> BuildOrphanedNpmProxyHostCleanupPlan(
        IReadOnlyList<RuntimeReconciliationNpmProxyHost> classifiedHosts,
        IReadOnlyCollection<int> requestedProxyHostIds)
    {
        var requestedIds = requestedProxyHostIds
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        var byId = classifiedHosts.ToDictionary(x => x.ProxyHostId);
        var orphanedIds = FindOrphanedMemStackProxyHosts(classifiedHosts)
            .Select(x => x.ProxyHostId)
            .ToHashSet();

        return requestedIds
            .Select(proxyHostId =>
            {
                if (!byId.TryGetValue(proxyHostId, out var host))
                {
                    return new RuntimeReconciliationNpmProxyHostCleanupResult(
                        ProxyHostId: proxyHostId,
                        DomainNames: [],
                        Status: "already_missing",
                        Reason: "NPM proxy host was not present when cleanup was evaluated.");
                }

                if (!orphanedIds.Contains(proxyHostId))
                {
                    return new RuntimeReconciliationNpmProxyHostCleanupResult(
                        ProxyHostId: proxyHostId,
                        DomainNames: host.DomainNames,
                        Status: "skipped_not_orphaned",
                        Reason: host.MatchesActiveRoute
                            ? "NPM proxy host matches an active MEM RuntimeRoute and must not be deleted by orphan cleanup."
                            : "NPM proxy host is not classified as an orphaned MEM stack proxy host.");
                }

                return new RuntimeReconciliationNpmProxyHostCleanupResult(
                    ProxyHostId: proxyHostId,
                    DomainNames: host.DomainNames,
                    Status: "pending_delete",
                    Reason: "NPM proxy host is classified as orphaned and selected for cleanup.");
            })
            .ToArray();
    }

    public static bool IsDestroyedRuntimeStackStatus(
        string? status,
        string? lastVerifiedStatus,
        string? slug)
    {
        return string.Equals(status, "destroyed", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(lastVerifiedStatus, "destroyed", StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(slug) &&
                slug.Contains("--destroyed-", StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksLikeMemStackRouteHost(string? host)
    {
        host = NormalizeHost(host);

        if (host.Length == 0)
        {
            return false;
        }

        return host.StartsWith("matrix-", StringComparison.OrdinalIgnoreCase) ||
               host.StartsWith("chat-", StringComparison.OrdinalIgnoreCase);
    }

    private static RouteEvidence ClassifyRouteEvidence(
        RuntimeReconciliationRoute route,
        IReadOnlyList<RuntimeReconciliationNpmProxyHost> npmHosts)
    {
        var expectedHost = NormalizeHost(route.PublicHost);
        if (expectedHost.Length == 0)
        {
            return RouteEvidence.Mismatch;
        }

        var matches = npmHosts
            .Where(host => host.Enabled && host.DomainNames.Any(domain =>
                string.Equals(
                    NormalizeHost(domain),
                    expectedHost,
                    StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (matches.Length == 0)
        {
            return RouteEvidence.Missing;
        }

        if (matches.Length != 1)
        {
            return RouteEvidence.Mismatch;
        }

        var observed = matches[0];
        if (!string.Equals(
                observed.ForwardHost,
                route.ForwardHost,
                StringComparison.OrdinalIgnoreCase) ||
            observed.ForwardPort != route.ForwardPort)
        {
            return RouteEvidence.Mismatch;
        }

        if (route.NpmCertificateId is > 0 &&
            observed.CertificateId != route.NpmCertificateId)
        {
            return RouteEvidence.Mismatch;
        }

        return RouteEvidence.Match;
    }

    private static RuntimeReconciliationNpmProxyHost ClassifyNpmProxyHost(
        NpmProxyHost host,
        IReadOnlySet<string> activeRouteHosts)
    {
        var domainNames = (host.domain_names ?? [])
            .Select(NormalizeHost)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var matchedActiveHosts = domainNames
            .Where(activeRouteHosts.Contains)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var looksLikeMemStackRoute = domainNames.Any(LooksLikeMemStackRouteHost);
        var matchesActiveRoute = matchedActiveHosts.Length > 0;
        var enabled = host.enabled ?? true;
        var nginxOnline = host.meta?.nginx_online ?? false;

        string reason;

        if (looksLikeMemStackRoute && !matchesActiveRoute)
        {
            reason = "NPM proxy host looks like a MEM Matrix/Element stack route, but no active RuntimeRoute uses any of its domain names.";
        }
        else if (matchesActiveRoute)
        {
            reason = "NPM proxy host matches an active MEM RuntimeRoute hostname.";
        }
        else
        {
            reason = "NPM proxy host does not look like a MEM stack route.";
        }

        return new RuntimeReconciliationNpmProxyHost(
            ProxyHostId: host.id,
            DomainNames: domainNames,
            ForwardHost: string.IsNullOrWhiteSpace(host.forward_host)
                ? null
                : host.forward_host.Trim(),
            ForwardPort: host.forward_port,
            CertificateId: host.certificate_id,
            Enabled: enabled,
            NginxOnline: nginxOnline,
            LooksLikeMemStackRoute: looksLikeMemStackRoute,
            MatchesActiveRoute: matchesActiveRoute,
            MatchedActiveRouteHosts: matchedActiveHosts,
            Reason: reason);
    }

    private static string NormalizeHost(string? host)
    {
        return (host ?? string.Empty)
            .Trim()
            .TrimEnd('.')
            .ToLowerInvariant();
    }

    private enum RouteEvidence
    {
        Match,
        Missing,
        Mismatch
    }
}
