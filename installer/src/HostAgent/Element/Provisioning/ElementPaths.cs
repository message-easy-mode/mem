using HostAgent.Runtime.ServiceRuntime;

namespace HostAgent.Element.Provisioning;

public static class ElementPaths
{
    private const string Folder = "element";

    public static string DataPath(this InstancePathProvider paths, Guid instanceId)
        => paths.ServiceDataPath(instanceId, Folder);

    public static string ConfigDir(this InstancePathProvider paths, Guid instanceId)
        => Path.Combine(paths.DataPath(instanceId), "config");

    public static string DefaultConfigPath(this InstancePathProvider paths, Guid instanceId)
        => Path.Combine(paths.ConfigDir(instanceId), "config.json");

    public static string HostConfigPath(this InstancePathProvider paths, Guid instanceId, string host)
    {
        host = (host ?? "").Trim().ToLowerInvariant();
        if (host.Length == 0) throw new ArgumentException("host required", nameof(host));

        return Path.Combine(paths.ConfigDir(instanceId), $"config.{host}.json");
    }
}
