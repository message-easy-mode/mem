using HostAgent.Runtime.ServiceRuntime;

namespace HostAgent.Runtime.Ingress;


public static class RuntimeMetadataReader
{
    public static string? GetInternalRouteId(IReadOnlyDictionary<string, string?> meta)
    {
        if (meta.TryGetValue(RuntimeMetadataKeys.LegacyNpm.InternalProxyHostId, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;

        if (meta.TryGetValue(IngressRuntimeMetadataKeys.InternalRouteId, out var v2) && !string.IsNullOrWhiteSpace(v2))
            return v2;

        return null;
    }

    public static string? GetPublicRouteId(IReadOnlyDictionary<string, string?> meta)
    {
        if (meta.TryGetValue(RuntimeMetadataKeys.LegacyNpm.PublicProxyHostId, out var v) && !string.IsNullOrWhiteSpace(v))
            return v;

        if (meta.TryGetValue(IngressRuntimeMetadataKeys.PublicRouteId, out var v2) && !string.IsNullOrWhiteSpace(v2))
            return v2;

        return null;
    }
}
