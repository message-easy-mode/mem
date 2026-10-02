using System.Net;
using System.Net.Sockets;

namespace HostAgent.Docker;

public static class HostPortAllocator
{
    public static int AllocateEphemeralPort(int minPort = 20000, int maxPort = 40000)
    {
        // Try a few random ports; if all fail, fall back to OS ephemeral allocation.
        var rnd = new Random();

        for (var i = 0; i < 20; i++)
        {
            var port = rnd.Next(minPort, maxPort);
            if (IsPortFree(port))
                return port;
        }

        // Fallback: let OS pick
        using var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        return ((IPEndPoint)l.LocalEndpoint).Port;
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            using var l = new TcpListener(IPAddress.Any, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
