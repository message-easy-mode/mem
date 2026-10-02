using HostAgent.Runtime.Maintenance;
using HostAgent.Runtime.ServiceRuntime;
using Modules.Integrations.Npm.Contracts;

namespace HostAgent.Tests.Runtime.Maintenance;

public sealed class RuntimeReconciliationClassifierTests
{
    [Fact]
    public void ClassifyNpmProxyHosts_marks_mem_stack_hosts_without_active_routes_as_orphaned()
    {
        var activeHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "matrix-demo-stack.deltabox.dev",
            "chat-demo-stack.deltabox.dev"
        };

        var hosts = new[]
        {
            ProxyHost(53, "matrix-demo-stack.deltabox.dev", "mem-matrix-demo-stack", 8008),
            ProxyHost(54, "chat-demo-stack.deltabox.dev", "mem-element-demo-stack", 80),
            ProxyHost(91, "chat-old-stack.deltabox.dev", "mem-element-old-stack", 80),
            ProxyHost(92, "admin.federationops.net", "admin", 80)
        };

        var classified = RuntimeReconciliationClassifier.ClassifyNpmProxyHosts(
            hosts,
            activeHosts);
        var orphaned = RuntimeReconciliationClassifier.FindOrphanedMemStackProxyHosts(
            classified);

        Assert.Single(orphaned);
        Assert.Equal(91, orphaned[0].ProxyHostId);
        Assert.Contains("chat-old-stack.deltabox.dev", orphaned[0].DomainNames);
        Assert.True(orphaned[0].LooksLikeMemStackRoute);
        Assert.False(orphaned[0].MatchesActiveRoute);

        Assert.Contains(classified, x => x.ProxyHostId == 53 && x.MatchesActiveRoute);
        Assert.Contains(classified, x => x.ProxyHostId == 54 && x.MatchesActiveRoute);
        Assert.Contains(classified, x => x.ProxyHostId == 92 && !x.LooksLikeMemStackRoute);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_classifies_missing_NPM_route_as_interrupted_removal()
    {
        var routes = Routes();
        var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
            hasManifest: false,
            matrixServiceCount: 1,
            elementServiceCount: 1,
            routes: routes,
            npmHosts:
            [
                ClassifiedHost(
                    54,
                    "chat-demo-stack.deltabox.dev",
                    "mem-element-demo-stack",
                    80)
            ],
            npmInspectionAvailable: true);

        Assert.Equal("interrupted_destroy_candidate", decision.State);
        Assert.True(decision.CanRemove);
        Assert.Equal("finish_interrupted_removal", decision.ActionCode);
        Assert.Equal(1, decision.MatchingNpmRouteCount);
        Assert.Equal(1, decision.MissingNpmRouteCount);
        Assert.Equal(0, decision.MismatchedNpmRouteCount);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_classifies_consistent_manifestless_runtime_as_deliberately_removable()
    {
        var routes = Routes();
        var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
            hasManifest: false,
            matrixServiceCount: 1,
            elementServiceCount: 1,
            routes: routes,
            npmHosts:
            [
                ClassifiedHost(
                    53,
                    "matrix-demo-stack.deltabox.dev",
                    "mem-matrix-demo-stack",
                    8008),
                ClassifiedHost(
                    54,
                    "chat-demo-stack.deltabox.dev",
                    "mem-element-demo-stack",
                    80)
            ],
            npmInspectionAvailable: true);

        Assert.Equal("unlisted_runtime", decision.State);
        Assert.True(decision.CanRemove);
        Assert.Equal("remove_unlisted_runtime", decision.ActionCode);
        Assert.Equal(2, decision.MatchingNpmRouteCount);
        Assert.Equal(0, decision.MissingNpmRouteCount);
        Assert.Equal(0, decision.MismatchedNpmRouteCount);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_withholds_removal_when_service_and_route_sets_are_incomplete()
    {
        var routes = Routes();
        var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
            hasManifest: false,
            matrixServiceCount: 1,
            elementServiceCount: 1,
            routes: [routes[0]],
            npmHosts:
            [
                ClassifiedHost(
                    53,
                    "matrix-demo-stack.deltabox.dev",
                    "mem-matrix-demo-stack",
                    8008)
            ],
            npmInspectionAvailable: true);

        Assert.Equal("ownership_ambiguous", decision.State);
        Assert.False(decision.CanRemove);
        Assert.Null(decision.ActionCode);
        Assert.Contains("one exact Matrix/Element ownership set", decision.Reason);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_classifies_route_target_drift_as_ambiguous_and_non_destructive()
    {
        var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
            hasManifest: false,
            matrixServiceCount: 1,
            elementServiceCount: 1,
            routes: Routes(),
            npmHosts:
            [
                ClassifiedHost(
                    53,
                    "matrix-demo-stack.deltabox.dev",
                    "wrong-matrix-container",
                    8008),
                ClassifiedHost(
                    54,
                    "chat-demo-stack.deltabox.dev",
                    "mem-element-demo-stack",
                    80)
            ],
            npmInspectionAvailable: true);

        Assert.Equal("ownership_ambiguous", decision.State);
        Assert.False(decision.CanRemove);
        Assert.Null(decision.ActionCode);
        Assert.Equal(1, decision.MismatchedNpmRouteCount);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_withholds_removal_when_NPM_inspection_is_unavailable()
    {
        var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
            hasManifest: false,
            matrixServiceCount: 1,
            elementServiceCount: 1,
            routes: Routes(),
            npmHosts: [],
            npmInspectionAvailable: false);

        Assert.Equal("inspection_unavailable", decision.State);
        Assert.False(decision.CanRemove);
        Assert.Null(decision.ActionCode);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_keeps_manifest_backed_stack_on_the_normal_inventory_path()
    {
        var decision = RuntimeReconciliationClassifier.ClassifyActiveStackRecovery(
            hasManifest: true,
            matrixServiceCount: 1,
            elementServiceCount: 1,
            routes: Routes(),
            npmHosts: [],
            npmInspectionAvailable: true);

        Assert.Equal("manifest_present", decision.State);
        Assert.False(decision.CanRemove);
        Assert.Null(decision.ActionCode);
    }

    [Fact]
    public void IsDestroyedRuntimeStackStatus_treats_destroyed_status_and_released_slug_as_history()
    {
        Assert.True(RuntimeReconciliationClassifier.IsDestroyedRuntimeStackStatus(
            "destroyed",
            "destroyed",
            "demo-stack--destroyed-20260709012705"));

        Assert.True(RuntimeReconciliationClassifier.IsDestroyedRuntimeStackStatus(
            "public_routes_verified",
            "destroyed",
            "demo-stack"));

        Assert.True(RuntimeReconciliationClassifier.IsDestroyedRuntimeStackStatus(
            "public_routes_verified",
            "public_routes_verified",
            "demo-stack--destroyed-20260709012705"));

        Assert.False(RuntimeReconciliationClassifier.IsDestroyedRuntimeStackStatus(
            "public_routes_verified",
            "public_routes_verified",
            "demo-stack-restored"));
    }

    [Fact]
    public void BuildOrphanedNpmProxyHostCleanupPlan_only_marks_selected_orphans_as_pending_delete()
    {
        var activeHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "matrix-demo-stack.deltabox.dev",
            "chat-demo-stack.deltabox.dev"
        };

        var classified = RuntimeReconciliationClassifier.ClassifyNpmProxyHosts(
            [
                ProxyHost(53, "matrix-demo-stack.deltabox.dev", "mem-matrix-demo-stack", 8008),
                ProxyHost(91, "chat-old-stack.deltabox.dev", "mem-element-old-stack", 80),
                ProxyHost(92, "admin.federationops.net", "admin", 80)
            ],
            activeHosts);

        var plan = RuntimeReconciliationClassifier.BuildOrphanedNpmProxyHostCleanupPlan(
            classified,
            [53, 91, 92, 999]);

        Assert.Contains(plan, x => x.ProxyHostId == 91 && x.Status == "pending_delete");
        Assert.Contains(plan, x => x.ProxyHostId == 53 && x.Status == "skipped_not_orphaned");
        Assert.Contains(plan, x => x.ProxyHostId == 92 && x.Status == "skipped_not_orphaned");
        Assert.Contains(plan, x => x.ProxyHostId == 999 && x.Status == "already_missing");
    }

    private static RuntimeReconciliationRoute[] Routes() =>
    [
        new RuntimeReconciliationRoute(
            RouteId: Guid.NewGuid(),
            RuntimeStackId: Guid.NewGuid(),
            StackSlug: "demo-stack",
            ServiceKey: ServiceKeys.Matrix,
            Provider: "npm",
            PublicHost: "matrix-demo-stack.deltabox.dev",
            ForwardHost: "mem-matrix-demo-stack",
            ForwardPort: 8008,
            NpmCertificateId: 2,
            ProviderRouteId: "53",
            Status: "created"),
        new RuntimeReconciliationRoute(
            RouteId: Guid.NewGuid(),
            RuntimeStackId: Guid.NewGuid(),
            StackSlug: "demo-stack",
            ServiceKey: ServiceKeys.ElementWeb,
            Provider: "npm",
            PublicHost: "chat-demo-stack.deltabox.dev",
            ForwardHost: "mem-element-demo-stack",
            ForwardPort: 80,
            NpmCertificateId: 2,
            ProviderRouteId: "54",
            Status: "created")
    ];

    private static RuntimeReconciliationNpmProxyHost ClassifiedHost(
        int id,
        string domain,
        string forwardHost,
        int forwardPort) =>
        new(
            ProxyHostId: id,
            DomainNames: [domain],
            ForwardHost: forwardHost,
            ForwardPort: forwardPort,
            CertificateId: 2,
            Enabled: true,
            NginxOnline: true,
            LooksLikeMemStackRoute: true,
            MatchesActiveRoute: true,
            MatchedActiveRouteHosts: [domain],
            Reason: "matches");

    private static NpmProxyHost ProxyHost(
        int id,
        string domain,
        string forwardHost,
        int forwardPort) =>
        new(
            id: id,
            domain_names: [domain],
            forward_host: forwardHost,
            forward_port: forwardPort,
            access_list_id: 0,
            certificate_id: 2,
            forward_scheme: "http",
            advanced_config: string.Empty,
            meta: new NpmProxyHostMeta(nginx_online: true, nginx_err: null),
            locations: [],
            ssl_forced: true,
            http2_support: true,
            allow_websocket_upgrade: true,
            block_exploits: true,
            caching_enabled: false,
            enabled: true,
            hsts_enabled: false,
            hsts_subdomains: false,
            trust_forwarded_proto: false);
}
