using System.Net;
using System.Net.Sockets;

namespace Core.Runtime;

public sealed class PortCheckService
{
    public bool IsPortAvailable(int port)
    {
        if (port < 1 || port > 65535)
            return false;

        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public int FindAvailablePort(int startingPort, int range = 1000)
    {
        if (startingPort < 1 || startingPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(startingPort));

        var upper = Math.Min(startingPort + range, 65535);

        for (var port = startingPort; port <= upper; port++)
        {
            if (IsPortAvailable(port))
                return port;
        }

        throw new InvalidOperationException(
            $"No available port found in range {startingPort}-{upper}.");
    }
}