using HostAgent.Docker.Models;
using HostAgent.Runtime.ServiceRuntime;

namespace HostAgent.Matrix.Provisioning;

public static class MatrixContainerSpecBuilder
{
    public const string SynapseHttpPort = "8008/tcp";
    private const string SynapseDataDirInContainer = "/data";

    public static DockerContainerSpec BuildRuntime(
        string containerName,
        string image,
        Guid instanceId,
        int hostPort,
        string dataPath,
        string serverName,
        string? user,
        string? networkName,
        IReadOnlyList<string>? networkAliases,
        bool reportStats = false)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
            throw new ArgumentException("dataPath is required.", nameof(dataPath));

        var labels = RuntimeConventions.Labels(instanceId, ServiceKeys.Matrix);

        var env = new Dictionary<string, string>
        {
            ["TZ"] = "UTC",
            ["SYNAPSE_SERVER_NAME"] = serverName,
            ["SYNAPSE_REPORT_STATS"] = reportStats ? "yes" : "no"
        };

        var portBindings = new Dictionary<string, string>
        {
            [SynapseHttpPort] = hostPort.ToString()
        };

        var bindMounts = new List<BindMount>
        {
            new BindMount(HostPath: dataPath, ContainerPath: SynapseDataDirInContainer, ReadOnly: false)
        };

        return new DockerContainerSpec(
            Name: containerName,
            Image: image,
            Env: env,
            Labels: labels,
            Cmd: null,
            PortBindings: portBindings,
            AutoRemove: false,
            BindMounts: bindMounts,
            User: string.IsNullOrWhiteSpace(user) ? null : user,
            RestartPolicy: new DockerRestartPolicy(DockerRestartPolicyName.UnlessStopped),
            NetworkName: string.IsNullOrWhiteSpace(networkName) ? null : networkName,
            NetworkAliases: networkAliases
        );
    }
}
