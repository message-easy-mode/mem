using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Commands;

public static class BackupCommands
{
    public static async Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        HostAgentClient hostAgentClient)
    {
        var output = CliOutput.Create(options);

        if (subcommand == "list")
        {
            if (!string.IsNullOrWhiteSpace(CliOptions.GetOptionValue(args, "--stack")))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorListDoesNotSupportStack);
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorListUseInspect);
                return 1;
            }

            return await ListCatalogAsync(
                options,
                hostAgentClient,
                output);
        }

        if (subcommand == "inspect")
        {
            var catalogEntryId = GetPositional(
                args,
                2);

            var obsoleteBackupId = GetPositional(
                args,
                3);

            if (string.IsNullOrWhiteSpace(catalogEntryId))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorMissingCatalogEntryId);
                output.WriteHumanErrorLine();
                output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
                output.WriteHumanErrorLine("  mem backups inspect <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(obsoleteBackupId))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorInspectRequiresCatalogEntry);
                output.WriteHumanErrorLine("mem backups inspect <catalog-entry-id>");
                return 1;
            }

            return await InspectCatalogAsync(
                catalogEntryId,
                options,
                hostAgentClient,
                output);
        }

        if (subcommand == "lifecycle")
        {
            var catalogEntryId = GetPositional(
                args,
                2);

            var extraArgument = GetPositional(
                args,
                3);

            if (string.IsNullOrWhiteSpace(catalogEntryId))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorMissingCatalogEntryId);
                output.WriteHumanErrorLine();
                output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
                output.WriteHumanErrorLine("  mem backups lifecycle <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(extraArgument))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorLifecycleRequiresCatalogEntry);
                output.WriteHumanErrorLine("mem backups lifecycle <catalog-entry-id>");
                return 1;
            }

            return await GetCatalogLifecycleAsync(
                catalogEntryId,
                options,
                hostAgentClient,
                output);
        }

        if (subcommand == "delete")
        {
            var catalogEntryId = GetPositional(
                args,
                2);

            var obsoleteBackupId = GetPositional(
                args,
                3);

            if (string.IsNullOrWhiteSpace(catalogEntryId))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorMissingCatalogEntryId);
                output.WriteHumanErrorLine();
                output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
                output.WriteHumanErrorLine("  mem backups delete <catalog-entry-id> --yes [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(obsoleteBackupId))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorDeleteRequiresCatalogEntry);
                return 1;
            }

            if (!HasFlag(args, "--yes"))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorRefusePermanentDeleteWithoutYes);
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorPermanentDeleteImpact);
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorPermanentDeleteActiveRestore);
                output.WriteHumanErrorLine();
                output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
                output.WriteHumanErrorLine("  mem backups delete <catalog-entry-id> --yes [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            return await DeleteCatalogAsync(
                catalogEntryId,
                options,
                hostAgentClient,
                output);
        }

        if (subcommand == "export")
        {
            var catalogEntryId = GetPositional(
                args,
                2);

            var obsoleteBackupId = GetPositional(
                args,
                3);

            var outputPath = CliOptions.GetOptionValue(
                args,
                "--out");

            if (string.IsNullOrWhiteSpace(catalogEntryId) ||
                string.IsNullOrWhiteSpace(outputPath))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorMissingCatalogEntryOrOutputPath);
                output.WriteHumanErrorLine();
                output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
                output.WriteHumanErrorLine("  mem backups export <catalog-entry-id> --out <path> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(obsoleteBackupId))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorExportRequiresCatalogEntry);
                return 1;
            }

            return await ExportCatalogAsync(
                catalogEntryId,
                outputPath,
                options,
                hostAgentClient,
                output);
        }

        if (subcommand == "import")
        {
            var zipPath = GetPositional(
                args,
                2);

            if (string.IsNullOrWhiteSpace(zipPath))
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.BackupErrorMissingPortableZipPath);
                output.WriteHumanErrorLine();
                output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
                output.WriteHumanErrorLine("  mem backups import <zip> [--json] [--profile <name>] [--server <url>]");
                return 1;
            }

            return await ImportAsync(
                zipPath,
                options,
                hostAgentClient,
                output);
        }

        if (subcommand == "uploads")
        {
            return await BackupUploadCommands.RunAsync(
                args,
                options,
                hostAgentClient);
        }

        PrintUsage(output);
        return 1;
    }

    private static async Task<int> ListCatalogAsync(
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.GetBackupCatalogAsync();

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupReadPresenter.WriteCatalogList(output, result);
        }

        return result.Status is "ok" ? 0 : 2;
    }

    private static async Task<int> InspectCatalogAsync(
        string catalogEntryId,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.GetBackupCatalogDetailAsync(
            catalogEntryId);

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupReadPresenter.WriteCatalogEntry(output, result);
        }

        return result.Status is "ok" ? 0 : 2;
    }


    private static async Task<int> GetCatalogLifecycleAsync(
        string catalogEntryId,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.GetBackupCatalogLifecycleAsync(
            catalogEntryId);

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupReadPresenter.WriteCatalogLifecycle(output, result);
        }

        return result.Status is "ok" ? 0 : 2;
    }

    private static async Task<int> DeleteCatalogAsync(
        string catalogEntryId,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var lifecycle = await hostAgentClient.GetBackupCatalogLifecycleAsync(
            catalogEntryId);

        BackupCatalogPermanentDeleteCliResult result;

        if (lifecycle.Status is not "ok" || lifecycle.Lifecycle is null)
        {
            result = new BackupCatalogPermanentDeleteCliResult(
                Source: lifecycle.Source,
                Status: lifecycle.Status,
                CatalogEntryId: catalogEntryId,
                OriginKind: null,
                DeletedBy: null,
                PayloadDeleted: false,
                OriginalArchiveDeleted: false,
                PortableExportsDeleted: 0,
                DetachedRestoreAttempts: 0,
                ActiveRestoreSessionId: null,
                Detail: lifecycle.Detail ?? "Backup Catalog lifecycle information was not available.");
        }
        else if (!lifecycle.Lifecycle.CanDelete)
        {
            result = new BackupCatalogPermanentDeleteCliResult(
                Source: lifecycle.Source,
                Status: "blocked",
                CatalogEntryId: lifecycle.Lifecycle.CatalogEntryId,
                OriginKind: null,
                DeletedBy: null,
                PayloadDeleted: false,
                OriginalArchiveDeleted: false,
                PortableExportsDeleted: 0,
                DetachedRestoreAttempts: 0,
                ActiveRestoreSessionId: lifecycle.Lifecycle.ActiveRestoreSessionId,
                Detail: lifecycle.Lifecycle.DeleteBlockReason ??
                        "An active restore workspace blocks permanent deletion.");
        }
        else
        {
            result = await hostAgentClient.DeleteBackupCatalogAsync(
                catalogEntryId);
        }

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupMutationPresenter.WriteCatalogPermanentDelete(
                output,
                result);
        }

        return result.Status is "deleted" ? 0 : 2;
    }

    private static async Task<int> ExportCatalogAsync(
        string catalogEntryId,
        string outputPath,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var export = await hostAgentClient.CreateBackupCatalogPortableExportAsync(
            catalogEntryId);

        if (export.Status is not "created" ||
            string.IsNullOrWhiteSpace(export.ExportId))
        {
            return WriteCatalogExportResult(
                new BackupCatalogPortableExportCliResult(
                    Source: export.Source,
                    Status: export.Status,
                    CatalogEntryId: export.CatalogEntryId,
                    OriginKind: export.OriginKind,
                    SourceStackSlug: export.SourceStackSlug,
                    ExportId: export.ExportId,
                    DownloadName: export.DownloadName,
                    OutputPath: null,
                    SizeBytes: export.SizeBytes,
                    BytesWritten: 0,
                    Warnings: export.Warnings,
                    Detail: export.Detail),
                options,
                output);
        }

        var download = await hostAgentClient.DownloadPortableExportAsync(
            export.ExportId);

        if (download.Status is not "downloaded")
        {
            return WriteCatalogExportResult(
                new BackupCatalogPortableExportCliResult(
                    Source: download.Source,
                    Status: download.Status,
                    CatalogEntryId: export.CatalogEntryId,
                    OriginKind: export.OriginKind,
                    SourceStackSlug: export.SourceStackSlug,
                    ExportId: export.ExportId,
                    DownloadName: export.DownloadName,
                    OutputPath: null,
                    SizeBytes: export.SizeBytes,
                    BytesWritten: 0,
                    Warnings: export.Warnings,
                    Detail: download.Detail),
                options,
                output);
        }

        var fullOutputPath = Path.GetFullPath(
            outputPath);

        try
        {
            var outputDirectory = Path.GetDirectoryName(
                fullOutputPath);

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await WritePrivatePortableExportAsync(
                fullOutputPath,
                download.Bytes);
        }
        catch (Exception)
        {
            return WriteCatalogExportResult(
                new BackupCatalogPortableExportCliResult(
                    Source: "cli",
                    Status: "write-failed",
                    CatalogEntryId: export.CatalogEntryId,
                    OriginKind: export.OriginKind,
                    SourceStackSlug: export.SourceStackSlug,
                    ExportId: export.ExportId,
                    DownloadName: download.DownloadName ?? export.DownloadName,
                    OutputPath: fullOutputPath,
                    SizeBytes: export.SizeBytes,
                    BytesWritten: 0,
                    Warnings: export.Warnings,
                    Detail: "The portable export could not be written to the requested output path."),
                options,
                output);
        }

        return WriteCatalogExportResult(
            new BackupCatalogPortableExportCliResult(
                Source: export.Source,
                Status: export.Status,
                CatalogEntryId: export.CatalogEntryId,
                OriginKind: export.OriginKind,
                SourceStackSlug: export.SourceStackSlug,
                ExportId: export.ExportId,
                DownloadName: download.DownloadName ?? export.DownloadName,
                OutputPath: fullOutputPath,
                SizeBytes: export.SizeBytes,
                BytesWritten: download.Bytes.LongLength,
                Warnings: export.Warnings,
                Detail: export.Detail),
            options,
            output);
    }

    private static async Task WritePrivatePortableExportAsync(
        string fullOutputPath,
        byte[] bytes)
    {
        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllBytesAsync(
                fullOutputPath,
                bytes);
            return;
        }

        var outputDirectory = Path.GetDirectoryName(
            fullOutputPath);

        var temporaryPath = Path.Combine(
            string.IsNullOrWhiteSpace(outputDirectory)
                ? Directory.GetCurrentDirectory()
                : outputDirectory,
            $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                UnixCreateMode =
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite
            };

            await using (var stream = new FileStream(
                             temporaryPath,
                             options))
            {
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }

            File.Move(
                temporaryPath,
                fullOutputPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static int WriteCatalogExportResult(
        BackupCatalogPortableExportCliResult result,
        CliOptions options,
        CliOutput output)
    {
        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupMutationPresenter.WriteCatalogExport(
                output,
                result);
        }

        return result.Status is "created" ? 0 : 2;
    }

    private static void PrintUsage(
        CliOutput output)
    {
        output.WriteLocalizedErrorLine(
            CliMessageKeys.BackupErrorUnknownCommand);
        output.WriteHumanErrorLine();
        output.WriteLocalizedErrorLine(CliMessageKeys.HelpUsage);
        output.WriteHumanErrorLine("  mem backups list [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups inspect <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups lifecycle <catalog-entry-id> [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups export <catalog-entry-id> --out <path> [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups delete <catalog-entry-id> --yes [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups import <zip> [--json] [--profile <name>] [--server <url>]");
        output.WriteHumanErrorLine("  mem backups uploads inspect <validation-id> [--json] [--profile <name>] [--server <url>]");
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

    private static async Task<int> ImportAsync(
        string zipPath,
        CliOptions options,
        HostAgentClient hostAgentClient,
        CliOutput output)
    {
        var result = await hostAgentClient.ImportPortableZipAsync(
            zipPath);

        if (options.Json)
        {
            output.WriteJson(result);
        }
        else
        {
            BackupMutationPresenter.WriteImport(
                output,
                result);
        }

        return IsImportReady(result) ? 0 : 2;
    }

    private static bool IsImportReady(
        BackupCatalogImportCliResult result)
    {
        return string.Equals(result.Status, "valid", StringComparison.OrdinalIgnoreCase) &&
               HasCatalogEntry(result);
    }

    private static bool HasCatalogEntry(
        BackupCatalogImportCliResult result)
    {
        return !string.IsNullOrWhiteSpace(result.CatalogEntryId);
    }

    private static bool HasFlag(
    string[] args,
    string flag)
    {
        return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
    }

}