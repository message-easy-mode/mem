using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Modules.Setup.HostChecks.Runtime;

/// <summary>
/// Runs a bounded process with redirected standard input. The input value is
/// never included in logs, arguments, environment variables, or exception text.
/// </summary>
public sealed class StandardInputCommandRunner(
    ILogger<StandardInputCommandRunner> logger) : IStandardInputCommandRunner
{
    public async Task<StandardInputCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<char> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            logger.LogDebug(
                "Running bounded standard-input command {Command} with {ArgumentCount} arguments.",
                fileName,
                arguments.Count);

            if (!process.Start())
            {
                return new StandardInputCommandResult(
                    -1,
                    string.Empty,
                    "Process failed to start.",
                    TimedOut: false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.StandardInput.WriteLineAsync(standardInput, timeoutSource.Token);
            process.StandardInput.Close();

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await process.WaitForExitAsync(CancellationToken.None);
                await Task.WhenAll(stdoutTask, stderrTask);
                return new StandardInputCommandResult(
                    -1,
                    string.Empty,
                    "Process timed out.",
                    TimedOut: true);
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return new StandardInputCommandResult(
                process.ExitCode,
                stdout.Trim(),
                stderr.Trim(),
                TimedOut: false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            logger.LogWarning(
                exception,
                "Standard-input command {Command} failed.",
                fileName);
            return new StandardInputCommandResult(
                -1,
                string.Empty,
                "Process failed.",
                TimedOut: false);
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
        catch
        {
            // Best-effort cleanup. The caller still receives a safe failure.
        }
    }
}
