using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks.Runtime;

namespace Api.IntegrationTests.Setup;

public sealed class CommandRunnerTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01C_timeout_kills_process_tree_and_returns_bounded_result()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var runner = new CommandRunner(NullLogger<CommandRunner>.Instance);

        var result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "sleep 5"],
            TimeSpan.FromMilliseconds(100),
            CancellationToken.None);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.Contains("timed out", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01C_stdout_and_stderr_are_bounded()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var runner = new CommandRunner(NullLogger<CommandRunner>.Instance);

        var result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "i=0; while [ $i -lt 8000 ]; do echo '01234567890123456789'; echo 'err-01234567890123456789' >&2; i=$((i+1)); done"],
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.OutputTruncated);
        Assert.Contains("output truncated", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("output truncated", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.StandardOutput.Length < 70_000);
        Assert.True(result.StandardError.Length < 70_000);
    }
}
