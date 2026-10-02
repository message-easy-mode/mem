using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Integrations.Npm.Contracts;

namespace Modules.Integrations.Npm.Services;

public sealed class NpmProxyHostService(
    NpmApiClient npmApiClient,
    INpmTokenProvider tokenProvider,
    NpmApiBaseUrlResolver baseUrlResolver,
    IOptions<NpmApiOptions> options,
    ILogger<NpmProxyHostService> log)
{
    public async Task<NpmProxyHostResult> EnsureProxyHostAsync(
        CreateNpmProxyHostRequest request,
        CancellationToken ct)
    {
        var domain = (request.Domain ?? "").Trim();
        var forwardHost = (request.ForwardHost ?? "").Trim();
        var advancedConfig = NormalizeAdvancedConfig(request.AdvancedConfig);

        if (domain.Length == 0)
            throw new ArgumentException("Domain is required.", nameof(request.Domain));

        if (forwardHost.Length == 0)
            throw new ArgumentException("ForwardHost is required.", nameof(request.ForwardHost));

        if (request.ForwardPort is <= 0 or > 65535)
            throw new ArgumentException("ForwardPort is invalid.", nameof(request.ForwardPort));

        var baseUrl = await GetNpmApiBaseUrlAsync(ct);
        var opts = options.Value;

        var existing = await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.FindProxyHostByDomainAsync(baseUrl, token, domain, ct),
            baseUrl,
            ct);

        var certificateId = request.CertificateId > 0
            ? request.CertificateId
            : opts.CertificateId;

        var forceSsl = request.ForceSsl || opts.ForceSsl;
        var sslForced = forceSsl && certificateId > 0;
        var http2 = request.Http2 || opts.Http2;

        var existingAdvancedConfig = NormalizeAdvancedConfig(existing?.advanced_config);

        var alreadyDesired =
            existing is not null &&
            (existing.domain_names?.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)) == true) &&
            string.Equals(existing.forward_host, forwardHost, StringComparison.OrdinalIgnoreCase) &&
            (existing.forward_port ?? 0) == request.ForwardPort &&
            (existing.certificate_id ?? 0) == certificateId &&
            (existing.ssl_forced ?? false) == sslForced &&
            (existing.http2_support ?? false) == http2 &&
            string.Equals(existingAdvancedConfig, advancedConfig, StringComparison.Ordinal) &&
            (existing.enabled ?? true);

        if (alreadyDesired)
        {
            log.LogInformation(
                "NPM proxy host already correct. Domain={Domain} ProxyHostId={ProxyHostId}",
                domain,
                existing!.id);

            return ToResult(
                existing,
                domain,
                forwardHost,
                request.ForwardPort,
                sslForced,
                certificateId,
                advancedConfig);
        }

        if (existing is null)
        {
            log.LogInformation(
                "Creating NPM proxy host. Domain={Domain} Forward={ForwardHost}:{ForwardPort} AdvancedConfig={HasAdvancedConfig}",
                domain,
                forwardHost,
                request.ForwardPort,
                !string.IsNullOrWhiteSpace(advancedConfig));

            var created = await ExecuteWithTokenRetryAsync(
                async token => await npmApiClient.CreateProxyHostAsync(
                    baseUrl,
                    token,
                    new NpmProxyHostCreate(
                        domain_names: [domain],
                        forward_host: forwardHost,
                        forward_port: request.ForwardPort,
                        forward_scheme: "http",
                        access_list_id: 0,
                        certificate_id: certificateId,
                        ssl_forced: sslForced,
                        http2_support: http2,
                        allow_websocket_upgrade: true,
                        block_exploits: true,
                        caching_enabled: false,
                        enabled: true,
                        advanced_config: advancedConfig,
                        locations: Array.Empty<object>(),
                        hsts_enabled: false,
                        hsts_subdomains: false,
                        trust_forwarded_proto: false),
                    ct),
                baseUrl,
                ct);

            return ToResult(
                created,
                domain,
                forwardHost,
                request.ForwardPort,
                sslForced,
                certificateId,
                advancedConfig);
        }

        log.LogInformation(
            "Updating NPM proxy host. Domain={Domain} ProxyHostId={ProxyHostId} Forward={ForwardHost}:{ForwardPort} AdvancedConfig={HasAdvancedConfig}",
            domain,
            existing.id,
            forwardHost,
            request.ForwardPort,
            !string.IsNullOrWhiteSpace(advancedConfig));

        var updated = await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.UpdateProxyHostAsync(
                baseUrl,
                token,
                existing.id,
                new NpmProxyHostUpdateRequest(
                    domain_names: [domain],
                    forward_host: forwardHost,
                    forward_port: request.ForwardPort,
                    forward_scheme: "http",
                    access_list_id: existing.access_list_id ?? 0,
                    certificate_id: certificateId,
                    ssl_forced: sslForced,
                    caching_enabled: false,
                    block_exploits: true,
                    allow_websocket_upgrade: true,
                    http2_support: http2,
                    enabled: true,
                    advanced_config: advancedConfig,
                    locations: Array.Empty<object>(),
                    hsts_enabled: existing.hsts_enabled ?? false,
                    hsts_subdomains: existing.hsts_subdomains ?? false),
                ct),
            baseUrl,
            ct);

        return ToResult(
            updated,
            domain,
            forwardHost,
            request.ForwardPort,
            sslForced,
            certificateId,
            advancedConfig);
    }

    public async Task<IReadOnlyList<NpmProxyHost>> ReapplyCertificateConsumersAsync(
        int certificateId,
        CancellationToken ct)
    {
        if (certificateId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(certificateId));
        }

        var baseUrl = await GetNpmApiBaseUrlAsync(ct);

        var hosts = await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.GetProxyHostsAsync(baseUrl, token, ct),
            baseUrl,
            ct);

        var plan = BuildCertificateConsumerRefreshPlan(hosts, certificateId);
        if (plan.Count == 0)
        {
            log.LogInformation(
                "No NPM proxy hosts currently reference certificate {CertificateId}; no runtime certificate activation was required.",
                certificateId);
            return [];
        }

        var refreshed = new List<NpmProxyHost>(plan.Count);

        foreach (var item in plan)
        {
            var updated = await ExecuteWithTokenRetryAsync(
                async token => await npmApiClient.UpdateProxyHostAsync(
                    baseUrl,
                    token,
                    item.ProxyHostId,
                    BuildExactUpdateRequest(item.Snapshot),
                    ct),
                baseUrl,
                ct);

            if (!SnapshotMatches(item.Snapshot, updated, requireSameId: true))
            {
                throw new InvalidOperationException(
                    $"NPM proxy host {item.ProxyHostId} changed unexpectedly while activating replacement certificate {certificateId}.");
            }

            if (updated.meta?.nginx_online == false)
            {
                throw new InvalidOperationException(
                    $"NPM proxy host {item.ProxyHostId} was re-applied for replacement certificate {certificateId}, but Nginx reported it offline: {updated.meta.nginx_err ?? "no detail"}");
            }

            refreshed.Add(updated);
        }

        log.LogInformation(
            "Re-applied {ProxyHostCount} NPM proxy host(s) that reference replacement certificate {CertificateId} so the running Nginx runtime reloads the updated certificate material.",
            refreshed.Count,
            certificateId);

        return refreshed;
    }

    internal static IReadOnlyList<NpmCertificateConsumerRefreshPlanItem> BuildCertificateConsumerRefreshPlan(
        IEnumerable<NpmProxyHost> hosts,
        int certificateId)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        if (certificateId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(certificateId));
        }

        return hosts
            .Where(host => (host.certificate_id ?? 0) == certificateId)
            .OrderBy(host => host.id)
            .Select(host => new NpmCertificateConsumerRefreshPlanItem(
                ProxyHostId: host.id,
                Snapshot: CaptureSnapshot(host)))
            .ToArray();
    }

    internal static NpmProxyHostUpdateRequest BuildExactUpdateRequest(NpmProxyHostSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        return new NpmProxyHostUpdateRequest(
            domain_names: snapshot.DomainNames,
            forward_host: snapshot.ForwardHost,
            forward_port: snapshot.ForwardPort,
            forward_scheme: snapshot.ForwardScheme,
            access_list_id: snapshot.AccessListId,
            certificate_id: snapshot.CertificateId,
            ssl_forced: snapshot.SslForced,
            caching_enabled: snapshot.CachingEnabled,
            block_exploits: snapshot.BlockExploits,
            allow_websocket_upgrade: snapshot.AllowWebsocketUpgrade,
            http2_support: snapshot.Http2Support,
            enabled: snapshot.Enabled,
            advanced_config: snapshot.AdvancedConfig,
            locations: snapshot.Locations,
            hsts_enabled: snapshot.HstsEnabled,
            hsts_subdomains: snapshot.HstsSubdomains,
            trust_forwarded_proto: snapshot.TrustForwardedProto);
    }

    internal sealed record NpmCertificateConsumerRefreshPlanItem(
        int ProxyHostId,
        NpmProxyHostSnapshot Snapshot);

    public static NpmProxyHostSnapshot CaptureSnapshot(NpmProxyHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return new NpmProxyHostSnapshot(
            Id: host.id,
            DomainNames: host.domain_names ?? [],
            ForwardHost: host.forward_host?.Trim() ?? string.Empty,
            ForwardPort: host.forward_port ?? 0,
            AccessListId: host.access_list_id ?? 0,
            CertificateId: host.certificate_id ?? 0,
            ForwardScheme: NormalizeForwardScheme(host.forward_scheme),
            AdvancedConfig: NormalizeAdvancedConfig(host.advanced_config),
            Locations: host.locations ?? [],
            SslForced: host.ssl_forced ?? false,
            Http2Support: host.http2_support ?? false,
            AllowWebsocketUpgrade: host.allow_websocket_upgrade ?? true,
            BlockExploits: host.block_exploits ?? true,
            CachingEnabled: host.caching_enabled ?? false,
            Enabled: host.enabled ?? true,
            HstsEnabled: host.hsts_enabled ?? false,
            HstsSubdomains: host.hsts_subdomains ?? false,
            TrustForwardedProto: host.trust_forwarded_proto ?? false);
    }

    public static bool SnapshotMatches(
        NpmProxyHostSnapshot expected,
        NpmProxyHost? current,
        bool requireSameId = true)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (current is null)
        {
            return false;
        }

        return (!requireSameId || current.id == expected.Id) &&
            DomainSetsEqual(expected.DomainNames, current.domain_names ?? []) &&
            string.Equals(current.forward_host?.Trim(), expected.ForwardHost.Trim(), StringComparison.OrdinalIgnoreCase) &&
            current.forward_port == expected.ForwardPort &&
            (current.access_list_id ?? 0) == expected.AccessListId &&
            (current.certificate_id ?? 0) == expected.CertificateId &&
            string.Equals(NormalizeForwardScheme(current.forward_scheme), NormalizeForwardScheme(expected.ForwardScheme), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeAdvancedConfig(current.advanced_config), NormalizeAdvancedConfig(expected.AdvancedConfig), StringComparison.Ordinal) &&
            LocationsEqual(expected.Locations, current.locations ?? []) &&
            (current.ssl_forced ?? false) == expected.SslForced &&
            (current.http2_support ?? false) == expected.Http2Support &&
            (current.allow_websocket_upgrade ?? true) == expected.AllowWebsocketUpgrade &&
            (current.block_exploits ?? true) == expected.BlockExploits &&
            (current.caching_enabled ?? false) == expected.CachingEnabled &&
            (current.enabled ?? true) == expected.Enabled &&
            (current.hsts_enabled ?? false) == expected.HstsEnabled &&
            (current.hsts_subdomains ?? false) == expected.HstsSubdomains &&
            (current.trust_forwarded_proto ?? false) == expected.TrustForwardedProto;
    }

    public Task<NpmProxyHost> RestoreSnapshotAsync(
        NpmProxyHostSnapshot snapshot,
        CancellationToken ct) =>
        RestoreSnapshotAsync(snapshot, snapshot.DomainNames.FirstOrDefault() ?? string.Empty, ct);

    public async Task<NpmProxyHost> RestoreSnapshotAsync(
        NpmProxyHostSnapshot snapshot,
        string lookupDomain,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateSnapshot(snapshot);

        var domain = (lookupDomain ?? string.Empty).Trim();
        if (domain.Length == 0 ||
            !snapshot.DomainNames.Any(x => string.Equals(x.Trim(), domain, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The NPM snapshot lookup domain must be one of the captured domain names.",
                nameof(lookupDomain));
        }
        var baseUrl = await GetNpmApiBaseUrlAsync(ct);
        var existing = await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.FindProxyHostByDomainAsync(baseUrl, token, domain, ct),
            baseUrl,
            ct);

        NpmProxyHost restored;
        if (existing is null)
        {
            restored = await ExecuteWithTokenRetryAsync(
                async token => await npmApiClient.CreateProxyHostAsync(
                    baseUrl,
                    token,
                    new NpmProxyHostCreate(
                        domain_names: snapshot.DomainNames,
                        forward_host: snapshot.ForwardHost,
                        forward_port: snapshot.ForwardPort,
                        forward_scheme: snapshot.ForwardScheme,
                        access_list_id: snapshot.AccessListId,
                        certificate_id: snapshot.CertificateId,
                        ssl_forced: snapshot.SslForced,
                        caching_enabled: snapshot.CachingEnabled,
                        block_exploits: snapshot.BlockExploits,
                        advanced_config: snapshot.AdvancedConfig,
                        allow_websocket_upgrade: snapshot.AllowWebsocketUpgrade,
                        http2_support: snapshot.Http2Support,
                        enabled: snapshot.Enabled,
                        locations: snapshot.Locations,
                        hsts_enabled: snapshot.HstsEnabled,
                        hsts_subdomains: snapshot.HstsSubdomains,
                        trust_forwarded_proto: snapshot.TrustForwardedProto),
                    ct),
                baseUrl,
                ct);
        }
        else
        {
            restored = await ExecuteWithTokenRetryAsync(
                async token => await npmApiClient.UpdateProxyHostAsync(
                    baseUrl,
                    token,
                    existing.id,
                    new NpmProxyHostUpdateRequest(
                        domain_names: snapshot.DomainNames,
                        forward_host: snapshot.ForwardHost,
                        forward_port: snapshot.ForwardPort,
                        forward_scheme: snapshot.ForwardScheme,
                        access_list_id: snapshot.AccessListId,
                        certificate_id: snapshot.CertificateId,
                        ssl_forced: snapshot.SslForced,
                        caching_enabled: snapshot.CachingEnabled,
                        block_exploits: snapshot.BlockExploits,
                        allow_websocket_upgrade: snapshot.AllowWebsocketUpgrade,
                        http2_support: snapshot.Http2Support,
                        enabled: snapshot.Enabled,
                        advanced_config: snapshot.AdvancedConfig,
                        locations: snapshot.Locations,
                        hsts_enabled: snapshot.HstsEnabled,
                        hsts_subdomains: snapshot.HstsSubdomains,
                        trust_forwarded_proto: snapshot.TrustForwardedProto),
                    ct),
                baseUrl,
                ct);
        }

        log.LogInformation(
            "Restored exact NPM proxy-host snapshot. Domain={Domain} PreviousProxyHostId={PreviousProxyHostId} CurrentProxyHostId={CurrentProxyHostId}",
            domain,
            snapshot.Id,
            restored.id);

        return restored;
    }

    public async Task<NpmProxyHost?> GetByDomainAsync(string domain, CancellationToken ct)
    {
        domain = (domain ?? "").Trim();

        if (domain.Length == 0)
            throw new ArgumentException("domain required", nameof(domain));

        var baseUrl = await GetNpmApiBaseUrlAsync(ct);

        return await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.FindProxyHostByDomainAsync(baseUrl, token, domain, ct),
            baseUrl,
            ct);
    }


    public async Task<IReadOnlyList<NpmProxyHost>> ListAsync(CancellationToken ct)
    {
        var baseUrl = await GetNpmApiBaseUrlAsync(ct);

        return await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.GetProxyHostsAsync(baseUrl, token, ct),
            baseUrl,
            ct);
    }

    public async Task DeleteByIdIfExistsAsync(int proxyHostId, CancellationToken ct)
    {
        await DeleteByIdIfExistsWithResultAsync(proxyHostId, ct);
    }

    public async Task<bool> DeleteByIdIfExistsWithResultAsync(int proxyHostId, CancellationToken ct)
    {
        if (proxyHostId <= 0)
        {
            return false;
        }

        var baseUrl = await GetNpmApiBaseUrlAsync(ct);

        try
        {
            await ExecuteWithTokenRetryAsync<object?>(
                async token =>
                {
                    await npmApiClient.DeleteProxyHostAsync(baseUrl, token, proxyHostId, ct);
                    return null;
                },
                baseUrl,
                ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogInformation(
                "NPM proxy host was already absent. ProxyHostId={ProxyHostId}",
                proxyHostId);
            return false;
        }

        log.LogInformation(
            "Deleted NPM proxy host. ProxyHostId={ProxyHostId}",
            proxyHostId);

        return true;
    }

    public async Task DeleteByDomainIfExistsAsync(string domain, CancellationToken ct)
    {
        domain = (domain ?? "").Trim();

        if (domain.Length == 0)
            throw new ArgumentException("domain required", nameof(domain));

        var baseUrl = await GetNpmApiBaseUrlAsync(ct);

        var existing = await ExecuteWithTokenRetryAsync(
            async token => await npmApiClient.FindProxyHostByDomainAsync(baseUrl, token, domain, ct),
            baseUrl,
            ct);

        if (existing is null)
            return;

        try
        {
            await ExecuteWithTokenRetryAsync<object?>(
                async token =>
                {
                    await npmApiClient.DeleteProxyHostAsync(baseUrl, token, existing.id, ct);
                    return null;
                },
                baseUrl,
                ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            log.LogInformation(
                "NPM proxy host was already absent. Domain={Domain} ProxyHostId={ProxyHostId}",
                domain,
                existing.id);
            return;
        }

        log.LogInformation(
            "Deleted NPM proxy host. Domain={Domain} ProxyHostId={ProxyHostId}",
            domain,
            existing.id);
    }

    private async Task<string> GetNpmApiBaseUrlAsync(CancellationToken ct)
    {
        var resolution = await baseUrlResolver.ResolveAsync(ct);

        if (!string.IsNullOrWhiteSpace(resolution.Warning))
        {
            log.LogDebug(
                "NPM base URL resolved from {Source}. Warning={Warning}",
                resolution.Source,
                resolution.Warning);
        }

        return resolution.BaseUrl;
    }

    private async Task<T> ExecuteWithTokenRetryAsync<T>(
        Func<string, Task<T>> action,
        string baseUrl,
        CancellationToken ct)
    {
        var token = await tokenProvider.GetTokenAsync(baseUrl, ct);

        try
        {
            return await action(token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            log.LogWarning(ex, "NPM returned unauthorized/forbidden; retrying with refreshed token");

            tokenProvider.Invalidate();

            var token2 = await tokenProvider.GetTokenAsync(baseUrl, ct);
            return await action(token2);
        }
    }

    private static NpmProxyHostResult ToResult(
        NpmProxyHost host,
        string domain,
        string forwardHost,
        int forwardPort,
        bool sslForced,
        int certificateId,
        string advancedConfig)
    {
        var resolvedAdvancedConfig = NormalizeAdvancedConfig(host.advanced_config);

        if (string.IsNullOrWhiteSpace(resolvedAdvancedConfig))
        {
            resolvedAdvancedConfig = advancedConfig;
        }

        return new NpmProxyHostResult(
            Id: host.id,
            DomainNames: host.domain_names ?? [domain],
            ForwardHost: host.forward_host ?? forwardHost,
            ForwardPort: host.forward_port ?? forwardPort,
            SslForced: host.ssl_forced ?? sslForced,
            CertificateId: host.certificate_id ?? certificateId,
            Enabled: host.enabled ?? true,
            AdvancedConfig: resolvedAdvancedConfig
        );
    }

    private static void ValidateSnapshot(NpmProxyHostSnapshot snapshot)
    {
        if (snapshot.DomainNames.Length == 0 || snapshot.DomainNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty NPM domain is required.", nameof(snapshot));
        }
        if (string.IsNullOrWhiteSpace(snapshot.ForwardHost))
        {
            throw new ArgumentException("NPM snapshot forward host is required.", nameof(snapshot));
        }
        if (snapshot.ForwardPort is <= 0 or > 65535)
        {
            throw new ArgumentException("NPM snapshot forward port is invalid.", nameof(snapshot));
        }
        if (string.IsNullOrWhiteSpace(snapshot.ForwardScheme))
        {
            throw new ArgumentException("NPM snapshot forward scheme is required.", nameof(snapshot));
        }
    }

    private static bool DomainSetsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right) =>
        left.Select(x => x.Trim()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(
                right.Select(x => x.Trim()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

    private static bool LocationsEqual(object[] left, object[] right) =>
        string.Equals(
            JsonSerializer.Serialize(left),
            JsonSerializer.Serialize(right),
            StringComparison.Ordinal);

    private static string NormalizeForwardScheme(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "http" : value.Trim().ToLowerInvariant();

    private static string NormalizeAdvancedConfig(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}