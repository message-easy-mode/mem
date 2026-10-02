namespace HostAgent.Runtime.ServiceRuntime;

public static class RuntimeConventions
{
    // Docker network shared with ingress (NPM today)
    // public const string GatewayNetworkName = "mem-gateway";

    // Common docker label keys
    public const string LabelInstanceId = "mem.instanceId";
    public const string LabelServiceKey = "mem.serviceKey";

    public static string ContainerName(string serviceKey, Guid instanceId)
    {
        if (instanceId == Guid.Empty) throw new ArgumentException("instanceId required", nameof(instanceId));

        var key = NormalizeServiceKey(serviceKey);
        if (key.Length == 0) throw new ArgumentException("serviceKey required", nameof(serviceKey));

        return $"mem-{key}-{instanceId:N}";
    }

    public static Dictionary<string, string> Labels(Guid instanceId, string serviceKey)
    {
        if (instanceId == Guid.Empty) throw new ArgumentException("instanceId required", nameof(instanceId));

        var key = NormalizeServiceKey(serviceKey);
        if (key.Length == 0) throw new ArgumentException("serviceKey required", nameof(serviceKey));

        return new Dictionary<string, string>
        {
            [LabelInstanceId] = instanceId.ToString(),
            [LabelServiceKey] = key
        };
    }

    public static string NormalizeServiceKey(string value) => (value ?? "").Trim().ToLowerInvariant();
}
