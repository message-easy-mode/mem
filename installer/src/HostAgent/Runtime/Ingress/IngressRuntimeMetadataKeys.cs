namespace HostAgent.Runtime.Ingress;

public static class IngressRuntimeMetadataKeys
{
    public const string InternalRouteId = "ingress.internal.routeId";
    public const string InternalDomain  = "ingress.internal.domain";

    public const string PublicRouteId   = "ingress.public.routeId";
    public const string PublicDomain    = "ingress.public.domain";

    public const string ForwardHost     = "ingress.forwardHost";
    public const string ForwardPort     = "ingress.forwardPort";

    public const string RouteKind       = "ingress.routeKind";
    public const string SslExpected     = "ingress.sslExpected";
    public const string CertificateId   = "ingress.certificateId";
    public const string AdvancedApplied = "ingress.advancedConfigApplied";
    public const string Ready           = "ingress.ready";
}
