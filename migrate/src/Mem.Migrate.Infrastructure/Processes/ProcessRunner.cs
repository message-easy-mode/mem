using System.Diagnostics;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Process timeout must be positive.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                return Failure("process_start_failed", "The process did not start.");
            }
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            return Failure(
                "process_start_failed",
                AssessmentRedactor.RedactText(ex.Message));
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);

        var timedOut = false;

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;

            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between cancellation and kill.
            }

            await process.WaitForExitAsync(CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var stdout = Limit(await stdoutTask, request.MaximumOutputCharacters);
        var stderr = Limit(
            AssessmentRedactor.RedactText(await stderrTask),
            request.MaximumOutputCharacters);

        return new ProcessResult(
            Started: true,
            ExitCode: process.HasExited ? process.ExitCode : null,
            TimedOut: timedOut,
            StandardOutput: stdout,
            StandardError: stderr,
            ErrorCode: timedOut ? "process_timeout" : null,
            ErrorMessage: timedOut
                ? $"The process exceeded its {request.Timeout.TotalSeconds:0}-second timeout."
                : null);
    }

    private static ProcessResult Failure(string code, string message) =>
        new(
            Started: false,
            ExitCode: null,
            TimedOut: false,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            ErrorCode: code,
            ErrorMessage: message);

    private static string Limit(string value, int maximumCharacters)
    {
        if (maximumCharacters <= 0 || value.Length <= maximumCharacters)
        {
            return value;
        }

        return value[..maximumCharacters] +
            Environment.NewLine +
            "<output truncated>";
    }
}
