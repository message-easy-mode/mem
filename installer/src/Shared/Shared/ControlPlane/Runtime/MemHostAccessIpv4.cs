using System.Net;
using System.Net.Sockets;

namespace Shared.ControlPlane.Runtime;

public static class MemHostAccessIpv4
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();
        if (!IPAddress.TryParse(candidate, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.Broadcast))
        {
            throw new MemRuntimeContextValidationException(
                "runtime_host_ipv4_invalid",
                "MEM_CONTROL_PLANE_HOST_IPV4 must be a concrete IPv4 address when supplied.");
        }

        return address.ToString();
    }

    /// <summary>
    /// Returns true only for browser-access candidates that are normally
    /// private to a LAN or overlay network. Public addresses are never
    /// advertised automatically by MEM.
    /// </summary>
    public static bool IsPrivateBrowserCandidate(string? value)
    {
        if (!IPAddress.TryParse(value, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        // RFC1918.
        if (bytes[0] == 10 ||
            (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
            (bytes[0] == 192 && bytes[1] == 168))
        {
            return true;
        }

        // RFC6598 shared address space; useful for private overlay/VPN
        // networks such as Tailscale without advertising arbitrary public IPs.
        return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }
}
