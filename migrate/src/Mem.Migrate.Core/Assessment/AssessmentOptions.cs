namespace Mem.Migrate.Core.Assessment;

public sealed record AssessmentOptions
{
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string DockerCommand { get; init; } = "docker";
    public string? ApiUrl { get; init; }
    public string? PostgresContainer { get; init; }
    public bool IncludeSensitivePaths { get; init; }
    public bool JsonConsoleOutput { get; init; }
    public bool NonInteractive { get; init; }
    public int CommandTimeoutSeconds { get; init; } = 30;
    public int HttpTimeoutSeconds { get; init; } = 5;
    public int MaximumFileScanEntries { get; init; } = 250_000;
    public long MaximumConfigurationBytes { get; init; } = 4 * 1024 * 1024;

    public AssessmentOptions Normalize()
    {
        var workspace = Path.GetFullPath(WorkspacePath);
        var output = Path.GetFullPath(OutputPath);

        if (CommandTimeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CommandTimeoutSeconds),
                "Command timeout must be between 1 and 600 seconds.");
        }

        if (HttpTimeoutSeconds is < 1 or > 60)
        {
            throw new ArgumentOutOfRangeException(
                nameof(HttpTimeoutSeconds),
                "HTTP timeout must be between 1 and 60 seconds.");
        }

        if (MaximumFileScanEntries is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumFileScanEntries),
                "Maximum file scan entries must be between 1 and 1,000,000.");
        }

        if (MaximumConfigurationBytes is < 1024 or > 64L * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumConfigurationBytes),
                "Maximum configuration bytes must be between 1 KiB and 64 MiB.");
        }

        return this with
        {
            WorkspacePath = workspace,
            OutputPath = output,
            DockerCommand = string.IsNullOrWhiteSpace(DockerCommand)
                ? "docker"
                : DockerCommand.Trim(),
            ApiUrl = string.IsNullOrWhiteSpace(ApiUrl) ? null : ApiUrl.Trim(),
            PostgresContainer = string.IsNullOrWhiteSpace(PostgresContainer)
                ? null
                : PostgresContainer.Trim()
        };
    }
}
