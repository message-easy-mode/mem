namespace HostAgent.Runtime;

/// <summary>
/// Builds deterministic internal hostnames for provisioned instances.
/// Goal: stable wildcard cert at *.your-domain.com, and consistent naming across services.
/// </summary>
public static class RuntimeConventions
{
    public static string BuildInternalHost(string servicePrefix, Guid instanceId, string internalRootSuffix)
    {
        servicePrefix = (servicePrefix ?? "").Trim().ToLowerInvariant();
        if (servicePrefix.Length == 0) throw new ArgumentException("servicePrefix required", nameof(servicePrefix));

        internalRootSuffix = (internalRootSuffix ?? "").Trim().ToLowerInvariant();
        if (internalRootSuffix.Length == 0) throw new ArgumentException("internalRootSuffix required", nameof(internalRootSuffix));

        var shortId = instanceId.ToString("N")[..8];
        return $"{servicePrefix}-{shortId}.{internalRootSuffix}";
    }
}
