using Microsoft.Extensions.Hosting;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqBootstrapStorageInspection(
    string State,
    string? WarningCode);

public sealed class SeqBootstrapStorageInspector(
    SeqDiagnosticsOptions options,
    ISeqBootstrapStateStore stateStore,
    IHostEnvironment environment)
{
    public SeqBootstrapStorageInspection Inspect()
    {
        string path;
        try
        {
            path = SeqFileSystemSafety.ResolveDirectory(
                options.HostDataPath,
                environment.ContentRootPath,
                "seq_data_path_invalid");
            SeqFileSystemSafety.EnsurePathContainsNoLinks(
                path,
                "seq_data_path_symlink");
        }
        catch (SeqOperationException exception)
        {
            return Invalid(exception.Code);
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return new SeqBootstrapStorageInspection(
                    "ready-to-create",
                    null);
            }

            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                return new SeqBootstrapStorageInspection("ready", null);
            }

            var state = stateStore.Read();
            if (state.State?.ManagementEnabled == true)
            {
                return new SeqBootstrapStorageInspection(
                    "initialized",
                    state.WarningCode);
            }

            return new SeqBootstrapStorageInspection(
                "unexplained-data",
                "seq_data_path_unexplained");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("seq_data_path_unavailable");
        }
    }


    public void Prepare()
    {
        try
        {
            var path = SeqFileSystemSafety.ResolveDirectory(
                options.HostDataPath,
                environment.ContentRootPath,
                "seq_data_path_invalid");
            SeqFileSystemSafety.EnsurePathContainsNoLinks(
                path,
                "seq_data_path_symlink");

            if (Directory.Exists(path) &&
                Directory.EnumerateFileSystemEntries(path).Any())
            {
                var state = stateStore.Read();
                if (state.State?.ManagementEnabled != true)
                {
                    throw new SeqOperationException(
                        "seq_data_path_unexplained",
                        Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict,
                        "The configured Seq data directory contains unexplained existing data and will not be initialized.");
                }

                return;
            }

            Directory.CreateDirectory(path);
            SeqFileSystemSafety.EnsurePathContainsNoLinks(
                path,
                "seq_data_path_symlink");
            SeqFileSystemSafety.SetDirectoryMode(path);
        }
        catch (SeqOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new SeqOperationException(
                "seq_data_path_unavailable",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "The configured Seq data directory could not be prepared by the MEM API process.");
        }
    }

    private static SeqBootstrapStorageInspection Invalid(string code) =>
        new("invalid", code);
}
