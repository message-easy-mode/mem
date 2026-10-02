namespace HostAgent.Runtime.Backups.RestoreAttempts.List;

/// <summary>
/// Browser-safe query contract for the canonical Restore Attempts table. Page
/// numbering is one-based at the HTTP boundary so browser URLs remain readable.
/// Restore attempts are always catalog-backed; validation receipts are
/// intentionally absent from this contract.
/// </summary>
public sealed record RestoreAttemptListQuery(
    int Page,
    int PageSize,
    string? Search,
    string? Status,
    string? TargetStack,
    string? SortBy,
    string? SortDirection);

/// <summary>
/// Aggregate counts calculated from the complete filtered result set before
/// paging. These values directly back the Restore Sessions overview cards.
/// </summary>
public sealed record RestoreAttemptListSummary(
    int TotalSessions,
    int ProductionRecreateCount,
    int PubliclyVerifiedCount,
    int NeedsActionCount);

/// <summary>
/// Canonical server-paginated Restore Attempt response for the operator
/// /restores screen. Host paths, raw evidence, credentials, Docker IDs, and
/// validation IDs are deliberately not exposed.
/// </summary>
public sealed record RestoreAttemptListResponse(
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
/// Compact durable table row. RestoreSessionId is the canonical workspace route
/// key. CatalogEntryId is intentionally null once permanent catalog deletion
/// has detached terminal restore history.
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
