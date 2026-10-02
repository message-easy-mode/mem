namespace Modules.Operator.Migrations.Workspace;

/// <summary>
/// Stable read-only projection for the guided Migration Workspace. It composes
/// existing durable migration records without introducing a second workflow
/// entity or exposing secret-bearing evidence.
/// </summary>
public sealed record MigrationWorkspaceResponse(
    int SchemaVersion,
    MigrationWorkspaceIdentity Migration,
    MigrationWorkspaceSource Source,
    MigrationWorkspaceTarget Target,
    MigrationWorkspaceOverallStatus OverallStatus,
    IReadOnlyList<MigrationWorkspaceStage> Stages,
    MigrationWorkspaceOperationSummary? CurrentOperation,
    IReadOnlyList<MigrationWorkspaceActivityItem> Activity,
    MigrationWorkspaceVerification Verification,
    MigrationWorkspaceRetention Retention,
    MigrationWorkspaceEvidenceSummary Evidence,
    IReadOnlyList<MigrationWorkspaceAdvancedTool> AdvancedTools,
    IReadOnlyList<MigrationWorkspaceWarning> Warnings,
    MigrationWorkspaceGuidedState Guided);

public sealed record MigrationWorkspaceIdentity(
    string MigrationId,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MigrationWorkspaceSource(
    string Adapter,
    string DisplayName,
    string? Product,
    string? ProductVersion,
    string? MatrixServerName,
    long? UsersCount,
    long? RoomsCount,
    long? EventsCount,
    int BlockerCount,
    int WarningCount,
    int AdvisoryCount);

public sealed record MigrationWorkspaceTarget(
    bool PlanPrepared,
    string Status,
    string? StackSlug,
    string? DisplayName,
    string? MatrixHost,
    string? ElementHost);

public sealed record MigrationWorkspaceOverallStatus(
    string Code,
    string Severity,
    string CurrentStageCode,
    MigrationWorkspaceAction? NextAction);

public sealed record MigrationWorkspaceStage(
    string Code,
    string State,
    bool Unlocked,
    DateTimeOffset? CompletedAtUtc,
    MigrationWorkspaceAction? PrimaryAction,
    IReadOnlyList<MigrationWorkspaceAction> SecondaryActions,
    string SummaryCode,
    IReadOnlyList<MigrationWorkspaceProblem> Problems,
    MigrationWorkspaceStageEvidenceSummary EvidenceSummary,
    MigrationWorkspaceOperationSummary? OperationSummary,
    MigrationWorkspaceFailureOutcome? FailureOutcome);

public sealed record MigrationWorkspaceAction(
    string Code,
    bool Enabled,
    string RelatedStage);

public sealed record MigrationWorkspaceProblem(
    string Code,
    string Severity,
    string? Detail);

public sealed record MigrationWorkspaceStageEvidenceSummary(
    int ItemCount,
    DateTimeOffset? LatestOccurredAtUtc,
    string? LatestStatus);

public sealed record MigrationWorkspaceOperationSummary(
    string OperationId,
    string Operation,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record MigrationWorkspaceFailureOutcome(
    string FailureCode,
    string FailedComponent,
    string MutationState,
    string CompensationState,
    string ObservedCurrentState,
    bool SafeToRetry,
    string SafeStateSummaryCode,
    MigrationWorkspaceAction? NextAction);

public sealed record MigrationWorkspaceActivityItem(
    string Code,
    string Status,
    DateTimeOffset OccurredAtUtc,
    string? RelatedObjectId);

public sealed record MigrationWorkspaceVerification(
    string Status,
    bool HasRun,
    bool? AllPassed,
    int CheckCount,
    int FailedCheckCount,
    DateTimeOffset? CheckedAtUtc);

public sealed record MigrationWorkspaceRetention(
    string Status,
    DateTimeOffset? RetainUntilUtc,
    bool? AutomaticDeletionAllowed);

public sealed record MigrationWorkspaceEvidenceSummary(
    IReadOnlyList<MigrationWorkspaceEvidenceCategory> Categories);

public sealed record MigrationWorkspaceEvidenceCategory(
    string Code,
    string Status,
    int ItemCount,
    DateTimeOffset? LatestOccurredAtUtc);

public sealed record MigrationWorkspaceAdvancedTool(
    string Code,
    string Availability,
    string? ReasonUnavailable,
    string RelatedStage);

public sealed record MigrationWorkspaceWarning(
    string Code,
    string Severity,
    string? Detail);

public static class MigrationWorkspaceStageCodes
{
    public const string CreateAndUploadPackage = "create-and-upload-package";
    public const string ReviewOldServer = "review-old-server";
    public const string PrepareAndTest = "prepare-and-test";
    public const string CreateNewServer = "create-new-server";
    public const string MakeNewServerLive = "make-new-server-live";
    public const string FinishMigration = "finish-migration";

    public static readonly IReadOnlyList<string> Ordered =
    [
        CreateAndUploadPackage,
        ReviewOldServer,
        PrepareAndTest,
        CreateNewServer,
        MakeNewServerLive,
        FinishMigration,
    ];
}

public static class MigrationWorkspaceStageStates
{
    public const string NotStarted = "not-started";
    public const string Ready = "ready";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string ActionRequired = "action-required";
    public const string Closed = "closed";
}

/// <summary>
/// Presentation authority for the guided journey. Specialist endpoints remain
/// evidence/command APIs; clients must not combine them to decide progression.
/// Revision is a content token, not a durable write or a timestamp. Operation
/// revisions deliberately exclude unrelated work, allowing a lost response to
/// be reconciled without replaying the command or guessing from a global change.
/// </summary>
public sealed record MigrationWorkspaceGuidedState(
    string Revision,
    string CurrentStageCode,
    string StageState,
    MigrationWorkspaceOperationSummary? CurrentOperation,
    MigrationWorkspaceAction? NextAction,
    MigrationWorkspaceProblem? Blocker,
    string UncertaintyState,
    MigrationSessionDetailDto Detail,
    bool ProductionAuthorized,
    string? ProductionAuthorityType,
    string? ConversionAttemptId,
    string? ConversionStatus,
    bool HasVerifiedCandidate,
    string? StagingRunId,
    string? StagingStatus,
    bool StagingRetained,
    string? AdoptionPlanId,
    bool PrivateRuntimeReady,
    string? CutoverPreviewId,
    string? CutoverPreviewStatus,
    bool PublicRoutesCreated,
    bool RuntimePromotionCompleted,
    bool Accepted,
    string? AcceptanceId,
    string BaselineBackupStatus,
    string? BaselineBackupId,
    string? BaselineCatalogEntryId,
    IReadOnlyDictionary<string, string> OperationRevisions,
    MigrationSessionCancellationDto? Cancellation = null);
