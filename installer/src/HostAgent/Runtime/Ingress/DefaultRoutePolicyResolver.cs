using Microsoft.Extensions.Options;

namespace HostAgent.Runtime.Ingress;

public sealed class DefaultRoutePolicyResolver : IRoutePolicyResolver
{
    private readonly IngressCertificateOptions _certs;

    public DefaultRoutePolicyResolver(IOptions<IngressCertificateOptions> certs)
    {
        _certs = certs.Value;
    }

    public RoutePublishRequest ApplyDefaults(RoutePublishRequest request)
    {
        var req = request;

        switch (req.Kind)
        {
            case RouteKind.Matrix:
            {
                req = req with
                {
                    RequireSsl = true,
                    ForceSsl = true,
                    Http2 = true,
                    HstsEnabled = true,
                    HstsSubdomains = true,
                    AdvancedConfig = string.IsNullOrWhiteSpace(req.AdvancedConfig)
                        ? IngressAdvancedConfigTemplates.MatrixWellKnown()
                        : req.AdvancedConfig
                };

                break;
            }

            case RouteKind.ElementWeb:
            {
                req = req with
                {
                    RequireSsl = true,
                    ForceSsl = true,
                    Http2 = true,
                    HstsEnabled = true,
                    HstsSubdomains = true
                };

                break;
            }

            case RouteKind.Generic:
            default:
                break;
        }

        return req;
    }
}
