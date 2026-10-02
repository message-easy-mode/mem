using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using HostAgent.Options;

namespace HostAgent.Matrix.Users;

internal sealed record SynapseControlPlaneConnectTarget(
    string Host,
    int Port);

internal static class SynapseControlPlaneHttpClientFactory
{
    internal const string NpmContainerName = "mem-npm";

    public static HttpClient Create(
        Uri targetBaseUri,
        RuntimeConnectivityContext connectivityContext)
    {
        ArgumentNullException.ThrowIfNull(targetBaseUri);
        ArgumentNullException.ThrowIfNull(connectivityContext);

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectCallback = async (context, cancellationToken) =>
            {
                // Preserve the public Matrix Host/SNI name while choosing the actual
                // NPM connection authority from the active runtime boundary. A host
                // process reaches the published NPM HTTPS listener over loopback; a
                // containerized Control Plane reaches the same route over mem-gateway.
                var target = ResolveConnectTarget(
                    targetBaseUri,
                    context.DnsEndPoint,
                    connectivityContext);

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true
                };

                try
                {
                    await socket.ConnectAsync(
                        target.Host,
                        target.Port,
                        cancellationToken);

                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    internal static SynapseControlPlaneConnectTarget ResolveConnectTarget(
        Uri targetBaseUri,
        DnsEndPoint requestedEndPoint,
        RuntimeConnectivityContext connectivityContext)
    {
        ArgumentNullException.ThrowIfNull(targetBaseUri);
        ArgumentNullException.ThrowIfNull(requestedEndPoint);
        ArgumentNullException.ThrowIfNull(connectivityContext);

        var port = requestedEndPoint.Port;

        if (port <= 0)
        {
            port = string.Equals(
                targetBaseUri.Scheme,
                "https",
                StringComparison.OrdinalIgnoreCase)
                ? 443
                : 80;
        }

        var host = connectivityContext.MatrixAdminConnectionMode switch
        {
            ServiceConnectionMode.HostLoopback => IPAddress.Loopback.ToString(),
            ServiceConnectionMode.DockerNetwork => NpmContainerName,
            _ => throw new InvalidOperationException(
                $"Unsupported Matrix admin connection mode '{connectivityContext.MatrixAdminConnectionMode}'.")
        };

        return new SynapseControlPlaneConnectTarget(host, port);
    }
}
