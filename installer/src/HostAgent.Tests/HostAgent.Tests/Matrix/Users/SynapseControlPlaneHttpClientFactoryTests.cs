using System.Net;
using HostAgent.Matrix.Users;
using HostAgent.Options;

namespace HostAgent.Tests.Matrix.Users;

public sealed class SynapseControlPlaneHttpClientFactoryTests
{
    [Fact]
    public void ResolveConnectTarget_uses_host_loopback_for_local_control_plane()
    {
        var target = SynapseControlPlaneHttpClientFactory.ResolveConnectTarget(
            new Uri("https://matrix.example.test"),
            new DnsEndPoint("matrix.example.test", 443),
            new RuntimeConnectivityContext
            {
                MatrixAdminConnectionMode = ServiceConnectionMode.HostLoopback
            });

        Assert.Equal(IPAddress.Loopback.ToString(), target.Host);
        Assert.Equal(443, target.Port);
    }

    [Fact]
    public void ResolveConnectTarget_uses_npm_docker_authority_for_containerized_control_plane()
    {
        var target = SynapseControlPlaneHttpClientFactory.ResolveConnectTarget(
            new Uri("https://matrix.example.test"),
            new DnsEndPoint("matrix.example.test", 443),
            new RuntimeConnectivityContext
            {
                MatrixAdminConnectionMode = ServiceConnectionMode.DockerNetwork
            });

        Assert.Equal(SynapseControlPlaneHttpClientFactory.NpmContainerName, target.Host);
        Assert.Equal(443, target.Port);
    }

    [Fact]
    public void ResolveConnectTarget_rejects_unknown_runtime_connection_mode()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            SynapseControlPlaneHttpClientFactory.ResolveConnectTarget(
                new Uri("https://matrix.example.test"),
                new DnsEndPoint("matrix.example.test", 443),
                new RuntimeConnectivityContext
                {
                    MatrixAdminConnectionMode = (ServiceConnectionMode)999
                }));

        Assert.Contains("Unsupported Matrix admin connection mode", ex.Message, StringComparison.Ordinal);
    }
}
