using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class StandardInputCommandRunnerTests
{
    [Fact]
    public async Task Standard_input_is_forwarded_without_becoming_an_argument()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var runner = new StandardInputCommandRunner(
            NullLogger<StandardInputCommandRunner>.Instance);

        var result = await runner.RunAsync(
            "/bin/cat",
            [],
            "sensitive-input".AsMemory(),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("sensitive-input", result.StandardOutput);
    }

    [Fact]
    public async Task Timeout_terminates_the_process_with_a_safe_result()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var runner = new StandardInputCommandRunner(
            NullLogger<StandardInputCommandRunner>.Instance);

        var result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "sleep 5"],
            "not-logged".AsMemory(),
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.DoesNotContain("not-logged", result.StandardError, StringComparison.Ordinal);
    }
}
