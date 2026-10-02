using System.Reflection;
using Mem.Migrate.Infrastructure.Conversion;

namespace Mem.Migrate.UnitTests;

public sealed class PrivateCandidateConfigurationTests
{
    [Fact]
    public void Candidate_configuration_is_private_postgres_backed_and_identity_bound()
    {
        var method = typeof(PrivateCandidateService).GetMethod(
            "BuildCandidateConfiguration",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("BuildCandidateConfiguration was not found.");

        var configuration = method.Invoke(null, ["matrix.example.test", "candidate-postgres", "password"]) as string
            ?? throw new InvalidOperationException("No candidate configuration was returned.");
        var normalized = configuration.ReplaceLineEndings("\n");

        Assert.Contains("server_name: \"matrix.example.test\"", normalized, StringComparison.Ordinal);
        Assert.Contains("signing_key_path: \"/candidate/signing.key\"", normalized, StringComparison.Ordinal);
        Assert.Contains("media_store_path: \"/candidate/media_store\"", normalized, StringComparison.Ordinal);
        Assert.Contains("host: candidate-postgres", normalized, StringComparison.Ordinal);
        Assert.Contains("names: [client, federation]", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("https_port", normalized, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("null", true)]
    [InlineData("{\"8008/tcp\":null}", true)]
    [InlineData("{\"8008/tcp\":[{\"HostIp\":\"0.0.0.0\",\"HostPort\":\"18008\"}]}", false)]
    public void Published_port_gate_distinguishes_exposed_bindings(string json, bool expected)
    {
        var method = typeof(PrivateCandidateService).GetMethod(
            "PublishedPortsAreAbsent",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("PublishedPortsAreAbsent was not found.");

        var actual = (bool)(method.Invoke(null, [json])
            ?? throw new InvalidOperationException("PublishedPortsAreAbsent returned no result."));
        Assert.Equal(expected, actual);
    }
}
