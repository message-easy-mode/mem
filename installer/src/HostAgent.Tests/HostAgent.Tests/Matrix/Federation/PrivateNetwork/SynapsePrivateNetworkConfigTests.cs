using System.Text;
using HostAgent.Matrix.Federation.PrivateNetwork;

namespace HostAgent.Tests.Matrix.Federation.PrivateNetwork;

public sealed class SynapsePrivateNetworkConfigTests
{
    [Fact]
    public async Task Reads_absent_and_supported_exact_entries()
    {
        var absent = await ReadAsync("server_name: matrix.example\n");
        Assert.True(absent.Supported);
        Assert.Empty(absent.Entries);

        var configured = await ReadAsync("""
server_name: matrix.example
ip_range_whitelist:
  - "10.0.0.238/32"
  - fd12:3456::10/128
listeners:
  - port: 8008
""");
        Assert.True(configured.Supported);
        Assert.Equal(["10.0.0.238/32", "fd12:3456::10/128"], configured.Entries);
    }

    [Theory]
    [InlineData("ip_range_whitelist: [10.0.0.238/32]\n")]
    [InlineData("\"ip_range_whitelist\": []\n")]
    [InlineData("ip_range_whitelist:\n  private: true\n")]
    public async Task Refuses_custom_yaml_representations(string yaml)
    {
        var result = await ReadAsync(yaml);

        Assert.False(result.Supported);
        Assert.Equal("private_network_config_custom_unsupported", result.ProblemCode);
    }

    [Fact]
    public void Adds_and_removes_only_the_exact_entry_while_preserving_other_config()
    {
        var editor = new SynapsePrivateNetworkConfigEditor();
        var original = Encoding.UTF8.GetBytes("""
server_name: matrix.example
federation_domain_whitelist:
  - partner.example

ip_range_whitelist:
  - "10.0.0.10/32"
# Preserve this unrelated operator comment.

listeners:
  - port: 8008
""");

        var added = Encoding.UTF8.GetString(editor.Render(original, "10.0.0.238/32", enabled: true));
        Assert.Contains("federation_domain_whitelist:\n  - partner.example", added, StringComparison.Ordinal);
        Assert.Contains("  - \"10.0.0.10/32\"", added, StringComparison.Ordinal);
        Assert.Contains("  - \"10.0.0.238/32\"", added, StringComparison.Ordinal);
        Assert.Contains("listeners:\n  - port: 8008", added, StringComparison.Ordinal);

        var removed = Encoding.UTF8.GetString(editor.Render(
            Encoding.UTF8.GetBytes(added),
            "10.0.0.238/32",
            enabled: false));
        Assert.Contains("  - \"10.0.0.10/32\"", removed, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.238/32", removed, StringComparison.Ordinal);
    }

    private static async Task<SynapsePrivateNetworkConfigReadResult> ReadAsync(string yaml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-private-network-{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllTextAsync(path, yaml);
            return await new SynapsePrivateNetworkConfigReader().ReadAsync(path, CancellationToken.None);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
