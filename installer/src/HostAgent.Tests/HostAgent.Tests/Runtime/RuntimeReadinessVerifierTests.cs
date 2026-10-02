using HostAgent.Runtime.Readiness;

namespace HostAgent.Tests.Runtime;

public sealed class RuntimeReadinessVerifierTests
{
    [Fact]
    public void STACK_CREATE_REL_01D_public_probe_runs_through_npm_with_public_hostname_and_sni()
    {
        const string url =
            "https://matrix-demo-stack-5.deltabox.dev/_matrix/client/versions";

        var command = RuntimeReadinessVerifier.BuildPublicHttpsProbeCommand(url);

        Assert.Equal("mem-npm", RuntimeReadinessVerifier.NpmContainerName);
        Assert.Equal("curl", command[0]);
        Assert.Contains("-k", command);
        Assert.Contains("--resolve", command);
        Assert.Contains(
            "matrix-demo-stack-5.deltabox.dev:443:127.0.0.1",
            command);
        Assert.Equal(url, command[^1]);
    }

    [Fact]
    public void STACK_CREATE_REL_01D_public_probe_preserves_an_explicit_https_port()
    {
        const string url =
            "https://chat.example.test:9443/";

        var command = RuntimeReadinessVerifier.BuildPublicHttpsProbeCommand(url);

        Assert.Contains(
            "chat.example.test:9443:127.0.0.1",
            command);
        Assert.Equal(url, command[^1]);
    }

    [Theory]
    [InlineData("http://chat.example.test/")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void STACK_CREATE_REL_01D_public_probe_refuses_non_https_or_relative_targets(
        string url)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            RuntimeReadinessVerifier.BuildPublicHttpsProbeCommand(url));

        Assert.Equal("url", exception.ParamName);
    }
}
