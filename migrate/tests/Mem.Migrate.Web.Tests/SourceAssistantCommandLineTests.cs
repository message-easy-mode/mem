using System.Reflection;
using System.Net;
using Mem.Migrate.Web.Hosting;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceAssistantCommandLineTests
{
    private static readonly Func<string, string?> EmptyEnvironment = _ => null;

    [Fact]
    public void Version_uses_the_informational_release_identity()
    {
        var result = SourceAssistantCommandLine.Parse(["--version"], EmptyEnvironment);

        Assert.True(result.ShouldExit);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Output);
        Assert.DoesNotContain("unknown", result.Output, StringComparison.OrdinalIgnoreCase);

        var expected = typeof(SourceAssistantCommandLine).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Assert.Equal(expected, result.Output);
        Assert.Equal(expected, SourceAssistantCommandLine.GetApplicationVersion());
    }

    [Fact]
    public void Defaults_to_loopback_and_private_roots()
    {
        var result = SourceAssistantCommandLine.Parse([], EmptyEnvironment);

        Assert.False(result.ShouldExit);
        var options = Assert.IsType<SourceAssistantOptions>(result.Options);
        Assert.Equal(IPAddress.Loopback, options.ListenAddress);
        Assert.Equal(7391, options.Port);
        Assert.True(options.IsLoopback);
        Assert.Equal("/var/lib/mem-migrate/work", options.WorkspaceRoot);
        Assert.Equal("/var/lib/mem-migrate/artifacts", options.ArtifactRoot);
        Assert.Equal(TimeSpan.FromMinutes(240), options.SessionIdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(1440), options.SessionAbsoluteTimeout);
    }


    [Fact]
    public void Session_absolute_lifetime_must_not_be_shorter_than_idle_timeout()
    {
        var result = SourceAssistantCommandLine.Parse(
            ["--session-minutes", "120", "--session-absolute-minutes", "60"],
            EmptyEnvironment);

        Assert.True(result.ShouldExit);
        Assert.Contains(
            "greater than or equal",
            Assert.IsType<string>(result.Error),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Session_lifetimes_can_be_overridden_explicitly()
    {
        var result = SourceAssistantCommandLine.Parse(
            ["--session-minutes", "90", "--session-absolute-minutes", "360"],
            EmptyEnvironment);

        Assert.False(result.ShouldExit);
        var options = Assert.IsType<SourceAssistantOptions>(result.Options);
        Assert.Equal(TimeSpan.FromMinutes(90), options.SessionIdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(360), options.SessionAbsoluteTimeout);
    }

    [Fact]
    public void Remote_bind_requires_explicit_acknowledgement()
    {
        var blocked = SourceAssistantCommandLine.Parse(
            ["--listen-address", "192.168.1.20"],
            EmptyEnvironment);

        Assert.True(blocked.ShouldExit);
        Assert.Equal(2, blocked.ExitCode);
        Assert.Contains("--allow-remote", Assert.IsType<string>(blocked.Error), StringComparison.Ordinal);

        var allowed = SourceAssistantCommandLine.Parse(
            ["--listen-address", "192.168.1.20", "--allow-remote"],
            EmptyEnvironment);

        Assert.False(allowed.ShouldExit);
        var options = Assert.IsType<SourceAssistantOptions>(allowed.Options);
        Assert.True(options.AllowRemote);
        Assert.False(options.IsLoopback);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Wildcard_bind_is_always_rejected(string address)
    {
        var result = SourceAssistantCommandLine.Parse(
            ["--listen-address", address, "--allow-remote"],
            EmptyEnvironment);

        Assert.True(result.ShouldExit);
        Assert.Contains("Wildcard", Assert.IsType<string>(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_and_artifact_roots_must_not_be_nested()
    {
        var result = SourceAssistantCommandLine.Parse(
            ["--workspace", "/srv/mem/work", "--artifacts", "/srv/mem/work/artifacts"],
            EmptyEnvironment);

        Assert.True(result.ShouldExit);
        Assert.Contains("must not contain", Assert.IsType<string>(result.Error), StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_and_artifact_roots_must_be_distinct()
    {
        var result = SourceAssistantCommandLine.Parse(
            ["--workspace", "/srv/mem", "--artifacts", "/srv/mem"],
            EmptyEnvironment);

        Assert.True(result.ShouldExit);
        Assert.Contains("different directories", Assert.IsType<string>(result.Error), StringComparison.Ordinal);
    }
}
