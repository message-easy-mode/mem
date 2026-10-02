using HostAgent.Runtime.Ingress;

namespace HostAgent.Runtime.ServiceRuntime;

public static class RuntimeMetadataKeys
{
    // ------------------------------------------------------------
    // Docker runtime keys (shared)
    // ------------------------------------------------------------
    public const string DockerContainerId = "docker.containerId"; // optional; ContainerId is already a proper field
    public const string DockerNetwork     = "docker.network";     // optional

    // ------------------------------------------------------------
    // Ingress keys (provider-agnostic) – re-export canonical keys
    // ------------------------------------------------------------
    public const string IngressInternalRouteId = IngressRuntimeMetadataKeys.InternalRouteId;
    public const string IngressInternalDomain  = IngressRuntimeMetadataKeys.InternalDomain;

    public const string IngressPublicRouteId   = IngressRuntimeMetadataKeys.PublicRouteId;
    public const string IngressPublicDomain    = IngressRuntimeMetadataKeys.PublicDomain;

    public const string IngressForwardHost     = IngressRuntimeMetadataKeys.ForwardHost;
    public const string IngressForwardPort     = IngressRuntimeMetadataKeys.ForwardPort;

    // ------------------------------------------------------------
    // Legacy NPM keys (keep only during migration)
    // ------------------------------------------------------------
    public static class LegacyNpm
    {
        public const string ProxyHostId  = "npm.proxyHostId";
        public const string Domain       = "npm.domain";
        public const string ForwardHost  = "npm.forwardHost";
        public const string ForwardPort  = "npm.forwardPort";

        public const string InternalProxyHostId = "npm.internal.proxyHostId";
        public const string InternalDomain      = "npm.internal.domain";

        public const string PublicProxyHostId   = "npm.public.proxyHostId";
        public const string PublicDomain        = "npm.public.domain";
    }
}
