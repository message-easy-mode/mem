using System.Net;
using System.Net.Sockets;

namespace HostAgent.Matrix.Federation.PrivateNetwork;

public sealed record CanonicalPrivateNetworkAddress(
    string Address,
    string Cidr,
    AddressFamily AddressFamily);

public sealed class PrivateNetworkAddressValidator
{
    public CanonicalPrivateNetworkAddress ValidateExactPrivateAddress(string? value) =>
        ValidateExactAddress(value, requirePrivate: true);

    public CanonicalPrivateNetworkAddress ValidateExactAddressForRemoval(string? value) =>
        ValidateExactAddress(value, requirePrivate: false);

    private static CanonicalPrivateNetworkAddress ValidateExactAddress(
        string? value,
        bool requirePrivate)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw Invalid("private_network_address_required", "Enter one exact private IPv4 or IPv6 address.");
        }

        if (candidate.Contains('/'))
        {
            throw Invalid(
                "private_network_exact_address_required",
                "Enter an exact IP address. MEM generates the /32 or /128 exception and does not accept broad CIDR ranges.");
        }

        if (!IPAddress.TryParse(candidate, out var parsed))
        {
            throw Invalid("private_network_address_invalid", "The private network exception must be an IP address, not a hostname or URL.");
        }

        if (parsed.IsIPv4MappedToIPv6)
        {
            parsed = parsed.MapToIPv4();
        }

        if (requirePrivate &&
            (IPAddress.IsLoopback(parsed) || IsUnspecified(parsed) || IsMulticast(parsed) || IsLinkLocal(parsed)))
        {
            throw Invalid(
                "private_network_address_unsafe",
                "Loopback, unspecified, multicast, and link-local addresses cannot be added as private network exceptions.");
        }

        if (requirePrivate && !IsPrivate(parsed))
        {
            throw Invalid(
                "private_network_address_not_private",
                "This control accepts only an exact RFC1918 IPv4 or unique-local IPv6 address. Public addresses do not require this exception.");
        }

        var canonical = parsed.ToString().ToLowerInvariant();
        var prefix = parsed.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        return new CanonicalPrivateNetworkAddress(
            canonical,
            $"{canonical}/{prefix}",
            parsed.AddressFamily);
    }

    internal static string CanonicaliseCidr(string value)
    {
        var candidate = value.Trim();
        var slash = candidate.LastIndexOf('/');
        if (slash <= 0 || slash == candidate.Length - 1)
        {
            throw new FormatException("An IP range entry must contain an address and prefix length.");
        }

        if (!IPAddress.TryParse(candidate[..slash], out var address) ||
            !int.TryParse(candidate[(slash + 1)..], out var prefix))
        {
            throw new FormatException("An IP range entry is invalid.");
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var maximum = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (prefix < 0 || prefix > maximum)
        {
            throw new FormatException("An IP range prefix length is invalid.");
        }

        return $"{address.ToString().ToLowerInvariant()}/{prefix}";
    }

    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                (bytes[0] == 192 && bytes[1] == 168);
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 &&
            (bytes[0] & 0xFE) == 0xFC;
    }

    private static bool IsUnspecified(IPAddress address) =>
        address.Equals(IPAddress.Any) ||
        address.Equals(IPAddress.IPv6Any);

    private static bool IsLinkLocal(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsMulticast(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6Multicast;
        }

        var first = address.GetAddressBytes()[0];
        return first is >= 224 and <= 239;
    }

    private static PrivateNetworkFederationException Invalid(string code, string detail) =>
        new(code, detail);
}
