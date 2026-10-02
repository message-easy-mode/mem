namespace Mem.Cli.Models;

/// <summary>
/// Optional operator filters for the canonical Restore Attempt inventory.
/// Page numbering is one-based at the HTTP boundary.
/// </summary>
public sealed record RestoreAttemptListRequest(
    int? Page = null,
    int? PageSize = null,
    string? Search = null,
    string? Status = null,
    string? TargetStack = null,
    string? SortBy = null,
    string? SortDirection = null);

public sealed record RestoreAttemptListQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    string? TargetStack,
    string? SortBy,
    string? SortDirection);

public sealed record RestoreAttemptListSummary(
    int TotalSessions,
    int ProductionRecreateCount,
    int PubliclyVerifiedCount,
    int NeedsActionCount);

/// <summary>
/// Safe, compact server projection for one durable Restore Attempt. The
/// restoreSessionId is the canonical workspace identity. A deleted source is
/// represented by SourceDeleted=true and a null CatalogEntryId.
/// </summary>
public sealed record RestoreAttemptListItem(
    string RestoreSessionId,
    bool WorkspaceAvailable,
    string SourceKind,
    string SourceLabel,
    bool SourceDeleted,
    string? CatalogEntryId,
    string? SourceStackSlug,
    string? SourceBackupId,
    string? TargetStackSlug,
    string Status,
    string StatusLabel,
    string CurrentStage,
    string CurrentStageLabel,
    int? ProgressPercent,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastUpdatedAtUtc,
    DateTimeOffset? TerminalAtUtc,
    int WarningCount,
    int ErrorCount,
    bool ProductionRecreateStarted,
    bool PubliclyVerified,
    string NextActionCode,
    string NextActionTitle,
    string Detail);

public sealed record RestoreAttemptListApiResponse(
    string Source,
    string Status,
    RestoreAttemptListQuery Query,
    RestoreAttemptListSummary Summary,
    int TotalSessions,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<string> TargetStacks,
    IReadOnlyList<RestoreAttemptListItem> Sessions,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Stable CLI projection for the canonical Restore Attempt list. It contains
/// only server-projected, browser-safe restore metadata.
/// </summary>
public sealed record RestoreAttemptListCliResult(
    string Source,
    string Status,
    RestoreAttemptListQuery Query,
    RestoreAttemptListSummary Summary,
    int TotalSessions,
    int Page,
    int PageSize,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyList<string> TargetStacks,
    IReadOnlyList<RestoreAttemptListItem> Sessions,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RestoreWorkspaceCliResult(
    string Source,
    string Status,
    RestoreWorkspaceResponse? Workspace,
    string? Detail);

/// <summary>
/// Stable read model for the canonical Restore Workspace. This mirrors the
/// HostAgent's safe projection and deliberately has no validation identifier,
/// payload path, credential, Docker identifier, or unrestricted raw logs.
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
    public RestoreWorkspacePrivateTestEvidence? PrivateTestEvidence { get; init; }
}

public sealed record RestoreWorkspaceStageEvidenceSummary(
    int ItemCount,
    DateTimeOffset? LatestOccurredAtUtc,
    string? LatestStatus);

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
    DateTimeOffset? CompletedAtUtc);

public sealed record RestoreWorkspaceAdvancedTool(
    string Code,
    string Title,
    string Availability,
    string? ReasonUnavailable,
    string RelatedStage);

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

/// <summary>
/// Narrow evidence-only CLI result derived from the canonical workspace
/// projection. This lets scripts request curated evidence without treating a
/// filesystem location or uploaded ZIP as restore identity.
/// </summary>
public sealed record RestoreWorkspaceEvidenceCliResult(
    string Source,
    string Status,
    string? RestoreSessionId,
    RestoreWorkspaceSource? WorkspaceSource,
    RestoreWorkspaceEvidenceSummary? Evidence,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RestoreLogListRequest(
    int? Page = null,
    int? PageSize = null,
    string? Severity = null,
    string? Stage = null,
    string? Search = null);

public sealed record RestoreLogEvent(
    int SchemaVersion,
    string EventId,
    DateTimeOffset TimestampUtc,
    string RestoreSessionId,
    Guid? OperationId,
    string Stage,
    string Severity,
    string EventCode,
    string Message,
    Dictionary<string, string>? Details);

public sealed record RestoreLogSummary(
    int TotalEvents,
    int WarningCount,
    int ErrorCount,
    RestoreLogEvent? LatestEvent,
    RestoreLogEvent? LatestWarningOrError);

public sealed record RestoreLogPage(
    string RestoreSessionId,
    int Page,
    int PageSize,
    int TotalEvents,
    int TotalPages,
    RestoreLogSummary Summary,
    IReadOnlyList<RestoreLogEvent> Events,
    IReadOnlyList<string> Warnings);

public sealed record RestoreLogPageCliResult(
    string Source,
    string Status,
    RestoreLogPage? Logs,
    string? Detail);

/// <summary>
/// Curated redacted support payload. The server contract deliberately excludes
/// database dumps, configuration files, credentials, private keys, and raw
/// unrestricted logs.
/// </summary>
public sealed record RestoreSupportReportApiResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string MemVersion,
    string RestoreSessionId,
    RestoreSupportReportAttempt Attempt,
    RestoreSupportReportSource Source,
    RestoreSupportReportTarget Target,
    RestoreLogSummary Logs,
    IReadOnlyList<RestoreSupportReportOperation> Operations,
    IReadOnlyList<RestoreLogEvent> RecentEvents,
    IReadOnlyList<string> Warnings);

public sealed record RestoreSupportReportSource(
    string SourceKind,
    string SourceKey,
    string? CatalogEntryId,
    string SourceDisplayName,
    string SourceOriginKind,
    string? SourceStackSlug,
    string? SourceBackupId,
    bool SourceDeleted);

public sealed record RestoreSupportReportTarget(
    string? TargetStackSlug,
    string? MatrixHost,
    string? ElementHost);

public sealed record RestoreSupportReportAttempt(
    string Status,
    string CurrentStage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? TerminalAtUtc,
    string? LastErrorCode,
    string? LastErrorSummary,
    int WarningCount,
    int ErrorCount);

public sealed record RestoreSupportReportOperation(
    Guid OperationId,
    string Operation,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError);

public sealed record RestoreSupportReportCliProjection(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string MemVersion,
    string RestoreSessionId,
    RestoreSupportReportAttempt Attempt,
    RestoreSupportReportCliSource Source,
    RestoreSupportReportTarget Target,
    RestoreLogSummary Logs,
    IReadOnlyList<RestoreSupportReportOperation> Operations,
    IReadOnlyList<RestoreLogEvent> RecentEvents,
    IReadOnlyList<string> Warnings);

/// <summary>
/// CLI-safe source projection for the redacted report. The server's internal
/// source key is intentionally omitted; restoreSessionId and catalogEntryId
/// remain the public operator identities.
/// </summary>
public sealed record RestoreSupportReportCliSource(
    string SourceKind,
    string? CatalogEntryId,
    string SourceDisplayName,
    string SourceOriginKind,
    string? SourceStackSlug,
    string? SourceBackupId,
    bool SourceDeleted);

public sealed record RestoreSupportReportCliResult(
    string Source,
    string Status,
    RestoreSupportReportCliProjection? Report,
    string? Detail);
