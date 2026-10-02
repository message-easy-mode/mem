using Mem.Cli.Config;
using Mem.Cli.Output;

namespace Mem.Cli.Commands;

/// <summary>
/// Reserved boundary for future MEM CLI authentication work. This command is
/// intentionally transport-free until named device login and SEC-AUTH-07A's
/// local recovery bridge have their server-side contracts.
/// </summary>
public static class AuthCommands
{
    public static Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        CliOutput output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);

        if (subcommand == "arm-recovery")
        {
            if (ContainsRemoteAuthorityOption(args))
            {
                if (options.Json)
                {
                    output.WriteJson(new
                    {
                        source = "mem-cli",
                        status = "error",
                        error = "cli_recovery_arm_remote_options_rejected"
                    });
                }
                else
                {
                    output.WriteHumanErrorLine(
                        "sudo mem auth arm-recovery is a future host-local recovery command and does not accept --server, --host-agent-url, or --profile.");
                    output.WriteHumanErrorLine(
                        "No control-plane network request was made.");
                }

                return Task.FromResult(1);
            }

            if (options.Json)
            {
                output.WriteJson(new
                {
                    source = "mem-cli",
                    status = "not_available",
                    error = "cli_recovery_arm_not_available"
                });
            }
            else
            {
                output.WriteHumanErrorLine(
                    "sudo mem auth arm-recovery is reserved for the future SEC-AUTH-07A local recovery bridge.");
                output.WriteHumanErrorLine(
                    "It is local-host-only, uses no remote server URL, and prints no recovery grant.");
                output.WriteHumanErrorLine(
                    "No control-plane network request was made.");
            }

            return Task.FromResult(1);
        }

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "not_available",
                error = "cli_auth_not_available"
            });
        }
        else
        {
            output.WriteHumanErrorLine(
                "MEM CLI authentication commands are not available in this build.");
            output.WriteHumanErrorLine(
                "Normal named-device login uses mem login --device. Host-local recovery arming requires the future SEC-AUTH-07A local bridge.");
            output.WriteHumanErrorLine(
                "No control-plane network request was made.");
        }

        return Task.FromResult(1);
    }

    private static bool ContainsRemoteAuthorityOption(string[] args) =>
        ContainsOption(args, "--server") ||
        ContainsOption(args, "--host-agent-url") ||
        ContainsOption(args, "--profile");

    private static bool ContainsOption(
        string[] args,
        string option) =>
        args.Any(argument => string.Equals(
            argument,
            option,
            StringComparison.OrdinalIgnoreCase));
}
