using Microsoft.Extensions.Options;
using HostAgent.Options;

namespace HostAgent.Runtime.ServiceRuntime;

public sealed class InstancePathProvider(IOptions<InstanceStorageOptions> opts)
{
    public string InstanceRoot(Guid instanceId)
        => Path.Combine(opts.Value.ResolveRequiredRoot(), instanceId.ToString());

    public string ServiceDataPath(Guid instanceId, string serviceKey)
        => Path.Combine(InstanceRoot(instanceId), (serviceKey ?? "").Trim().ToLowerInvariant());
}
