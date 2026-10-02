using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace Modules.Integrations.Seq.Services;

public sealed record SeqBootstrapState(
    int SchemaVersion = 1,
    bool ManagementEnabled = false,
    bool EulaAccepted = false,
    DateTimeOffset? EulaAcceptedAtUtc = null,
    Guid? EulaAcceptedByUserId = null,
    int? SelectedHostPort = null,
    string? PrivateUiUrl = null,
    string? SetupStage = null,
    DateTimeOffset? RuntimeVerifiedAtUtc = null,
    string? IngestionCredentialState = null,
    string? IngestionCredentialId = null,
    DateTimeOffset? DeliveryVerifiedAtUtc = null,
    string? LastDeliveryVerificationId = null,
    string? LastDeliveryVerificationEventId = null,
    Guid? ActiveDeliveryProcessId = null,
    DateTimeOffset? ActiveDeliveryVerifiedAtUtc = null,
    string? LastActiveDeliveryVerificationId = null,
    Guid? LastOperationId = null);

public sealed record SeqBootstrapStateReadResult(
    SeqBootstrapState? State,
    string? WarningCode)
{
    public bool Available => State is not null;
}

public interface ISeqBootstrapStateStore
{
    SeqBootstrapStateReadResult Read();

    Task WriteAsync(
        SeqBootstrapState state,
        CancellationToken cancellationToken);
}

public sealed class SeqBootstrapStateStore(
    SeqDiagnosticsOptions options,
    IHostEnvironment environment) : ISeqBootstrapStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public SeqBootstrapStateReadResult Read()
    {
        string path;
        try
        {
            path = SeqFileSystemSafety.ResolveFile(
                options.BootstrapStatePath,
                environment.ContentRootPath,
                "seq_bootstrap_state_path_invalid");
            SeqFileSystemSafety.EnsurePathContainsNoLinks(
                path,
                "seq_bootstrap_state_path_invalid");
            SeqFileSystemSafety.EnsureTargetIsNotLink(
                path,
                "seq_bootstrap_state_path_invalid");
        }
        catch (SeqOperationException)
        {
            return new SeqBootstrapStateReadResult(
                null,
                "seq_bootstrap_state_path_invalid");
        }

        if (!File.Exists(path))
        {
            return new SeqBootstrapStateReadResult(null, null);
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<SeqBootstrapState>(
                json,
                SerializerOptions);
            if (state is null)
            {
                return new SeqBootstrapStateReadResult(
                    null,
                    "seq_bootstrap_state_invalid");
            }

            try
            {
                ValidateState(state);
            }
            catch (SeqOperationException)
            {
                return new SeqBootstrapStateReadResult(
                    null,
                    "seq_bootstrap_state_invalid");
            }

            return new SeqBootstrapStateReadResult(state, null);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SeqBootstrapStateReadResult(
                null,
                "seq_bootstrap_state_invalid");
        }
    }

    public async Task WriteAsync(
        SeqBootstrapState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);

        var path = SeqFileSystemSafety.ResolveFile(
            options.BootstrapStatePath,
            environment.ContentRootPath,
            "seq_bootstrap_state_path_invalid");
        var parent = Path.GetDirectoryName(path)
            ?? throw new SeqOperationException(
                "seq_bootstrap_state_path_invalid",
                Microsoft.AspNetCore.Http.StatusCodes.Status409Conflict,
                "The Seq bootstrap state path is invalid.");

        SeqFileSystemSafety.EnsureSafeParentDirectory(
            parent,
            path,
            "seq_bootstrap_state_path_invalid");
        SeqFileSystemSafety.EnsureTargetIsNotLink(
            path,
            "seq_bootstrap_state_path_invalid");

        var temporaryPath = $"{path}.tmp-{Guid.NewGuid():N}";
        try
        {
            var json = JsonSerializer.Serialize(state, SerializerOptions);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(json.AsMemory(), cancellationToken);
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
                "seq_bootstrap_state_write_failed",
                Microsoft.AspNetCore.Http.StatusCodes.Status503ServiceUnavailable,
                "MEM could not persist the Seq bootstrap state.");
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }


    private static void ValidateState(SeqBootstrapState state)
    {
        if (state.SchemaVersion != 1 ||
            state.SelectedHostPort is < 1 or > 65535 ||
            (!string.IsNullOrWhiteSpace(state.PrivateUiUrl) &&
             !SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                 state.PrivateUiUrl,
                 out _)))
        {
            throw new SeqOperationException(
                "seq_bootstrap_state_invalid",
                Microsoft.AspNetCore.Http.StatusCodes.Status400BadRequest,
                "The Seq bootstrap state is invalid.");
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
