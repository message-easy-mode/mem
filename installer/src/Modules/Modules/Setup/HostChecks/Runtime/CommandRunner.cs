using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Modules.Setup.HostChecks.Runtime;

/// <summary>
/// Runs Setup/install helper commands with bounded execution and bounded output.
///
/// Arguments are deliberately not emitted into logs. Installation commands may
/// eventually contain secret-bearing values and the generic process boundary
/// must remain safe even when a caller makes a mistake.
/// </summary>
public sealed class CommandRunner(ILogger<CommandRunner> logger) : ICommandRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private const int MaxCapturedCharactersPerStream = 64 * 1024;

    public Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, DefaultTimeout, cancellationToken);

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
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

        var stdout = new BoundedCapture(MaxCapturedCharactersPerStream);
        var stderr = new BoundedCapture(MaxCapturedCharactersPerStream);

        process.OutputDataReceived += (_, args) => stdout.AppendLine(args.Data);
        process.ErrorDataReceived += (_, args) => stderr.AppendLine(args.Data);

        try
        {
            logger.LogDebug(
                "Running bounded Setup command {Command} with {ArgumentCount} arguments. TimeoutSeconds={TimeoutSeconds}",
                fileName,
                arguments.Count,
                timeout.TotalSeconds);

            if (!process.Start())
            {
                return new CommandResult(
                    ExitCode: -1,
                    StandardOutput: string.Empty,
                    StandardError: "Process failed to start.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
                process.WaitForExit();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                await WaitForExitBestEffortAsync(process);

                return new CommandResult(
                    ExitCode: -1,
                    StandardOutput: stdout.GetText(),
                    StandardError: BuildTimeoutError(timeout, stderr.GetText()),
                    TimedOut: true,
                    OutputTruncated: stdout.Truncated || stderr.Truncated);
            }

            return new CommandResult(
                process.ExitCode,
                stdout.GetText(),
                stderr.GetText(),
                TimedOut: false,
                OutputTruncated: stdout.Truncated || stderr.Truncated);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            TryKill(process);
            logger.LogWarning(
                ex,
                "Setup command {Command} failed before normal completion.",
                fileName);

            return new CommandResult(
                ExitCode: -1,
                StandardOutput: stdout.GetText(),
                StandardError: string.IsNullOrWhiteSpace(stderr.GetText())
                    ? ex.Message
                    : stderr.GetText(),
                TimedOut: false,
                OutputTruncated: stdout.Truncated || stderr.Truncated);
        }
    }

    private static string BuildTimeoutError(TimeSpan timeout, string stderr)
    {
        var timeoutMessage = $"Process timed out after {timeout.TotalSeconds:0.###} seconds.";

        return string.IsNullOrWhiteSpace(stderr)
            ? timeoutMessage
            : $"{timeoutMessage}{Environment.NewLine}{stderr}";
    }

    private static async Task WaitForExitBestEffortAsync(Process process)
    {
        try
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(cleanupTimeout.Token);
            process.WaitForExit();
        }
        catch
        {
            // Best-effort process cleanup is itself bounded.
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
            // Best-effort cleanup. The caller receives a bounded failure.
        }
    }

    private sealed class BoundedCapture(int maxCharacters)
    {
        private readonly object _gate = new();
        private readonly StringBuilder _builder = new();

        public bool Truncated { get; private set; }

        public void AppendLine(string? value)
        {
            if (value is null)
            {
                return;
            }

            lock (_gate)
            {
                if (_builder.Length >= maxCharacters)
                {
                    Truncated = true;
                    return;
                }

                var remaining = maxCharacters - _builder.Length;
                var take = Math.Min(value.Length, remaining);
                _builder.Append(value.AsSpan(0, take));

                if (take < value.Length)
                {
                    Truncated = true;
                    return;
                }

                if (_builder.Length < maxCharacters)
                {
                    _builder.AppendLine();
                }
                else
                {
                    Truncated = true;
                }
            }
        }

        public string GetText()
        {
            lock (_gate)
            {
                var value = _builder.ToString().Trim();

                if (!Truncated)
                {
                    return value;
                }

                return string.IsNullOrEmpty(value)
                    ? "... output truncated ..."
                    : $"{value}{Environment.NewLine}... output truncated ...";
            }
        }
    }
}
