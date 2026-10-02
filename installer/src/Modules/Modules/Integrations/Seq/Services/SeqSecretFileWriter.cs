using Microsoft.Extensions.Hosting;

namespace Modules.Integrations.Seq.Services;

public sealed class SeqSecretFileWriter(
    SeqDiagnosticsOptions options,
    IHostEnvironment environment)
{
    public Task WriteAdministratorPasswordHashAsync(
        string value,
        CancellationToken cancellationToken) =>
        WriteAsync(
            options.AdminPasswordHashFilePath,
            value,
            "seq_admin_password_hash_path_invalid",
            cancellationToken);

    public Task WriteIngestionApiKeyAsync(
        string value,
        CancellationToken cancellationToken) =>
        WriteAsync(
            options.ApiKeyFilePath,
            value,
            "seq_api_key_path_invalid",
            cancellationToken);

    private async Task WriteAsync(
        string? configuredPath,
        string value,
        string pathErrorCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Contains('\0') ||
            value.Contains('\r') ||
            value.Contains('\n'))
        {
            throw new SeqOperationException(
                "seq_secret_value_invalid",
                Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest,
                "The generated Seq secret value is invalid.");
        }

        var root = SeqFileSystemSafety.ResolveDirectory(
            options.SecretRootPath,
            environment.ContentRootPath,
            "seq_secret_root_invalid");
        var path = SeqFileSystemSafety.ResolveFile(
            configuredPath ?? string.Empty,
            environment.ContentRootPath,
            pathErrorCode);
        SeqFileSystemSafety.EnsureFileWithinRoot(root, path, pathErrorCode);
        SeqFileSystemSafety.EnsureSafeParentDirectory(root, path, pathErrorCode);
        SeqFileSystemSafety.EnsureTargetIsNotLink(path, pathErrorCode);

        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(value.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            SeqFileSystemSafety.SetSecretFileMode(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            SeqFileSystemSafety.SetSecretFileMode(path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new SeqOperationException(
                "seq_secret_write_failed",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "MEM could not persist the generated Seq secret.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // The primary failure remains authoritative.
        }
    }
}
