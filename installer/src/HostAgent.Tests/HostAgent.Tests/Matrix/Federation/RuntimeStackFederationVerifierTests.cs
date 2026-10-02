using HostAgent.Matrix.Federation;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class RuntimeStackFederationVerifierTests
{
    [Fact]
    public void PLATFORM_FED_PRIVATE_NET_CORR_01_public_probe_runs_through_npm_with_public_hostname_and_sni()
    {
        const string url =
            "https://matrix-qa-bravo.deltabox.dev/_matrix/federation/v1/version";

        var command = RuntimeStackFederationVerifier.BuildPublicHttpsProbeCommand(url);

        Assert.Equal("mem-npm", RuntimeStackFederationVerifier.NpmContainerName);
        Assert.Equal("curl", command[0]);
        Assert.Contains("-k", command);
        Assert.Contains("--resolve", command);
        Assert.Contains(
            "matrix-qa-bravo.deltabox.dev:443:127.0.0.1",
            command);
        Assert.Equal(url, command[^1]);
    }

    [Fact]
    public void PLATFORM_FED_PRIVATE_NET_CORR_01_public_probe_preserves_an_explicit_https_port()
    {
        const string url =
            "https://matrix.example.test:9443/_matrix/key/v2/server";

        var command = RuntimeStackFederationVerifier.BuildPublicHttpsProbeCommand(url);

        Assert.Contains(
            "matrix.example.test:9443:127.0.0.1",
            command);
        Assert.Equal(url, command[^1]);
    }

    [Theory]
    [InlineData("http://matrix.example.test/_matrix/federation/v1/version")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void PLATFORM_FED_PRIVATE_NET_CORR_01_public_probe_refuses_non_https_or_relative_targets(
        string url)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RuntimeStackFederationVerifier.BuildPublicHttpsProbeCommand(url));

        Assert.Equal("url", exception.ParamName);
    }
}
