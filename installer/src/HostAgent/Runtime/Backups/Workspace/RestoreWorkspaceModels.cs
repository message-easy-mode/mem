namespace HostAgent.Runtime.Backups.Workspace;

/// <summary>
/// Stable read model for the Restore Workspace screen. This is a projection of
/// durable attempt, claim, operation, evidence, and log state; it does not
/// create resources or decide whether a new target is globally available.
/// </summary>
public sealed record RestoreWorkspaceResponse(
    int SchemaVersion,
    string RestoreSessionId,
    RestoreWorkspaceAttempt Attempt,
    RestoreWorkspaceSource Source,
    RestoreWorkspaceTarget Target,
    RestoreWorkspaceOverallStatus OverallStatus,
    IReadOnlyList<RestoreWorkspaceStandardStage> StandardStages,
    IReadOnlyList<RestoreWorkspaceAdvancedTool> AdvancedTools,
    RestoreWorkspaceVerification Verification,
    RestoreWorkspaceEvidenceSummary Evidence,
    RestoreWorkspaceLogsSummary Logs,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Explicit, server-projected cancellation capability for this durable
    /// workspace. Callers must not infer cancellation from source identity or
    /// a client-side status guess; the endpoint repeats the safety check at
    /// mutation time.
    /// </summary>
    public RestoreWorkspaceCancellation? Cancellation { get; init; }
}

public sealed record RestoreWorkspaceAttempt(
    Guid Id,
    string Status,
    string CurrentStage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? TerminalAtUtc,
    DateTimeOffset? LastEventAtUtc,
    string? LastErrorCode,
    string? LastErrorSummary,
    int WarningCount,
    int ErrorCount,
    Guid? CurrentOperationId);

public sealed record RestoreWorkspaceSource(
    string Kind,
    string? StackSlug,
    string? BackupId,
    DateTimeOffset? CreatedAtUtc,
    long? SizeBytes,
    string? MatrixHost,
    string? ElementHost,
    string ValidationStatus,
    string ValidationSummary,
    string? CatalogEntryId,
    string SourceDisplayName,
    string SourceOriginKind,
    bool SourceDeleted);

public sealed record RestoreWorkspaceTarget(
    string? StackSlug,
    string? MatrixHost,
    string? ElementHost,
    string Availability,
    string Detail,
    IReadOnlyList<RestoreWorkspaceTargetClaim> Claims);

public sealed record RestoreWorkspaceTargetClaim(
    string ResourceType,
    string ResourceValue,
    string Status,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleaseReason);

public sealed record RestoreWorkspaceOverallStatus(
    string Code,
    string Title,
    string Description,
    string Severity,
    RestoreWorkspaceAction? NextAction);

/// <summary>
/// Server-projected guard for cancelling a non-terminal restore workspace.
/// Cancellation releases temporary restore target claims and preserves audit
/// history; it never deletes a completed runtime stack or source backup.
/// </summary>
public sealed record RestoreWorkspaceCancellation(
    bool CanCancel,
    string? ReasonUnavailable,
    string Summary);

public sealed record RestoreWorkspaceAction(
    string Code,
    string Title,
    string Description,
    bool Enabled,
    string? RelatedStage);

public sealed record RestoreWorkspaceStandardStage(
    string Code,
    string Title,
    string Description,
    string State,
    bool Required,
    bool Unlocked,
    DateTimeOffset? CompletedAtUtc,
    RestoreWorkspaceAction? PrimaryAction,
    IReadOnlyList<RestoreWorkspaceAction> SecondaryActions,
    string Summary,
    IReadOnlyList<string> Blockers,
    RestoreWorkspaceStageEvidenceSummary EvidenceSummary,
    RestoreWorkspaceOperationSummary? OperationSummary)
{
    /// <summary>
    /// Curated execution evidence for the canonical private restore test. This
    /// is intentionally populated only for the private-test stage; it excludes
    /// host paths, Docker identifiers, container names, credentials, and raw
    /// runtime logs.
    /// </summary>
    public RestoreWorkspacePrivateTestEvidence? PrivateTestEvidence { get; init; }
}

public sealed record RestoreWorkspaceStageEvidenceSummary(
    int ItemCount,
    DateTimeOffset? LatestOccurredAtUtc,
    string? LatestStatus);

/// <summary>
/// Browser-safe, durable evidence from the latest private restore-test
/// operation. The historical result is retained after staging teardown; live
/// staging lifecycle fields are refreshed from the private-staging history.
/// </summary>
public sealed record RestoreWorkspacePrivateTestEvidence(
    string SourceKind,
    string? CatalogEntryId,
    string? StagingId,
    string? MatrixServerName,
    string Status,
    bool PrivateOnly,
    bool DockerNetworkInternal,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool RequiresExplicitDestroy,
    DateTimeOffset? CompletedAtUtc,
    string StagingRuntimeStatus,
    bool? StagingRuntimeDestroyed,
    bool? DestroyAvailable,
    DateTimeOffset? DestroyedAtUtc);

public sealed record RestoreWorkspaceOperationSummary(
    Guid OperationId,
    string Operation,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc)
{
    /// <summary>
    /// Latest durable restore-scoped activity correlated to this operation.
    /// This is sourced from the structured restore event stream and lets the UI
    /// distinguish a long-running operation that is still advancing from one
    /// that has produced no recent progress evidence.
    /// </summary>
    public DateTimeOffset? LastActivityAtUtc { get; init; }

    /// <summary>
    /// Runtime-operation attempt number. Standard Recreate currently starts a
    /// new durable operation for a deliberate retry, but exposing the persisted
    /// attempt count keeps the progress projection honest if that evolves.
    /// </summary>
    public int AttemptNumber { get; init; } = 1;
}

public sealed record RestoreWorkspaceAdvancedTool(
    string Code,
    string Title,
    string Availability,
    string? ReasonUnavailable,
    string RelatedStage);

/// <summary>
/// Safe projection of the latest explicit public server check for this restored
/// stack. This deliberately excludes URLs, response bodies, command output, and
/// raw infrastructure diagnostics.
/// </summary>
public sealed record RestoreWorkspaceVerification(
    string Status,
    bool HasRun,
    bool? AllPassed,
    DateTimeOffset? CheckedAtUtc,
    IReadOnlyList<RestoreWorkspaceVerificationCheck> Checks,
    string Summary);

public sealed record RestoreWorkspaceVerificationCheck(
    string Code,
    string Title,
    string Status);

public sealed record RestoreWorkspaceEvidenceSummary(
    IReadOnlyList<RestoreWorkspaceEvidenceCategory> Categories,
    RestoreWorkspaceEvidenceItem? LatestFailure,
    RestoreWorkspaceEvidenceItem? LatestSuccess);

public sealed record RestoreWorkspaceEvidenceCategory(
    string Code,
    string Title,
    string Status,
    int ItemCount,
    DateTimeOffset? LatestOccurredAtUtc,
    IReadOnlyList<RestoreWorkspaceEvidenceItem> Items);

public sealed record RestoreWorkspaceEvidenceItem(
    string Code,
    string Category,
    string Title,
    string Status,
    DateTimeOffset OccurredAtUtc,
    string? EventCode,
    Guid? OperationId,
    string? Stage,
    string Description);

public sealed record RestoreWorkspaceLogsSummary(
    int TotalEvents,
    int WarningCount,
    int ErrorCount,
    RestoreWorkspaceLogEventSummary? LatestEvent,
    RestoreWorkspaceLogEventSummary? LatestWarningOrError,
    bool SupportReportAvailable,
    bool SupportBundleAvailable,
    IReadOnlyList<string> Warnings);

public sealed record RestoreWorkspaceLogEventSummary(
    DateTimeOffset TimestampUtc,
    string Stage,
    string Severity,
    string EventCode,
    string Message,
    Guid? OperationId);

public static class RestoreWorkspaceStageStates
{
    public const string NotStarted = "not-started";
    public const string Ready = "ready";
    public const string Optional = "optional";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
}
