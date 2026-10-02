using System.Diagnostics;
using System.Text.Json;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Infrastructure.Target;

public interface ITargetSecretToolProcessRunner
{
    Task<TargetSecretToolProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed record TargetSecretToolProcessResult(
    bool Started,
    bool TimedOut,
    int? ExitCode,
    string StandardOutput);

public sealed class SystemTargetSecretToolProcessRunner :
    ITargetSecretToolProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<TargetSecretToolProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "secret-tool",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new TargetSecretToolProcessResult(
                    Started: false,
                    TimedOut: false,
                    ExitCode: null,
                    StandardOutput: string.Empty);
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeout.CancelAfter(Timeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await DrainAsync(standardOutputTask, standardErrorTask);

                return new TargetSecretToolProcessResult(
                    Started: true,
                    TimedOut: true,
                    ExitCode: null,
                    StandardOutput: string.Empty);
            }

            var standardOutput = await standardOutputTask;
            await standardErrorTask;

            return new TargetSecretToolProcessResult(
                Started: true,
                TimedOut: false,
                ExitCode: process.ExitCode,
                StandardOutput: standardOutput);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new TargetSecretToolProcessResult(
                Started: false,
                TimedOut: false,
                ExitCode: null,
                StandardOutput: string.Empty);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The child process exited while timeout handling was running.
        }
    }

    private static async Task DrainAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask);
        }
        catch (Exception)
        {
            // Secret-store process details must not leak into operator output.
        }
    }
}

/// <summary>
/// Reads the same non-secret profile file and Secret Service item used by the
/// normal MEM CLI. No plaintext token file, environment fallback, or browser
/// credential surface is supported.
/// </summary>
public sealed class MemCliTargetProfileCredentialResolver :
    ITargetProfileCredentialResolver
{
    private const int CurrentSchemaVersion = 2;
    private const int MaximumConfigBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configDirectoryPath;
    private readonly ITargetSecretToolProcessRunner _processRunner;

    public MemCliTargetProfileCredentialResolver()
        : this(
            ResolveDefaultConfigDirectoryPath(),
            new SystemTargetSecretToolProcessRunner())
    {
    }

    public MemCliTargetProfileCredentialResolver(
        string configDirectoryPath,
        ITargetSecretToolProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configDirectoryPath);
        ArgumentNullException.ThrowIfNull(processRunner);

        _configDirectoryPath = Path.GetFullPath(configDirectoryPath);
        _processRunner = processRunner;
    }

    public async Task<TargetProfileCredential> ResolveAsync(
        string profileName,
        CancellationToken cancellationToken)
    {
        if (!TargetProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedProfileName))
        {
            throw new InvalidOperationException(
                "A valid named MEM CLI profile is required for target import.");
        }

        var configPath = Path.Combine(
            _configDirectoryPath,
            "config.json");

        var profile = await ReadProfileAsync(
            configPath,
            normalizedProfileName,
            cancellationToken);

        if (!TargetProfileValidator.TryCreateCredentialKey(
                profile.Name,
                profile.ServerUrl,
                out var key))
        {
            throw new InvalidOperationException(
                $"MEM CLI profile '{normalizedProfileName}' is invalid. " +
                "Recreate it with: mem profile create <name> --server <url>");
        }

        if (!OperatingSystem.IsLinux())
        {
            throw new InvalidOperationException(
                "The OS secret store is unavailable. MM-05C currently requires " +
                "the Linux Secret Service credential used by MEM CLI.");
        }

        var result = await _processRunner.RunAsync(
            [
                "lookup",
                "application",
                "matrix-easy-mode",
                "kind",
                "cli-device-session",
                "profile",
                key.ProfileName,
                "server",
                key.ServerUrl
            ],
            cancellationToken);

        if (!result.Started || result.TimedOut)
        {
            throw new InvalidOperationException(
                "The OS secret store is unavailable. Install and unlock a " +
                "Secret Service keyring, then run mem login --device again.");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"No MEM CLI device session is stored for profile: " +
                $"{key.ProfileName}. Run: mem login --device --profile " +
                key.ProfileName);
        }

        var credential = result.StandardOutput.Trim();
        if (!TargetDeviceCredentialValidator.IsValid(credential))
        {
            throw new InvalidOperationException(
                $"The stored MEM CLI device session for profile " +
                $"{key.ProfileName} is invalid. Run: mem logout --profile " +
                $"{key.ProfileName}, then mem login --device --profile " +
                key.ProfileName);
        }

        return new TargetProfileCredential(
            key.ProfileName,
            key.ServerUrl,
            credential);
    }

    private static async Task<ProfileDocument> ReadProfileAsync(
        string configPath,
        string profileName,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                throw new InvalidOperationException(
                    $"MEM CLI profile '{profileName}' was not found. " +
                    "Create it with: mem profile create <name> --server <url>");
            }

            var fileInfo = new FileInfo(configPath);
            if (fileInfo.LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    "The MEM CLI profile configuration path is unsafe.");
            }

            if (fileInfo.Length > MaximumConfigBytes)
            {
                throw new InvalidOperationException(
                    "The MEM CLI profile configuration is unexpectedly large.");
            }

            await using var stream = new FileStream(
                configPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var document = await JsonSerializer.DeserializeAsync<
                PreferencesDocument>(
                stream,
                JsonOptions,
                cancellationToken);

            if (document is null ||
                document.SchemaVersion != CurrentSchemaVersion ||
                document.Profiles is null)
            {
                throw new InvalidOperationException(
                    "The MEM CLI profile configuration is invalid or unsupported.");
            }

            var matches = document.Profiles
                .Where(profile =>
                    profile is not null &&
                    TargetProfileValidator.TryNormalizeName(
                        profile.Name,
                        out var normalizedName) &&
                    string.Equals(
                        normalizedName,
                        profileName,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    $"MEM CLI profile '{profileName}' was not found. " +
                    "Create it with: mem profile create <name> --server <url>");
            }

            var match = matches[0]!;
            if (!TargetProfileValidator.TryNormalizeName(
                    match.Name,
                    out var normalizedProfileName) ||
                !TargetProfileValidator.TryNormalizeServerUrl(
                    match.ServerUrl,
                    out var normalizedServerUrl))
            {
                throw new InvalidOperationException(
                    $"MEM CLI profile '{profileName}' is invalid. " +
                    "Recreate it with: mem profile create <name> --server <url>");
            }

            return new ProfileDocument(
                normalizedProfileName,
                normalizedServerUrl);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "The MEM CLI profile configuration is invalid or unsupported.");
        }
        catch (IOException)
        {
            throw new InvalidOperationException(
                "The MEM CLI profile configuration could not be read.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The MEM CLI profile configuration could not be read.");
        }
    }

    private static string ResolveDefaultConfigDirectoryPath()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable(
            "XDG_CONFIG_HOME");

        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(xdgConfigHome, "mem");
        }

        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrWhiteSpace(applicationData))
        {
            return Path.Combine(applicationData, "mem");
        }

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, ".config", "mem");
        }

        throw new InvalidOperationException(
            "A local MEM CLI configuration directory could not be resolved.");
    }

    private sealed record PreferencesDocument(
        int SchemaVersion,
        string? DefaultProfile,
        List<ProfileInputDocument?>? Profiles);

    private sealed record ProfileInputDocument(
        string? Name,
        string? ServerUrl);

    private sealed record ProfileDocument(
        string Name,
        string ServerUrl);
}
