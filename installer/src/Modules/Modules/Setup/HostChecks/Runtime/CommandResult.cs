namespace Modules.Setup.HostChecks.Runtime;

public sealed record CommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool OutputTruncated = false)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
