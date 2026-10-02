namespace Mem.Migrate.Core.Target;

public sealed record TargetImportOptions
{
    public string ManifestPath { get; init; } = string.Empty;
    public string StackExportPath { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = ".workspace";
    public string? AttemptId { get; init; }
    public string DisplayName { get; init; } = "Imported MEM server";
    public int HttpTimeoutSeconds { get; init; } = 300;
    public bool AllowInsecureTls { get; init; }
    public bool Resume { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public TargetImportOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            throw new ArgumentException(
                "A migration intake manifest path is required.");
        }

        if (string.IsNullOrWhiteSpace(StackExportPath))
        {
            throw new ArgumentException(
                "A MEM stack export ZIP path is required.");
        }

        if (!TargetProfileValidator.TryNormalizeName(
                ProfileName,
                out var normalizedProfileName))
        {
            throw new ArgumentException(
                "A valid named MEM CLI profile is required. " +
                "Profile names use 1-32 letters, numbers, or hyphens.");
        }

        if (HttpTimeoutSeconds is < 10 or > 3600)
        {
            throw new ArgumentException(
                "HTTP timeout must be between 10 and 3600 seconds.");
        }

        var id = string.IsNullOrWhiteSpace(AttemptId)
            ? $"mm05c-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..40]
            : NormalizeIdentifier(AttemptId);

        return this with
        {
            ManifestPath = Path.GetFullPath(ManifestPath),
            StackExportPath = Path.GetFullPath(StackExportPath),
            ProfileName = normalizedProfileName,
            WorkspacePath = Path.GetFullPath(WorkspacePath),
            AttemptId = id,
            DisplayName = string.IsNullOrWhiteSpace(DisplayName)
                ? "Imported MEM server"
                : DisplayName.Trim()
        };
    }

    private static string NormalizeIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length is < 3 or > 80 ||
            trimmed.Any(c =>
                !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            throw new ArgumentException(
                "Attempt IDs must contain 3-80 ASCII letters, digits, '-' or '_'.");
        }

        return trimmed;
    }
}

public sealed record TargetImportReport(
    string Schema,
    int SchemaVersion,
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string TargetProfileName,
    string TargetBaseUrl,
    string ManifestPath,
    string ManifestSha256,
    string StackExportPath,
    string StackExportSha256,
    long StackExportBytes,
    string ArtifactId,
    string IntakeId,
    string ValidationId,
    string CatalogEntryId,
    string BindingStatus,
    string ExpectedSha256,
    string ActualSha256,
    long ExpectedBytes,
    long ActualBytes,
    string[] Warnings,
    string[] NextSteps);

public sealed record StoredTargetImportRun(
    string AttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string ManifestSha256,
    string StackExportSha256,
    string? TargetProfileName,
    string? TargetBaseUrl,
    string? IntakeId,
    string? ValidationId,
    string? CatalogEntryId,
    string? ArtifactId,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);

public interface ITargetImportJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<StoredTargetImportRun?> GetAsync(
        string attemptId,
        CancellationToken cancellationToken);

    Task StartAsync(
        string attemptId,
        DateTimeOffset startedAtUtc,
        string manifestSha256,
        string stackExportSha256,
        string targetProfileName,
        string targetBaseUrl,
        CancellationToken cancellationToken);

    Task SaveProgressAsync(
        string attemptId,
        string? intakeId,
        string? validationId,
        string? catalogEntryId,
        string? artifactId,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        TargetImportReport report,
        string reportJson,
        CancellationToken cancellationToken);

    Task FailAsync(
        string attemptId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}
