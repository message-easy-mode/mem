using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class MemCliHostCommandInstallerTests
{
    [Fact]
    public async Task CLI_HOST_01B_skips_host_command_install_when_release_payload_is_not_enabled()
    {
        var runner = new RecordingCommandRunner();
        var installer = CreateInstaller(
            runner,
            new MemCliHostCommandOptions { Enabled = false });

        var result = await installer.InstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("not enabled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task CLI_HOST_01B_invokes_the_cli_owned_installer_with_the_staged_binary_and_host_paths()
    {
        var runner = new RecordingCommandRunner();
        var installer = CreateInstaller(
            runner,
            new MemCliHostCommandOptions
            {
                Enabled = true,
                InstallScriptPath = "/opt/mem/bootstrap/cli/install-host-command.sh",
                BinaryPath = "/opt/mem/bootstrap/cli/mem",
                Version = "0.1.1",
                InstallRoot = "/opt/mem/cli",
                LinkPath = "/usr/local/bin/mem"
            });

        var result = await installer.InstallAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("/opt/mem/bootstrap/cli/install-host-command.sh", call.FileName);
        Assert.Equal(
            new[]
            {
                "--binary", "/opt/mem/bootstrap/cli/mem",
                "--version", "0.1.1",
                "--install-root", "/opt/mem/cli",
                "--link-path", "/usr/local/bin/mem"
            },
            call.Arguments);
    }

    [Fact]
    public async Task CLI_HOST_01B_rejects_unsafe_or_relative_host_command_payload_configuration()
    {
        var runner = new RecordingCommandRunner();
        var installer = CreateInstaller(
            runner,
            new MemCliHostCommandOptions
            {
                Enabled = true,
                InstallScriptPath = "relative/install-host-command.sh",
                BinaryPath = "/opt/mem/bootstrap/cli/mem",
                Version = "bad version with spaces",
                InstallRoot = "/opt/mem/cli",
                LinkPath = "usr/local/bin/mem"
            });

        var result = await installer.InstallAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("InstallScriptPath must be an absolute path", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("LinkPath must be an absolute path", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("Version must be", result.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public void CLI_HOST_01B_adds_the_host_command_step_before_docker_or_container_mutation()
    {
        var steps = InstallStepNames.InitialSteps.ToList();

        Assert.Contains(InstallStepNames.InstallMemCliHostCommand, steps);
        Assert.True(
            steps.IndexOf(InstallStepNames.InstallMemCliHostCommand) <
            steps.IndexOf(InstallStepNames.CreateOrVerifyDockerNetwork));
        Assert.True(
            steps.IndexOf(InstallStepNames.InstallMemCliHostCommand) <
            steps.IndexOf(InstallStepNames.StartPostgres));
    }

    private static MemCliHostCommandInstaller CreateInstaller(
        RecordingCommandRunner runner,
        MemCliHostCommandOptions options)
    {
        return new MemCliHostCommandInstaller(
            runner,
            Options.Create(options),
            NullLogger<MemCliHostCommandInstaller>.Instance);
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public List<CommandCall> Calls { get; } = [];

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new CommandCall(fileName, arguments.ToArray()));

            return Task.FromResult(new CommandResult(
                ExitCode: 0,
                StandardOutput: "installed",
                StandardError: ""));
        }
    }

    private sealed record CommandCall(
        string FileName,
        IReadOnlyList<string> Arguments);
}
