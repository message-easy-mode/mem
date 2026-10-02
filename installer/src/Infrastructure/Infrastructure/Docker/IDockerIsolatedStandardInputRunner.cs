namespace Infrastructure.Docker;

/// <summary>
/// Runs one bounded command in a disposable, network-isolated Docker container.
/// Standard input is transported only over the Docker attach stream; callers do
/// not provide environment variables, mounts, ports or network configuration.
/// </summary>
public interface IDockerIsolatedStandardInputRunner
{
    Task<DockerIsolatedStandardInputResult> RunAsync(
        string image,
        string containerName,
        IReadOnlyList<string> command,
        ReadOnlyMemory<char> standardInput,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed record DockerIsolatedStandardInputResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;
}
