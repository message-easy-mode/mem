using Mem.Localization;

namespace Mem.Cli.Output;

public static class CliHelp
{
    private static readonly string[] CommandUsageLines =
    {
        "  mem config get language [--json]",
        "  mem config set language <en|de> [--json]",
        "  mem config get default-profile [--json]",
        "  mem config set default-profile <name> [--json]",
        string.Empty,
        "  mem profile create <name> --server <url> [--language <en|de>] [--json]",
        "  mem profile list [--json]",
        "  mem profile select <name> [--json]",
        "  mem profile remove <name> [--json]",
        string.Empty,
        "  mem login --device [--profile <name>] [--server <url>]",
        "  mem account show [--json] [--profile <name>] [--server <url>]",
        "  mem logout [--json] [--profile <name>] [--server <url>]",
        string.Empty,
        "  mem host status [--json] [--profile <name>] [--server <url>]",
        "  mem maintenance reconciliation [--json] [--profile <name>] [--server <url>]",
        "  mem maintenance cleanup npm-hosts --proxy-host-id <id> [--proxy-host-id <id>] --confirm "DELETE ORPHANED NPM HOSTS" [--json] [--profile <name>] [--server <url>]",
        string.Empty,
        "  mem stack list [--json] [--profile <name>] [--server <url>]",
        "  mem stack inspect <slug-or-id> [--json] [--profile <name>] [--server <url>]",
        "  mem stack doctor <slug-or-id> [--json] [--profile <name>] [--server <url>]",
        "  mem stack operations <slug-or-id> [--json] [--profile <name>] [--server <url>]",
        string.Empty,
        "  mem backups list [--json] [--profile <name>] [--server <url>]",
        "  mem backups inspect <catalog-entry-id> [--json] [--profile <name>] [--server <url>]",
        "  mem backups lifecycle <catalog-entry-id> [--json] [--profile <name>] [--server <url>]",
        "  mem backups export <catalog-entry-id> --out <path> [--json] [--profile <name>] [--server <url>]",
        "  mem backups delete <catalog-entry-id> --yes [--json] [--profile <name>] [--server <url>]",
        "  mem backups import <zip> [--json] [--profile <name>] [--server <url>]",
        "  mem backups uploads inspect <validation-id> [--json] [--profile <name>] [--server <url>]",
        "  mem backups uploads delete <validation-id> --yes [--json] [--profile <name>] [--server <url>]",
        string.Empty,
        "  mem restores list [--page <n>] [--page-size <n>] [--search <text>] [--status <status>] [--target-stack <slug>] [--sort-by <field>] [--sort-direction asc|desc] [--json] [--profile <name>] [--server <url>]",
        "  mem restores inspect <restore-session-id> [--json] [--profile <name>] [--server <url>]",
        "  mem restores evidence <restore-session-id> [--json] [--profile <name>] [--server <url>]",
        "  mem restores logs <restore-session-id> [--page <n>] [--page-size <n>] [--severity <severity>] [--stage <stage>] [--search <text>] [--json] [--profile <name>] [--server <url>]",
        "  mem restores support-report <restore-session-id> [--json] [--profile <name>] [--server <url>]",
        "  mem restores create <catalog-entry-id> [--json] [--profile <name>] [--server <url>]",
        "  mem restores private-test <restore-session-id> [--json] [--profile <name>] [--server <url>]",
        "  mem restores private-test destroy <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]",
        "  mem restores recreate preflight <restore-session-id> --target-stack <slug> --element-host <host> [--matrix-host <host>] [--requested-domain-id <id>] [--json] [--profile <name>] [--server <url>]",
        "  mem restores recreate execute <restore-session-id> --target-stack <slug> --element-host <host> --execute-production-recreate --acknowledge-creates-real-stack --acknowledge-mutates-production-postgres --acknowledge-mutates-npm-routes --acknowledge-no-automatic-rollback [--json] [--profile <name>] [--server <url>]",
        "  mem restores cancel <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]",
        "  mem restores handover complete <restore-session-id> --yes [--json] [--profile <name>] [--server <url>]"
    };

    public static void Write(
        TextWriter output,
        IMemLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(localizer);

        output.WriteLine(localizer.Get(CliMessageKeys.HelpTitle));
        output.WriteLine();
        output.WriteLine(localizer.Get(CliMessageKeys.HelpUsage));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpAuthorityLine1));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpAuthorityLine2));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpStableCommandNote));
        output.WriteLine();
        output.WriteLine(localizer.Get(CliMessageKeys.HelpCommands));

        foreach (var commandUsageLine in CommandUsageLines)
        {
            output.WriteLine(commandUsageLine);
        }

        output.WriteLine();
        output.WriteLine(localizer.Get(CliMessageKeys.HelpOptions));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpOptionProfile));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpOptionServer));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpOptionLanguage));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpOptionJson));
        output.WriteLine();
        output.WriteLine(localizer.Get(CliMessageKeys.HelpEnvironment));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpEnvironmentServerUrl));
        output.WriteLine(localizer.Get(CliMessageKeys.HelpEnvironmentLanguage));

    }
}
