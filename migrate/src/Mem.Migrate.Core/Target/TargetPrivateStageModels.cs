namespace Mem.Migrate.Core.Target;

public sealed record TargetPrivateStageOptions
{
    public string ImportAttemptId { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string? StageAttemptId { get; init; }
    public int HttpTimeoutSeconds { get; init; } = 900;
    public bool AllowInsecureTls { get; init; }
    public bool DestroyAfterVerification { get; init; }
    public bool Resume { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public TargetPrivateStageOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(ImportAttemptId))
            throw new ArgumentException("A completed MM-05C import attempt ID is required.");
        if (!TargetProfileValidator.TryNormalizeName(ProfileName, out var profile))
            throw new ArgumentException("A valid named MEM CLI profile is required.");
        if (HttpTimeoutSeconds is < 30 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(HttpTimeoutSeconds), "HTTP timeout must be between 30 and 3600 seconds.");

        var id = string.IsNullOrWhiteSpace(StageAttemptId)
            ? $"mm05d-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"
            : StageAttemptId.Trim();
        if (id.Length > 100 || id.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new ArgumentException("Stage attempt ID may contain only letters, digits, '-' and '_'.");

        return this with
        {
            ImportAttemptId = ImportAttemptId.Trim(),
            ProfileName = profile,
            WorkspacePath = Path.GetFullPath(WorkspacePath),
            OutputPath = Path.GetFullPath(OutputPath),
            StageAttemptId = id
        };
    }
}

public sealed record TargetPrivateStageReport(
    string Schema,
    int SchemaVersion,
    string StageAttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ImportAttemptId,
    string ProfileName,
    string TargetBaseUrl,
    string CatalogEntryId,
    string RestoreSessionId,
    string StagingId,
    bool PrivateOnly,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool PublishedRoutesAbsent,
    bool RequiresExplicitDestroy,
    bool DestroyRequested,
    bool DestroySucceeded,
    string WorkspaceEvidencePath,
    string[] Warnings,
    string[] NextSteps);

public sealed record StoredTargetPrivateStageRun(
    string StageAttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string ImportAttemptId,
    string TargetProfileName,
    string TargetBaseUrl,
    string CatalogEntryId,
    string? RestoreSessionId,
    string? StagingId,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);

public interface ITargetPrivateStageJournal
{
    Task InitializeAsync(CancellationToken ct);
    Task<StoredTargetPrivateStageRun?> GetAsync(string stageAttemptId, CancellationToken ct);
    Task StartAsync(string stageAttemptId, DateTimeOffset startedAtUtc, string importAttemptId, string profileName, string targetBaseUrl, string catalogEntryId, CancellationToken ct);
    Task SaveProgressAsync(string stageAttemptId, string? restoreSessionId, string? stagingId, CancellationToken ct);
    Task CompleteAsync(TargetPrivateStageReport report, string reportJson, CancellationToken ct);
    Task FailAsync(string stageAttemptId, DateTimeOffset completedAtUtc, string errorCode, string errorMessage, CancellationToken ct);
}
