using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace HostAgent.Runtime.Coturn;

/// <summary>
/// Bounded evidence from Coturn's authenticated allocation probe. An advertised
/// public address is necessary for Internet TURN, but is NOT a reachability or
/// media-path test. Local DNS and successful allocation alone cannot prove it.
/// </summary>
internal static class CoturnRelayAddressPolicy
{
    internal const int MaximumOutputCharacters = 64 * 1024;
    internal const int MaximumRelayObservations = 32;
    private const string RelayMarker = "Received relay addr:";
    private const string Boundary =
        "External-client reachability and NAT/firewall port preservation still require an independent external test.";

    private static readonly HashSet<string> CurrentCodes = new(StringComparer.Ordinal)
    {
        "relay-auto-public", "relay-explicit-public", "relay-non-public",
        "relay-mismatch", "relay-inconclusive", "relay-allocation-failed",
        "relay-allocation-not-run", "relay-port-out-of-range"
    };

    internal static bool HasCurrentEvidence(CoturnCheckResponse result) =>
        result.Checks.Any(check => check.Key == "external-ip" &&
                                  check.Code is not null && CurrentCodes.Contains(check.Code));

    internal static CoturnRelayAddressEvidence Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output) || output.Length > MaximumOutputCharacters)
        {
            return new CoturnRelayAddressEvidence(false, Array.Empty<CoturnAdvertisedRelay>());
        }

        var relays = new List<CoturnAdvertisedRelay>();
        var complete = true;
        var observations = 0;
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            var marker = line.IndexOf(RelayMarker, StringComparison.Ordinal);
            if (marker < 0) continue;
            if (++observations > MaximumRelayObservations)
            {
                complete = false;
                break;
            }

            // Only the precise uclient IPv4/IPv6 relay evidence is accepted.
            // Never copy other output (which may contain credentials) into this DTO.
            var prefix = line[..marker].TrimEnd();
            var family = prefix.EndsWith("IPv4.", StringComparison.Ordinal)
                ? AddressFamily.InterNetwork
                : prefix.EndsWith("IPv6.", StringComparison.Ordinal)
                    ? AddressFamily.InterNetworkV6
                    : AddressFamily.Unspecified;
            var endpoint = line[(marker + RelayMarker.Length)..].Trim();
            var separator = endpoint.LastIndexOf(':');
            if (family == AddressFamily.Unspecified || separator <= 0 ||
                !int.TryParse(endpoint[(separator + 1)..], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            {
                complete = false;
                continue;
            }

            var literal = endpoint[..separator];
            if (literal.StartsWith('[') && literal.EndsWith(']')) literal = literal[1..^1];
            if (literal.Contains('%') || !IPAddress.TryParse(literal, out var address) ||
                address.AddressFamily != family)
            {
                complete = false;
                continue;
            }

            relays.Add(new CoturnAdvertisedRelay(address.ToString(), port));
        }

        return new CoturnRelayAddressEvidence(
            Complete: complete && observations > 0 && relays.Count == observations,
            Relays: relays.Distinct().ToArray());
    }

    internal static CoturnCheckItem Evaluate(
        string? externalIp,
        CoturnAllocationProbeResponse allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        var evidence = allocation.RelayAddressEvidence;
        // Only bounded, normalized addresses from our parser reach the detail.
        var detail = Describe(evidence);
        if (allocation.Status == CoturnCheckStatuses.Failed)
        {
            return Result(CoturnCheckStatuses.Failed, "relay-allocation-failed",
                "The allocation probe failed; advertised relay-address verification did not pass.", detail);
        }

        if (allocation.Status != CoturnCheckStatuses.Passed)
        {
            return Result(CoturnCheckStatuses.NotRun, "relay-allocation-not-run",
                "The advertised relay address has not been verified by a successful allocation probe.", detail);
        }

        var relays = evidence?.Relays ?? Array.Empty<CoturnAdvertisedRelay>();
        if (relays.Any(relay => !IPAddress.TryParse(relay.Address, out var ip) || !IsPublicAddress(ip)))
        {
            return Result(CoturnCheckStatuses.Failed, "relay-non-public",
                "Coturn advertised a non-public relay address. Internet clients cannot use this as a public TURN relay.", detail);
        }

        if (relays.Any(relay => relay.Port < CoturnRuntimePolicy.RelayMinPort ||
                                relay.Port > CoturnRuntimePolicy.RelayMaxPort))
        {
            return Result(CoturnCheckStatuses.Failed, "relay-port-out-of-range",
                "Coturn advertised a relay port outside the MEM-published UDP range.", detail);
        }

        IPAddress? expected = null;
        if (!string.IsNullOrWhiteSpace(externalIp) &&
            (!IPAddress.TryParse(externalIp.Trim(), out expected) ||
             relays.Any(relay => !IPAddress.Parse(relay.Address).Equals(expected))))
        {
            return Result(CoturnCheckStatuses.Failed, "relay-mismatch",
                "The observed relay address does not match the configured external/public IP.", detail);
        }

        if (evidence?.Complete != true || relays.Count == 0)
        {
            return Result(CoturnCheckStatuses.Warning, "relay-inconclusive",
                "Allocation succeeded, but complete advertised relay-address evidence is unavailable. Run a fresh check before relying on external TURN.", detail);
        }

        return expected is null
            ? Result(CoturnCheckStatuses.Passed, "relay-auto-public",
                "Automatic detection advertised a public relay address in the allocation response.", detail)
            : Result(CoturnCheckStatuses.Passed, "relay-explicit-public",
                "The allocation response advertised the configured public relay address.", detail);
    }

    // Conservative public-unicast classification, not a routing guarantee.
    // Deliberately excludes documentation, benchmarking, transition and reserved
    // space as well as private, loopback, link-local and multicast addresses.
    internal static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 ||
                     (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                     (bytes[0] == 169 && bytes[1] == 254) ||
                     (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                     (bytes[0] == 192 && (bytes[1] == 168 ||
                         (bytes[1] == 0 && bytes[2] is 0 or 2) ||
                         (bytes[1] == 88 && bytes[2] == 99))) ||
                     (bytes[0] == 198 && (bytes[1] is 18 or 19 ||
                         (bytes[1] == 51 && bytes[2] == 100))) ||
                     (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113));
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.ScopeId != 0 || address.IsIPv4MappedToIPv6 ||
            (bytes[0] & 0xe0) != 0x20)
        {
            return false;
        }

        return !((bytes[0] == 0x20 && bytes[1] == 0x01 &&
                     (bytes[2] < 2 || (bytes[2] == 0x0d && bytes[3] == 0xb8))) ||
                 (bytes[0] == 0x20 && bytes[1] == 0x02) ||
                 (bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0));
    }

    private static CoturnCheckItem Result(string status, string code, string summary, string detail) =>
        new("external-ip", status, summary, detail) { Code = code };

    private static string Describe(CoturnRelayAddressEvidence? evidence)
    {
        var endpoints = (evidence?.Relays ?? Array.Empty<CoturnAdvertisedRelay>())
            .Take(MaximumRelayObservations)
            .Where(relay => IPAddress.TryParse(relay.Address, out _) && relay.Port is > 0 and <= 65535)
            .Select(relay => $"[{IPAddress.Parse(relay.Address)}]:{relay.Port.ToString(CultureInfo.InvariantCulture)}");
        var observed = string.Join(", ", endpoints);
        return (observed.Length == 0 ? "No relay address was observed. " : $"Observed relays: {observed}. ") + Boundary;
    }
}
