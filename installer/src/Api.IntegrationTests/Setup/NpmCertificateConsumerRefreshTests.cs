using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace Api.IntegrationTests.Setup;

public sealed class NpmCertificateConsumerRefreshTests
{
    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_02_refresh_plan_targets_only_proxy_hosts_using_replaced_certificate()
    {
        var hosts = new[]
        {
            ProxyHost(3, certificateId: 7, domain: "matrix.example.test", trustForwardedProto: true),
            ProxyHost(5, certificateId: 9, domain: "other.example.test"),
            ProxyHost(8, certificateId: 7, domain: "chat.example.test", enabled: false, hsts: true)
        };

        var plan = NpmProxyHostService.BuildCertificateConsumerRefreshPlan(hosts, certificateId: 7);

        Assert.Equal(new[] { 3, 8 }, plan.Select(item => item.ProxyHostId).ToArray());
        Assert.All(plan, item => Assert.Equal(7, item.Snapshot.CertificateId));
        Assert.Equal("matrix.example.test", plan[0].Snapshot.DomainNames.Single());
        Assert.True(plan[0].Snapshot.TrustForwardedProto);
        Assert.False(plan[1].Snapshot.Enabled);
        Assert.True(plan[1].Snapshot.HstsEnabled);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_02_exact_update_request_preserves_existing_proxy_host_contract()
    {
        var host = new NpmProxyHost(
            id: 12,
            domain_names: ["matrix.example.test", "matrix-alt.example.test"],
            forward_host: "mem-matrix-example",
            forward_port: 8008,
            access_list_id: 4,
            certificate_id: 7,
            forward_scheme: "http",
            advanced_config: "proxy_read_timeout 600;",
            meta: new NpmProxyHostMeta(nginx_online: true, nginx_err: null),
            locations: [new { path = "/_matrix" }],
            ssl_forced: true,
            http2_support: true,
            allow_websocket_upgrade: true,
            block_exploits: false,
            caching_enabled: true,
            enabled: true,
            hsts_enabled: true,
            hsts_subdomains: true,
            trust_forwarded_proto: true);

        var snapshot = NpmProxyHostService.CaptureSnapshot(host);
        var request = NpmProxyHostService.BuildExactUpdateRequest(snapshot);

        Assert.Equal(host.domain_names!, request.domain_names);
        Assert.Equal(host.forward_host!, request.forward_host);
        Assert.Equal(host.forward_port!.Value, request.forward_port);
        Assert.Equal(host.forward_scheme!, request.forward_scheme);
        Assert.Equal(host.access_list_id!.Value, request.access_list_id);
        Assert.Equal(host.certificate_id!.Value, request.certificate_id);
        Assert.Equal(host.ssl_forced!.Value, request.ssl_forced);
        Assert.Equal(host.caching_enabled!.Value, request.caching_enabled);
        Assert.Equal(host.block_exploits!.Value, request.block_exploits);
        Assert.Equal(host.allow_websocket_upgrade!.Value, request.allow_websocket_upgrade);
        Assert.Equal(host.http2_support!.Value, request.http2_support);
        Assert.Equal(host.enabled!.Value, request.enabled);
        Assert.Equal(host.advanced_config!, request.advanced_config);
        Assert.Equal(host.locations!, request.locations);
        Assert.Equal(host.hsts_enabled!.Value, request.hsts_enabled);
        Assert.Equal(host.hsts_subdomains!.Value, request.hsts_subdomains);
        Assert.Equal(host.trust_forwarded_proto!.Value, request.trust_forwarded_proto);
    }

    [Fact]
    public void PLATFORM_TURN_01E_CLEANROOM_CORR_02_refresh_plan_rejects_invalid_certificate_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NpmProxyHostService.BuildCertificateConsumerRefreshPlan([], certificateId: 0));
    }

    private static NpmProxyHost ProxyHost(
        int id,
        int certificateId,
        string domain,
        bool enabled = true,
        bool hsts = false,
        bool trustForwardedProto = false) =>
        new(
            id: id,
            domain_names: [domain],
            forward_host: $"upstream-{id}",
            forward_port: 8000 + id,
            access_list_id: 0,
            certificate_id: certificateId,
            forward_scheme: "http",
            advanced_config: string.Empty,
            meta: new NpmProxyHostMeta(nginx_online: true, nginx_err: null),
            locations: [],
            ssl_forced: true,
            http2_support: true,
            allow_websocket_upgrade: true,
            block_exploits: true,
            caching_enabled: false,
            enabled: enabled,
            hsts_enabled: hsts,
            hsts_subdomains: false,
            trust_forwarded_proto: trustForwardedProto);
}
