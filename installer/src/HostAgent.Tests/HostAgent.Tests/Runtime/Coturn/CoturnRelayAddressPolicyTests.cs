using System.Net;
using System.Text.Json;
using HostAgent.Runtime.Coturn;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnRelayAddressPolicyTests
{
    // Public-looking literals below are synthetic evidence; no test connects to them.
    [Fact]
    public void Qa8_private_allocation_transcript_is_not_a_green_external_ip_check()
    {
        var probe = Probe("""
            0: (25): INFO: success
            0: (25): INFO: IPv4. Received relay addr: 172.18.0.5:49168
            0: (25): INFO: IPv4. Received relay addr: 172.18.0.5:49174
            0: (25): INFO: IPv4. Received relay addr: 172.18.0.5:49190
            5: (25): INFO: start_mclient: tot_send_msgs=2, tot_recv_msgs=2
            5: (25): INFO: Total lost packets 0 (0.000000%), total send dropped 2 (50.000000%)
            """);
        var result = CoturnRuntimeService.BuildExternalIpCheck(null, probe);
        Assert.True(probe.RelayAddressEvidence!.Complete);
        Assert.Equal(3, probe.RelayAddressEvidence.Relays.Count);
        Assert.Equal("relay-non-public", result.Code);
        Assert.Equal(CoturnCheckStatuses.Failed, result.Status);
        Assert.Equal(CoturnCheckStatuses.Failed, CoturnDiagnosticsPolicy.SummarizeStatus([result], probe));
    }

    [Theory]
    [InlineData(null, "8.8.8.8", "relay-auto-public")]
    [InlineData("8.8.8.8", "8.8.8.8", "relay-explicit-public")]
    [InlineData("2606:4700:4700:0:0:0:0:1111", "2606:4700:4700::1111", "relay-explicit-public")]
    public void Public_relay_is_qualified_by_observed_address_not_by_the_mode(
        string? configured, string relay, string expectedCode)
    {
        var version = relay.Contains(':') ? 6 : 4;
        var probe = Probe($"0: INFO: IPv{version}. Received relay addr: [{relay}]:49161");
        var result = CoturnRelayAddressPolicy.Evaluate(configured, probe);
        Assert.Equal(CoturnCheckStatuses.Passed, result.Status);
        Assert.Equal(expectedCode, result.Code);
        Assert.Contains("independent external test", result.Detail!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("10.10.0.198")]
    [InlineData("172.18.0.5")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.5")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.19.255.255")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("240.1.1.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:2::1")]
    [InlineData("2002::1")]
    [InlineData("3fff::1")]
    public void Non_public_addresses_do_not_prove_an_Internet_relay(string address)
    {
        Assert.False(CoturnRelayAddressPolicy.IsPublicAddress(IPAddress.Parse(address)));
        var family = address.Contains(':') ? 6 : 4;
        var result = CoturnRelayAddressPolicy.Evaluate(address,
            Probe($"0: INFO: IPv{family}. Received relay addr: [{address}]:49168"));
        Assert.Equal(CoturnCheckStatuses.Failed, result.Status);
        Assert.Equal("relay-non-public", result.Code);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.1.1")]
    [InlineData("172.32.1.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Classifier_does_not_reject_neighbouring_global_unicast_ranges(string address) =>
        Assert.True(CoturnRelayAddressPolicy.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public void Explicit_ip_mismatch_is_failed_even_when_both_addresses_are_public()
    {
        var result = CoturnRelayAddressPolicy.Evaluate("1.1.1.1",
            Probe("0: INFO: IPv4. Received relay addr: 8.8.8.8:49168"));
        Assert.Equal("relay-mismatch", result.Code);
        Assert.Equal(CoturnCheckStatuses.Failed, result.Status);
    }

    [Fact]
    public void Mixed_public_and_private_observations_cannot_be_hidden_by_last_public_line()
    {
        var result = CoturnRelayAddressPolicy.Evaluate(null, Probe("""
            0: INFO: IPv4. Received relay addr: 172.18.0.5:49168
            0: INFO: IPv4. Received relay addr: 8.8.8.8:49190
            """));
        Assert.Equal("relay-non-public", result.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("allocation completed")]
    [InlineData("0: INFO: IPv4. Received relay addr: not-an-ip:49168")]
    [InlineData("0: INFO: IPv4. Received relay addr: 8.8.8.8")]
    [InlineData("0: INFO: IPv4. Received relay addr: 8.8.8.8:0")]
    [InlineData("0: INFO: IPv4. Received relay addr: 8.8.8.8:65536")]
    [InlineData("0: INFO: IPv6. Received relay addr: 8.8.8.8:49168")]
    [InlineData("0: INFO: IPv6. Received relay addr: [fe80::1%eth0]:49168")]
    [InlineData("0: INFO: Received relay addr: 8.8.8.8:49168")]
    public void Missing_or_malformed_evidence_is_not_passed(string? log)
    {
        var result = CoturnRelayAddressPolicy.Evaluate(null, Probe(log));
        Assert.Equal("relay-inconclusive", result.Code);
        Assert.Equal(CoturnCheckStatuses.Warning, result.Status);
    }

    [Fact]
    public void One_valid_line_does_not_hide_an_unparseable_allocation()
    {
        var result = CoturnRelayAddressPolicy.Evaluate(null, Probe("""
            0: INFO: IPv4. Received relay addr: 8.8.8.8:49168
            0: INFO: IPv4. Received relay addr: malformed
            """));
        Assert.Equal(CoturnCheckStatuses.Warning, result.Status);
    }

    [Fact]
    public void Legacy_probe_without_typed_evidence_cannot_gain_a_new_green_result()
    {
        var probe = new CoturnAllocationProbeResponse("passed", "udp", "old", "IPv4. Received relay addr: 8.8.8.8:49168");
        Assert.Equal("relay-inconclusive", CoturnRelayAddressPolicy.Evaluate(null, probe).Code);
    }

    [Theory]
    [InlineData(49159)]
    [InlineData(49201)]
    public void Public_relay_port_must_be_in_published_range(int port)
    {
        var result = CoturnRelayAddressPolicy.Evaluate(null,
            Probe($"IPv4. Received relay addr: 8.8.8.8:{port}"));
        Assert.Equal(CoturnCheckStatuses.Failed, result.Status);
        Assert.Equal("relay-port-out-of-range", result.Code);
    }

    [Fact]
    public void Probe_failure_is_not_overridden_by_plausible_address_evidence()
    {
        var failed = Probe("IPv4. Received relay addr: 8.8.8.8:49168") with { Status = "failed" };
        var result = CoturnRelayAddressPolicy.Evaluate(null, failed);
        Assert.Equal("relay-allocation-failed", result.Code);
        Assert.Equal(CoturnCheckStatuses.Failed, result.Status);
    }

    [Fact]
    public void Probe_not_run_is_explicit_and_degrades_overall_health()
    {
        var notRun = new CoturnAllocationProbeResponse("not-run", "udp", "restricted", null);
        var result = CoturnRelayAddressPolicy.Evaluate(null, notRun);
        Assert.Equal("relay-allocation-not-run", result.Code);
        Assert.Equal(CoturnCheckStatuses.NotRun, result.Status);
        Assert.Equal(CoturnCheckStatuses.Warning, CoturnDiagnosticsPolicy.SummarizeStatus([result], notRun));
    }

    [Fact]
    public void Typed_evidence_survives_tail_truncation_without_copying_credentials()
    {
        var raw = "password=secret-value\nIPv4. Received relay addr: 172.18.0.5:49168\n" +
                  string.Join('\n', Enumerable.Repeat("bounded repeated output", 205));
        var probe = Probe(raw) with { LogTail = CoturnDiagnosticsPolicy.SanitizeDiagnosticText(raw, "secret-value").Content };
        Assert.DoesNotContain("Received relay addr", probe.LogTail!, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", JsonSerializer.Serialize(probe.RelayAddressEvidence), StringComparison.Ordinal);
        Assert.Equal("relay-non-public", CoturnRelayAddressPolicy.Evaluate(null, probe).Code);
    }

    [Fact]
    public void Excessive_output_or_observation_count_is_bounded_and_inconclusive()
    {
        var huge = Probe(new string('x', CoturnRelayAddressPolicy.MaximumOutputCharacters + 1));
        Assert.Equal("relay-inconclusive", CoturnRelayAddressPolicy.Evaluate(null, huge).Code);
        var excessive = Probe(string.Join('\n', Enumerable.Repeat(
            "IPv4. Received relay addr: 8.8.8.8:49168", CoturnRelayAddressPolicy.MaximumRelayObservations + 1)));
        Assert.False(excessive.RelayAddressEvidence!.Complete);
        Assert.True(excessive.RelayAddressEvidence.Relays.Count <= CoturnRelayAddressPolicy.MaximumRelayObservations);
        Assert.Equal("relay-inconclusive", CoturnRelayAddressPolicy.Evaluate(null, excessive).Code);
    }

    [Fact]
    public void Bracketed_and_unbracketed_ipv6_are_normalized_and_round_trip_as_safe_json()
    {
        var evidence = CoturnRelayAddressPolicy.Parse("""
            0: INFO: IPv6. Received relay addr: [2606:4700:4700::1111]:49168
            0: INFO: IPv6. Received relay addr: 2606:4700:4700::1111:49174
            """);
        Assert.True(evidence.Complete);
        Assert.All(evidence.Relays, item => Assert.Equal("2606:4700:4700::1111", item.Address));
        var restored = JsonSerializer.Deserialize<CoturnRelayAddressEvidence>(JsonSerializer.Serialize(evidence));
        Assert.NotNull(restored);
        Assert.True(restored!.Complete);
        Assert.Equal(evidence.Relays, restored.Relays);
    }

    private static CoturnAllocationProbeResponse Probe(string? output) =>
        new("passed", "udp", "local allocation complete", null)
        {
            RelayAddressEvidence = CoturnRelayAddressPolicy.Parse(output)
        };
}
