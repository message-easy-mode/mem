using System.Net;

namespace Mem.Migrate.Web.Hosting;

internal sealed record SourceAssistantOptions(
    IPAddress ListenAddress,
    int Port,
    bool AllowRemote,
    string WorkspaceRoot,
    string ArtifactRoot,
    TimeSpan SessionIdleTimeout,
    TimeSpan SessionAbsoluteTimeout)
{
    public bool IsLoopback => IPAddress.IsLoopback(ListenAddress);

    public string ListenerDisplay => $"http://{FormatAddress(ListenAddress)}:{Port}";

    public string TunnelTargetDisplay => FormatAddress(ListenAddress);

    private static string FormatAddress(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{address}]"
            : address.ToString();
}
