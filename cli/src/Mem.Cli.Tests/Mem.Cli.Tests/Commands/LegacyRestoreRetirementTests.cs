using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class LegacyRestoreRetirementTests
{
    [Theory]
    [InlineData("restore" + "-plan")]
    [InlineData("restore" + "-lab")]
    [InlineData("restore" + "-staging")]
    public async Task Legacy_validation_first_backup_commands_are_not_registered_and_do_not_call_the_control_plane(
        string subcommand)
    {
        var handler = new RecordingHttpMessageHandler();
        var options = new CliOptions(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);

        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            BackupCommands.RunAsync(
                ["backups", subcommand, "legacy-validation-id"],
                subcommand,
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("Unknown backups command.", captured.StandardError);
        Assert.Empty(handler.Requests);
    }
}
