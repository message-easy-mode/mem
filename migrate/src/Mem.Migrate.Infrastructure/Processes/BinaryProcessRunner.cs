using System.Diagnostics;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Processes;

public sealed class BinaryProcessRunner : IBinaryProcessRunner
{
    public async Task<BinaryProcessResult> RunToFileAsync(
        BinaryProcessRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Process timeout must be positive.");
        }

        var outputPath = Path.GetFullPath(request.OutputPath);
        var parent = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException(
                "Binary process output has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(parent);

        if (File.Exists(outputPath))
        {
            throw new IOException(
                $"Binary process output already exists: {outputPath}");
        }

        var partialPath = outputPath + ".partial";
        File.Delete(partialPath);
        var ownsOutput = false;
        Process? process = null;

        try
        {
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

            process = new Process { StartInfo = startInfo };

            try
            {
                if (!process.Start())
                {
                    return Failure(
                        "process_start_failed",
                        "The process did not start.");
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

            var stderrTask = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(request.Timeout);
            var timedOut = false;

            await using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                var stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(
                    destination,
                    CancellationToken.None);

                try
                {
                    await process.WaitForExitAsync(timeout.Token);
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
                }

                await stdoutTask;
                await destination.FlushAsync(CancellationToken.None);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var stderr = Limit(
                AssessmentRedactor.RedactText(await stderrTask),
                request.MaximumErrorCharacters);
            var outputBytes = File.Exists(partialPath)
                ? new FileInfo(partialPath).Length
                : 0;

            if (timedOut || process.ExitCode != 0 || outputBytes == 0)
            {
                File.Delete(partialPath);
                return new BinaryProcessResult(
                    Started: true,
                    ExitCode: process.HasExited ? process.ExitCode : null,
                    TimedOut: timedOut,
                    StandardError: stderr,
                    ErrorCode: timedOut ? "process_timeout" : null,
                    ErrorMessage: timedOut
                        ? $"The process exceeded its {request.Timeout.TotalSeconds:0}-second timeout."
                        : null,
                    OutputBytes: 0);
            }

            PrivateFilePermissions.EnsureFile(partialPath);
            File.Move(partialPath, outputPath);
            ownsOutput = true;
            PrivateFilePermissions.EnsureFile(outputPath);

            return new BinaryProcessResult(
                Started: true,
                ExitCode: process.ExitCode,
                TimedOut: false,
                StandardError: stderr,
                ErrorCode: null,
                ErrorMessage: null,
                OutputBytes: outputBytes);
        }
        catch
        {
            File.Delete(partialPath);

            if (ownsOutput)
            {
                File.Delete(outputPath);
            }

            throw;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static BinaryProcessResult Failure(string code, string message) =>
        new(
            Started: false,
            ExitCode: null,
            TimedOut: false,
            StandardError: string.Empty,
            ErrorCode: code,
            ErrorMessage: message,
            OutputBytes: 0);

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
