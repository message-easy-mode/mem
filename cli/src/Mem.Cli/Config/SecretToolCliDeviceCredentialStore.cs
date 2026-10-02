using System.Diagnostics;

namespace Mem.Cli.Config;

/// <summary>
/// Process boundary for Linux Secret Service operations. Process arguments are
/// intentionally represented separately from stdin so tests can prove that an
/// opaque credential is never placed in argv or process listings.
/// </summary>
public interface ISecretToolProcessRunner
{
    Task<SecretToolProcessResult> RunAsync(
        SecretToolProcessRequest request,
        CancellationToken ct = default);
}

public sealed record SecretToolProcessRequest(
    IReadOnlyList<string> Arguments,
    string? StandardInput = null);

public sealed record SecretToolProcessResult(
    bool Started,
    bool TimedOut,
    int? ExitCode,
    string StandardOutput)
{
    public bool Succeeded =>
        Started &&
        !TimedOut &&
        ExitCode == 0;
}

/// <summary>
/// Executes the documented <c>secret-tool</c> command without a shell. Standard
/// error is deliberately discarded so keyring/system detail cannot leak to CLI
/// output. The raw credential is supplied through stdin only.
/// </summary>
public sealed class SystemSecretToolProcessRunner : ISecretToolProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<SecretToolProcessResult> RunAsync(
        SecretToolProcessRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var process = new Process();

        process.StartInfo = new ProcessStartInfo
        {
            FileName = "secret-tool",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = request.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in request.Arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return new SecretToolProcessResult(
                    Started: false,
                    TimedOut: false,
                    ExitCode: null,
                    StandardOutput: string.Empty);
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(
                    request.StandardInput.AsMemory(),
                    ct);
                await process.StandardInput.FlushAsync();
                process.StandardInput.Close();
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(process);
                await DrainAsync(standardOutputTask, standardErrorTask);

                return new SecretToolProcessResult(
                    Started: true,
                    TimedOut: true,
                    ExitCode: null,
                    StandardOutput: string.Empty);
            }

            var standardOutput = await standardOutputTask;
            await standardErrorTask;

            return new SecretToolProcessResult(
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
            return new SecretToolProcessResult(
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
            // The child process exited while cancellation was being handled.
        }
    }

    private static async Task DrainAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(
                standardOutputTask,
                standardErrorTask);
        }
        catch (Exception)
        {
            // Process output must never surface as a CLI detail string.
        }
    }
}

/// <summary>
/// Linux Secret Service credential store. It requires a running
/// Secret Service-compatible keyring and the <c>secret-tool</c> executable.
/// MEM CLI refuses login when either is unavailable rather than writing an
/// opaque device session credential to a file, profile, environment variable,
/// shell history, or process arguments.
/// </summary>
public sealed class SecretToolCliDeviceCredentialStore(
    ISecretToolProcessRunner processRunner)
    : ICliDeviceCredentialStore
{
    private const string ApplicationAttribute = "application";
    private const string ApplicationValue = "matrix-easy-mode";
    private const string KindAttribute = "kind";
    private const string KindValue = "cli-device-session";
    private const string ProfileAttribute = "profile";
    private const string ServerAttribute = "server";

    private readonly ISecretToolProcessRunner _processRunner =
        processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<CliDeviceCredentialStoreAvailability> CheckAvailableAsync(
        CancellationToken ct = default)
    {
        var probeKey = new CliDeviceCredentialKey(
            ProfileName: $"probe-{Guid.NewGuid():N}"[..18],
            ServerUrl: "https://mem-cli-probe.invalid");
        var probeCredential = CliDeviceCredentialValidator.CreateProbeCredential();

        try
        {
            var stored = await StoreAsync(
                probeKey,
                probeCredential,
                ct);

            if (!stored.Succeeded)
            {
                return CliDeviceCredentialStoreAvailability.Unavailable();
            }

            var read = await ReadAsync(probeKey, ct);

            return read.Succeeded &&
                string.Equals(
                    read.Credential,
                    probeCredential,
                    StringComparison.Ordinal)
                ? CliDeviceCredentialStoreAvailability.AvailableResult()
                : CliDeviceCredentialStoreAvailability.Unavailable();
        }
        finally
        {
            // A failed or interrupted availability probe must not leave even a
            // test-only value in the user keyring. Ignore cleanup failure here:
            // availability remains false unless the full round trip succeeded.
            await DeleteAsync(probeKey, CancellationToken.None);
        }
    }

    public async Task<CliDeviceCredentialReadResult> ReadAsync(
        CliDeviceCredentialKey key,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var result = await _processRunner.RunAsync(
            new SecretToolProcessRequest(
                ["lookup", .. BuildAttributes(key)]),
            ct);

        if (!result.Started || result.TimedOut)
        {
            return CliDeviceCredentialReadResult.Unavailable();
        }

        if (result.ExitCode != 0)
        {
            // secret-tool lookup conventionally uses a non-zero result when no
            // matching item exists. The caller treats this as no usable local
            // sign-in and never prints process stderr.
            return CliDeviceCredentialReadResult.NotFound();
        }

        var credential = result.StandardOutput.Trim();

        return CliDeviceCredentialValidator.IsValid(credential)
            ? CliDeviceCredentialReadResult.Found(credential)
            : CliDeviceCredentialReadResult.InvalidCredential();
    }

    public async Task<CliDeviceCredentialWriteResult> StoreAsync(
        CliDeviceCredentialKey key,
        string credential,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!CliDeviceCredentialValidator.IsValid(credential))
        {
            return CliDeviceCredentialWriteResult.InvalidCredential();
        }

        var result = await _processRunner.RunAsync(
            new SecretToolProcessRequest(
                [
                    "store",
                    $"--label=MEM CLI device session ({key.ProfileName})",
                    .. BuildAttributes(key)
                ],
                StandardInput: credential.Trim() + Environment.NewLine),
            ct);

        return result.Succeeded
            ? CliDeviceCredentialWriteResult.Success()
            : CliDeviceCredentialWriteResult.Unavailable();
    }

    public async Task<CliDeviceCredentialDeleteResult> DeleteAsync(
        CliDeviceCredentialKey key,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        var result = await _processRunner.RunAsync(
            new SecretToolProcessRequest(
                ["clear", .. BuildAttributes(key)]),
            ct);

        return result.Succeeded
            ? CliDeviceCredentialDeleteResult.Success()
            : CliDeviceCredentialDeleteResult.Unavailable();
    }

    private static IReadOnlyList<string> BuildAttributes(
        CliDeviceCredentialKey key) =>
        [
            ApplicationAttribute,
            ApplicationValue,
            KindAttribute,
            KindValue,
            ProfileAttribute,
            key.ProfileName,
            ServerAttribute,
            key.ServerUrl
        ];
}
