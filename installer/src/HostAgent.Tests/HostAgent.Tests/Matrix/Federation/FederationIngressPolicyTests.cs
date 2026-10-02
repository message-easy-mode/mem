using HostAgent.Matrix.Federation;
using HostAgent.Runtime.Ingress;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class FederationIngressPolicyTests
{
    [Fact]
    public void Recognises_the_existing_normal_matrix_template()
    {
        Assert.Equal(
            FederationIngressModes.Normal,
            FederationIngressPolicy.Classify(IngressAdvancedConfigTemplates.MatrixWellKnown()));
    }

    [Fact]
    public void Recognises_the_canonical_local_only_template_after_line_ending_normalisation()
    {
        var config = IngressAdvancedConfigTemplates.MatrixLocalOnly()
            .Replace("\n", "\r\n", StringComparison.Ordinal);

        Assert.Equal(
            FederationIngressModes.LocalOnly,
            FederationIngressPolicy.Classify(config));
    }

    [Fact]
    public void Refuses_custom_advanced_configuration()
    {
        Assert.Equal(
            FederationIngressModes.CustomUnsupported,
            FederationIngressPolicy.Classify("location / { proxy_pass http://matrix:8008; }"));
    }

    [Fact]
    public void Local_only_keeps_client_discovery_and_blocks_server_federation_and_key_paths()
    {
        var config = IngressAdvancedConfigTemplates.MatrixLocalOnly();

        Assert.Contains("/.well-known/matrix/client", config, StringComparison.Ordinal);
        Assert.Contains("/.well-known/matrix/server", config, StringComparison.Ordinal);
        Assert.Contains("/_matrix/federation/", config, StringComparison.Ordinal);
        Assert.Contains("/_matrix/key/", config, StringComparison.Ordinal);
        Assert.Equal(3, config.Split("return 404;", StringSplitOptions.None).Length - 1);
    }
}
