namespace HostAgent.Runtime.Ingress;

public sealed record RoutePublishRequest(
    string Domain,
    string ForwardHost,
    int ForwardPort,
    RouteKind Kind = RouteKind.Generic,
    string ForwardScheme = "http",
    bool IsPublic = false,
    bool RequireSsl = false,
    int? CertificateId = null,
    bool ForceSsl = false,
    bool Http2 = true,
    bool HstsEnabled = false,
    bool HstsSubdomains = false,
    bool AllowWebsocketUpgrade = true,
    bool BlockExploits = true,
    bool CachingEnabled = false,
    bool Enabled = true,
    bool TrustForwardedProto = false,
    string AdvancedConfig = ""
);

public enum RouteKind
{
    Generic = 0,
    Matrix = 1,
    ElementWeb = 2
}

public sealed record RoutePublishResult(
    string? RouteId,
    string Domain,
    string ForwardHost,
    int ForwardPort,
    RouteKind Kind,
    bool SslExpected,
    bool SslConfigured,
    int? CertificateId,
    bool AdvancedConfigApplied,
    bool Ready,
    string? Warning = null
);
public interface IRoutePublisher
{
    Task<RoutePublishResult> EnsureAsync(RoutePublishRequest req, CancellationToken ct);
    Task DeleteByIdIfExistsAsync(string? routeId, CancellationToken ct);
    Task DeleteByDomainIfExistsAsync(string? domain, CancellationToken ct);
}
