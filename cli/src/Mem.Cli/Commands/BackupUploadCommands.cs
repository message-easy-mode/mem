using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Commands;

/// <summary>
/// Uploaded ZIP provenance commands. validationId identifies only the retained
/// upload/archive record; restore work must use the catalog entry returned by
/// `mem backups import`.
/// </summary>
public static class BackupUploadCommands
{
    public static async Task<int> RunAsync(
        string[] args,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var output = CliOutput.Create(options);
        var action = GetPositional(args, 2);

        if (string.Equals(action, "inspect", StringComparison.OrdinalIgnoreCase))
        {
            var validationId = GetPositional(args, 3);

            if (string.IsNullOrWhiteSpace(validationId))
            {
                PrintInspectUsage(output);
                return 1;
            }

            return await InspectAsync(
                validationId,
                options,
                hostAgentClient,
                output);
        }

        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            var validationId = GetPositional(args, 3);

            if (string.IsNullOrWhiteSpace(validationId))
            {
                PrintDeleteUsage(output);
                return 1;
            }

            if (!HasFlag(args, "--yes"))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorRefuseUploadDeleteWithoutYes);
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorUploadDeleteImpact);
                output.WriteHumanErrorLine();
                PrintDeleteUsage(output);
                return 1;
            }

            return await DeleteAsync(
                validationId,
                options,
                hostAgentClient,
                output);
        }

        PrintUsage(output);
        return 1;
    }

    private static async Task<int> InspectAsync(
        string validationId,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.GetUploadedZipArchiveAsync(
            validationId);

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupReadPresenter.WriteUploadedZipProvenance(output, result);
        }

        return string.Equals(result.Status, "ok", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static async Task<int> DeleteAsync(
        string validationId,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.DeleteUploadedZipArchiveAsync(
            validationId);

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupMutationPresenter.WriteUploadedZipArchiveDelete(
                output,
                result);
        }

        return string.Equals(result.Status, "deleted", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 2;
    }

    private static void PrintUsage(
        CliOutput output)
    {
        output.WriteLocalizedErrorLine(
            CliMessageKeys.BackupErrorUnknownUploadsCommand);
        output.WriteHumanErrorLine();
        output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
        output.WriteHumanErrorLine("  mem backups uploads inspect <validation-id> [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups uploads delete <validation-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintInspectUsage(
        CliOutput output)
    {
        output.WriteLocalizedErrorLine(
            CliMessageKeys.BackupErrorMissingValidationReference);
        output.WriteHumanErrorLine();
        output.WriteLocalizedErrorLine(
            CliMessageKeys.HelpUsage);
        output.WriteHumanErrorLine(
            "  mem backups uploads inspect <validation-id> [--json] [--profile <name>] [--server <url>]");
    }

    private static void PrintDeleteUsage(
        CliOutput output)
    {
        output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
        output.WriteHumanErrorLine("  mem backups uploads delete <validation-id> --yes [--json] [--profile <name>] [--server <url>]");
    }

    private static string? GetPositional(
        string[] args,
        int index)
    {
        return args.Length > index && !args[index].StartsWith("--", StringComparison.Ordinal)
            ? args[index]
            : null;
    }

    private static bool HasFlag(
        string[] args,
        string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }
}
