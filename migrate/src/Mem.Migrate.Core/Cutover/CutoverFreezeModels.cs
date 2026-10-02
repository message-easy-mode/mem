using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Core.Cutover;

public sealed record CutoverFreezeOptions
{
    public AssessmentOptions Assessment { get; init; } = new();
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string PlanPath { get; init; } = string.Empty;
    public string ExpectedPlanHash { get; init; } = string.Empty;
    public string? FreezeAttemptId { get; init; }
    public int StopTimeoutSeconds { get; init; } = 30;
    public bool Resume { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public CutoverFreezeOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(PlanPath))
        {
            throw new ArgumentException("Cutover plan path is required.");
        }

        if (!IsSha256(ExpectedPlanHash))
        {
            throw new ArgumentException(
                "Expected plan hash must be a 64-character SHA-256 value.");
        }

        if (StopTimeoutSeconds is < 5 or > 300)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StopTimeoutSeconds),
                "Container stop timeout must be between 5 and 300 seconds.");
        }

        var attemptId = string.IsNullOrWhiteSpace(FreezeAttemptId)
            ? $"mm06b-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..42]
            : FreezeAttemptId.Trim();
        if (!IsIdentifier(attemptId, 8, 96))
        {
            throw new ArgumentException(
                "Freeze attempt ID must contain 8-96 ASCII letters, digits, '-' or '_'.");
        }

        if (Resume && string.IsNullOrWhiteSpace(FreezeAttemptId))
        {
            throw new ArgumentException("--resume requires an explicit --freeze-attempt-id.");
        }

        var workspace = Path.GetFullPath(WorkspacePath);
        var output = Path.GetFullPath(OutputPath);
        var planPath = Path.GetFullPath(PlanPath);
        var assessment = (Assessment with
        {
            WorkspacePath = Path.Combine(
                workspace,
                "cutover-freezes",
                attemptId,
                "source-assessment-work"),
            OutputPath = Path.Combine(
                output,
                attemptId,
                "source-assessment"),
            IncludeSensitivePaths = false,
            JsonConsoleOutput = false,
            NonInteractive = true
        }).Normalize();

        return this with
        {
            Assessment = assessment,
            WorkspacePath = workspace,
            OutputPath = output,
            PlanPath = planPath,
            ExpectedPlanHash = ExpectedPlanHash.ToLowerInvariant(),
            FreezeAttemptId = attemptId
        };
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsIdentifier(string value, int minimum, int maximum) =>
        value.Length >= minimum &&
        value.Length <= maximum &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record CutoverFreezeContainerResult(
    string Role,
    string ContainerId,
    string ContainerName,
    string PreviousRestartPolicy,
    bool WasRunning,
    string FinalState,
    string FinalRestartPolicy,
    bool RestartPolicyDisabled,
    bool Stopped);

public sealed record CutoverRollbackCheckpoint(
    string PlanId,
    string PlanHash,
    string SourceFingerprint,
    DateTimeOffset CapturedAtUtc,
    CutoverSourceContainer[] SourceContainers,
    CutoverRetainedContainer[] RetainedContainers,
    CutoverRouteHint[] RouteHints,
    string[] RestoreOrder,
    string Warning);

public sealed record CutoverFreezeReport(
    string Schema,
    int SchemaVersion,
    string Status,
    string FreezeAttemptId,
    string PlanId,
    string PlanHash,
    string SourceFingerprint,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool SourceFrozen,
    bool PublicRoutingMutationOccurred,
    CutoverFreezeContainerResult[] Containers,
    CutoverRollbackCheckpoint RollbackCheckpoint,
    string JsonPath,
    string MarkdownPath,
    string[] Warnings,
    string[] NextSteps);

public sealed record StoredCutoverFreezeRun(
    string FreezeAttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string PlanId,
    string PlanHash,
    string? SourceFingerprint,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);

public interface ICutoverFreezeJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<StoredCutoverFreezeRun?> GetAsync(
        string freezeAttemptId,
        CancellationToken cancellationToken);
    Task StartAsync(
        string freezeAttemptId,
        DateTimeOffset startedAtUtc,
        string planId,
        string planHash,
        CancellationToken cancellationToken);
    Task CompleteAsync(
        CutoverFreezeReport report,
        string reportJson,
        CancellationToken cancellationToken);
    Task FailAsync(
        string freezeAttemptId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}

public interface ICutoverSourceFreezer
{
    Task<CutoverFreezeContainerResult[]> FreezeAsync(
        CutoverPlanDocument plan,
        string dockerCommand,
        int commandTimeoutSeconds,
        int stopTimeoutSeconds,
        CancellationToken cancellationToken);
}
