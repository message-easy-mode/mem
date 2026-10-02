using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Core.Cutover;

public sealed record CutoverPrepareOptions
{
    public AssessmentOptions Assessment { get; init; } = new();
    public string WorkspacePath { get; init; } = ".workspace";
    public string? TargetWorkspacePath { get; init; }
    public string OutputPath { get; init; } = ".";
    public string StageAttemptId { get; init; } = string.Empty;
    public string ExpectedSourceFingerprint { get; init; } = string.Empty;
    public string? PlanId { get; init; }
    public int ValidForMinutes { get; init; } = 30;
    public bool DevelopmentExternalControlPlane { get; init; }
    public bool Resume { get; init; }
    public bool JsonConsoleOutput { get; init; }
    public string ProducerVersion { get; init; } = "unknown";

    public CutoverPrepareOptions Normalize()
    {
        if (!IsSha256(ExpectedSourceFingerprint))
        {
            throw new ArgumentException(
                "Expected source fingerprint must be a 64-character SHA-256 value.");
        }

        if (!IsIdentifier(StageAttemptId, 3, 100))
        {
            throw new ArgumentException(
                "Stage attempt ID must contain 3-100 ASCII letters, digits, '-' or '_'.");
        }

        if (ValidForMinutes is < 5 or > 240)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ValidForMinutes),
                "Cutover plan validity must be between 5 and 240 minutes.");
        }

        var planId = string.IsNullOrWhiteSpace(PlanId)
            ? $"mm06a-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..42]
            : PlanId.Trim();

        if (!IsIdentifier(planId, 8, 96))
        {
            throw new ArgumentException(
                "Plan ID must contain 8-96 ASCII letters, digits, '-' or '_'.");
        }

        if (Resume && string.IsNullOrWhiteSpace(PlanId))
        {
            throw new ArgumentException("--resume requires an explicit --plan-id.");
        }

        if (string.IsNullOrWhiteSpace(ProducerVersion))
        {
            throw new ArgumentException("The mem-migrate producer version is required.");
        }

        var workspace = Path.GetFullPath(WorkspacePath);
        var output = Path.GetFullPath(OutputPath);
        var targetWorkspaceValue = string.IsNullOrWhiteSpace(TargetWorkspacePath)
            ? WorkspacePath
            : TargetWorkspacePath!;
        var targetWorkspace = Path.GetFullPath(targetWorkspaceValue);
        var assessmentWorkspace = Path.Combine(
            workspace,
            "cutover-plans",
            planId,
            "source-assessment-work");
        var assessmentOutput = Path.Combine(
            output,
            planId,
            "source-assessment");
        var assessment = (Assessment with
        {
            WorkspacePath = assessmentWorkspace,
            OutputPath = assessmentOutput,
            IncludeSensitivePaths = false,
            JsonConsoleOutput = false,
            NonInteractive = true
        }).Normalize();

        return this with
        {
            Assessment = assessment,
            WorkspacePath = workspace,
            TargetWorkspacePath = targetWorkspace,
            OutputPath = output,
            StageAttemptId = StageAttemptId.Trim(),
            ExpectedSourceFingerprint = ExpectedSourceFingerprint.ToLowerInvariant(),
            PlanId = planId,
            ProducerVersion = ProducerVersion.Trim()
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

public sealed record CutoverSourceContainer(
    string Role,
    string ContainerId,
    string ContainerName,
    string Image,
    string ImageId,
    string State,
    string? Health,
    string RestartPolicy,
    bool WasRunning,
    Guid? StackId,
    Guid? ServiceId);

public sealed record CutoverRetainedContainer(
    string Role,
    string ContainerId,
    string ContainerName,
    string Image,
    string ImageId,
    string State,
    string RestartPolicy,
    string RetentionAction);

public sealed record CutoverStack(
    Guid SourceStackId,
    string Slug,
    string DisplayName,
    string MatrixServerName,
    Guid MatrixServiceId,
    string MatrixContainerId,
    Guid ElementServiceId,
    string ElementContainerId,
    string? MatrixPublicHost,
    string? ElementPublicHost);

public sealed record CutoverRouteHint(
    string Role,
    Guid? StackId,
    string? RouteId,
    string? PublicHost,
    string? ForwardHost,
    int? ForwardPort,
    string EvidenceKind);

public sealed record CutoverTargetEvidence(
    string StageAttemptId,
    string ImportAttemptId,
    string TargetProfileName,
    string TargetBaseUrl,
    string CatalogEntryId,
    string RestoreSessionId,
    string StagingId,
    DateTimeOffset StageCompletedAtUtc,
    bool PrivateOnly,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool PublishedRoutesAbsent,
    bool StagingDestroyed);

public sealed record CutoverPlanStep(
    int Order,
    string Code,
    string Description,
    bool MutatesSource,
    bool MutatesPublicRouting);

public sealed record CutoverPlanDocument(
    string Schema,
    int SchemaVersion,
    string PlanId,
    string ProducerVersion,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset ValidUntilUtc,
    string SourceAssessmentId,
    string SourceFingerprint,
    AssessmentClassification SourceClassification,
    bool SourceMutationOccurred,
    bool PublicRoutingMutationOccurred,
    CutoverSourceContainer[] SourceContainersToFreeze,
    CutoverRetainedContainer[] RetainedSourceContainers,
    CutoverStack[] Stacks,
    CutoverRouteHint[] RouteHints,
    CutoverTargetEvidence TargetEvidence,
    string[] Preconditions,
    CutoverPlanStep[] ExecutionSteps,
    CutoverPlanStep[] RollbackSteps,
    string[] Warnings,
    string[] NextSteps,
    bool DevelopmentExternalControlPlane = false);

public sealed record CutoverPreparationReport(
    string Schema,
    int SchemaVersion,
    string Status,
    string PlanHash,
    CutoverPlanDocument Plan,
    string JsonPath,
    string MarkdownPath);

public sealed record StoredCutoverPlanRun(
    string PlanId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string InputBindingSha256,
    string ExpectedSourceFingerprint,
    string StageAttemptId,
    string? SourceFingerprint,
    string? PlanHash,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);

public interface ICutoverPlanJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<StoredCutoverPlanRun?> GetAsync(
        string planId,
        CancellationToken cancellationToken);

    Task StartAsync(
        string planId,
        DateTimeOffset startedAtUtc,
        string inputBindingSha256,
        string expectedSourceFingerprint,
        string stageAttemptId,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        CutoverPreparationReport report,
        string reportJson,
        CancellationToken cancellationToken);

    Task FailAsync(
        string planId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}
