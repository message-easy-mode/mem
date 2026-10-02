using System.Text;
using HostAgent.Matrix.Federation;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class SynapseFederationConfigEditorTests
{
    private readonly SynapseFederationConfigEditor _editor = new();

    [Fact]
    public void Appends_a_deterministic_restricted_block_without_reserialising_unrelated_yaml()
    {
        var before = Encoding.UTF8.GetBytes(
            "server_name: matrix.example.test\n# retained comment\nlisteners: []\n");

        var result = Encoding.UTF8.GetString(_editor.Render(
            before,
            new CanonicalFederationPolicyRequest(
                FederationModes.Restricted,
                ["matrix.example.org", "partner.example"])));

        Assert.Equal(
            "server_name: matrix.example.test\n# retained comment\nlisteners: []\n\n" +
            "federation_domain_whitelist:\n" +
            "  - matrix.example.org\n" +
            "  - partner.example\n",
            result);
    }

    [Fact]
    public void Replaces_only_the_supported_allowlist_lines_and_preserves_comments()
    {
        var before = Encoding.UTF8.GetBytes(
            "server_name: matrix.example.test\r\n" +
            "federation_domain_whitelist:\r\n" +
            "  # partner note\r\n" +
            "  - old.example\r\n" +
            "listeners: []\r\n");

        var result = Encoding.UTF8.GetString(_editor.Render(
            before,
            new CanonicalFederationPolicyRequest(
                FederationModes.Restricted,
                ["new.example"])));

        Assert.Equal(
            "server_name: matrix.example.test\r\n" +
            "federation_domain_whitelist:\r\n" +
            "  - new.example\r\n" +
            "  # partner note\r\n" +
            "listeners: []\r\n",
            result);
    }

    [Fact]
    public void Public_removes_the_supported_key_and_items_without_writing_a_wildcard()
    {
        var before = Encoding.UTF8.GetBytes(
            "server_name: matrix.example.test\n" +
            "federation_domain_whitelist:\n" +
            "  # retained note\n" +
            "  - partner.example\n" +
            "listeners: []\n");

        var result = Encoding.UTF8.GetString(_editor.Render(
            before,
            new CanonicalFederationPolicyRequest(FederationModes.Public, [])));

        Assert.Equal(
            "server_name: matrix.example.test\n" +
            "  # retained note\n" +
            "listeners: []\n",
            result);
        Assert.DoesNotContain("*", result, StringComparison.Ordinal);
        Assert.DoesNotContain("federation_domain_whitelist", result, StringComparison.Ordinal);
    }
    [Fact]
    public void Local_only_writes_the_canonical_inline_empty_allowlist()
    {
        var before = Encoding.UTF8.GetBytes(
            "server_name: matrix.example.test\n" +
            "federation_domain_whitelist:\n" +
            "  - partner.example\n" +
            "listeners: []\n");

        var result = Encoding.UTF8.GetString(_editor.Render(
            before,
            new CanonicalFederationPolicyRequest(FederationModes.LocalOnly, [])));

        Assert.Equal(
            "server_name: matrix.example.test\n" +
            "federation_domain_whitelist: []\n" +
            "listeners: []\n",
            result);
    }

}
