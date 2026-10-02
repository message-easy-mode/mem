using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Runtime.Ingress;

public sealed class NpmRoutePublisher : IRoutePublisher
{
    private readonly NpmProxyHostService _proxyHostService;
    private readonly IRoutePolicyResolver _policyResolver;

    public NpmRoutePublisher(
        NpmProxyHostService proxyHostService,
        IRoutePolicyResolver policyResolver)
    {
        _proxyHostService = proxyHostService;
        _policyResolver = policyResolver;
    }

    public async Task<RoutePublishResult> EnsureAsync(
        RoutePublishRequest req,
        CancellationToken ct)
    {
        var effective = _policyResolver.ApplyDefaults(req);

        if (string.IsNullOrWhiteSpace(effective.Domain))
            throw new InvalidOperationException("Route domain is required.");

        if (string.IsNullOrWhiteSpace(effective.ForwardHost))
            throw new InvalidOperationException("Route forward host is required.");

        if (effective.ForwardPort is <= 0 or > 65535)
            throw new InvalidOperationException("Route forward port is invalid.");

        var certificateId = effective.CertificateId.GetValueOrDefault();
        var advancedConfig = NormalizeAdvancedConfig(effective.AdvancedConfig);

        var result = await _proxyHostService.EnsureProxyHostAsync(
            new CreateNpmProxyHostRequest(
                Domain: effective.Domain,
                ForwardHost: effective.ForwardHost,
                ForwardPort: effective.ForwardPort,
                ForceSsl: effective.ForceSsl,
                CertificateId: certificateId,
                Http2: effective.Http2,
                AdvancedConfig: advancedConfig),
            ct);

        var sslExpected =
            effective.RequireSsl ||
            effective.ForceSsl ||
            certificateId > 0;

        var advancedConfigApplied =
            string.IsNullOrWhiteSpace(advancedConfig) ||
            string.Equals(
                NormalizeAdvancedConfig(result.AdvancedConfig),
                advancedConfig,
                StringComparison.Ordinal);

        return new RoutePublishResult(
            RouteId: result.Id.ToString(),
            Domain: effective.Domain,
            ForwardHost: result.ForwardHost,
            ForwardPort: result.ForwardPort,
            Kind: effective.Kind,
            SslExpected: sslExpected,
            SslConfigured: result.SslForced && result.CertificateId > 0,
            CertificateId: result.CertificateId > 0 ? result.CertificateId : null,
            AdvancedConfigApplied: advancedConfigApplied,
            Ready: result.Enabled,
            Warning: BuildWarning(
                effective,
                result,
                advancedConfig,
                advancedConfigApplied));
    }

    public async Task DeleteByIdIfExistsAsync(
        string? routeId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(routeId))
        {
            return;
        }

        if (!int.TryParse(routeId, out var parsedId) || parsedId <= 0)
        {
            return;
        }

        await _proxyHostService.DeleteByIdIfExistsAsync(parsedId, ct);
    }

    public async Task DeleteByDomainIfExistsAsync(
        string? domain,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return;
        }

        await _proxyHostService.DeleteByDomainIfExistsAsync(
            domain.Trim(),
            ct);
    }

    private static string? BuildWarning(
        RoutePublishRequest request,
        NpmProxyHostResult result,
        string advancedConfig,
        bool advancedConfigApplied)
    {
        if (request.RequireSsl && result.CertificateId <= 0)
        {
            return "Route requested SSL, but no NPM certificate id was provided.";
        }

        if (!string.IsNullOrWhiteSpace(advancedConfig) && !advancedConfigApplied)
        {
            return "Advanced route config was requested, but NPM returned a different or empty advanced_config value after create/update.";
        }

        return null;
    }

    private static string NormalizeAdvancedConfig(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
    }
}
