using System.Diagnostics;

namespace Modules.Operator.Migrations.Runtime;

public interface IMemMigrateRuntimeProbe
{
    Task<MemMigrateProbeResult> RunAsync(
        string commandPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public sealed record MemMigrateProbeResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed class MemMigrateRuntimeProbe : IMemMigrateRuntimeProbe
{
    public async Task<MemMigrateProbeResult> RunAsync(
        string commandPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = commandPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The configured mem-migrate command could not be started.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new MemMigrateProbeResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }
}
