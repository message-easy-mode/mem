namespace Modules.Setup.HostChecks.Runtime;

public interface IStandardInputCommandRunner
{
    Task<StandardInputCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        ReadOnlyMemory<char> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record StandardInputCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
