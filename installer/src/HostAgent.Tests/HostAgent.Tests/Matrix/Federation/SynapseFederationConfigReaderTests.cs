using HostAgent.Matrix.Federation;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class SynapseFederationConfigReaderTests
{
    [Fact]
    public async Task Detects_public_when_the_top_level_allowlist_key_is_absent()
    {
        var result = await ReadAsync("server_name: matrix.example\npublic_baseurl: https://matrix.example\n");

        Assert.Equal(FederationModes.Public, result.Mode);
        Assert.Equal(SynapseFederationConfigKinds.KeyAbsent, result.Kind);
        Assert.Empty(result.Allowlist);
        Assert.StartsWith("sha256:", result.FileSha256, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detects_local_only_from_the_exact_inline_empty_list()
    {
        var result = await ReadAsync("server_name: matrix.example\r\nfederation_domain_whitelist: [] # MEM\r\n");

        Assert.Equal(FederationModes.LocalOnly, result.Mode);
        Assert.Equal(SynapseFederationConfigKinds.EmptyList, result.Kind);
        Assert.True(result.Supported);
    }

    [Fact]
    public async Task Detects_and_canonicalises_a_supported_restricted_block_list()
    {
        var result = await ReadAsync("""
server_name: matrix.example
federation_domain_whitelist:
  # approved partner
  - Partner.Example.
  - bücher.example # IDNA
listeners:
  - port: 8008
""");

        Assert.Equal(FederationModes.Restricted, result.Mode);
        Assert.Equal(SynapseFederationConfigKinds.ExactDomainList, result.Kind);
        Assert.Equal(
            ["partner.example", "xn--bcher-kva.example"],
            result.Allowlist);
    }

    [Theory]
    [InlineData("federation_domain_whitelist: [matrix.example]\n")]
    [InlineData("federation_domain_whitelist: &partners\n  - matrix.example\n")]
    [InlineData("federation_domain_whitelist:\n  - *.example\n")]
    [InlineData("federation_domain_whitelist:\n  matrix.example: true\n")]
    [InlineData("\"federation_domain_whitelist\": []\n")]
    [InlineData("{federation_domain_whitelist: []}\n")]
    public async Task Refuses_custom_or_unsafe_yaml_representations(string yaml)
    {
        var result = await ReadAsync(yaml);

        Assert.Equal(FederationModes.Unknown, result.Mode);
        Assert.Equal(SynapseFederationConfigKinds.CustomUnsupported, result.Kind);
        Assert.False(result.Supported);
        Assert.Equal("federation_config_custom_unsupported", result.ProblemCode);
    }

    [Fact]
    public async Task Refuses_duplicate_top_level_keys_without_returning_the_file_body()
    {
        const string secretMarker = "registration_shared_secret: do-not-return-this-value";
        var result = await ReadAsync($"""
{secretMarker}
federation_domain_whitelist: []
federation_domain_whitelist:
  - matrix.example
""");

        Assert.Equal("federation_config_ambiguous", result.ProblemCode);
        Assert.DoesNotContain("do-not-return-this-value", result.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_invalid_utf8_without_exposing_file_content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-federation-{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllBytesAsync(path, [0xFF, 0xFE, 0xFD]);
            var reader = new SynapseFederationConfigReader(new FederationDomainValidator());

            var result = await reader.ReadAsync(path, CancellationToken.None);

            Assert.Equal(FederationModes.Unknown, result.Mode);
            Assert.Equal("federation_config_invalid", result.ProblemCode);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task<SynapseFederationConfigReadResult> ReadAsync(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-federation-{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllTextAsync(path, yaml);
            var reader = new SynapseFederationConfigReader(new FederationDomainValidator());
            return await reader.ReadAsync(path, CancellationToken.None);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
