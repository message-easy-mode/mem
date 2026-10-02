namespace Mem.Migrate.Core.Processes;

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    int MaximumOutputCharacters = 16 * 1024 * 1024,
    IReadOnlyDictionary<string, string?>? Environment = null,
    string? WorkingDirectory = null);

public sealed record ProcessResult(
    bool Started,
    int? ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool Succeeded => Started && !TimedOut && ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        CancellationToken cancellationToken);
}

public sealed record BinaryProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string OutputPath,
    TimeSpan Timeout,
    int MaximumErrorCharacters = 1024 * 1024,
    IReadOnlyDictionary<string, string?>? Environment = null,
    string? WorkingDirectory = null);

public sealed record BinaryProcessResult(
    bool Started,
    int? ExitCode,
    bool TimedOut,
    string StandardError,
    string? ErrorCode,
    string? ErrorMessage,
    long OutputBytes)
{
    public bool Succeeded =>
        Started && !TimedOut && ExitCode == 0 && OutputBytes > 0;
}

public interface IBinaryProcessRunner
{
    Task<BinaryProcessResult> RunToFileAsync(
        BinaryProcessRequest request,
        CancellationToken cancellationToken);
}
