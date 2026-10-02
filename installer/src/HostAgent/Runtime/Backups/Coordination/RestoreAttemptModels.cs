namespace HostAgent.Runtime.Backups.Coordination;

public static class RestoreAttemptStatuses
{
    public const string Creating = "creating";
    public const string Ready = "ready";
    public const string Testing = "testing";
    public const string Planning = "planning";
    public const string Recreating = "recreating";
    public const string Verifying = "verifying";
    public const string NeedsAttention = "needs-attention";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
    public const string Abandoned = "abandoned";
    public const string Superseded = "superseded";

    public static bool IsTerminal(string status) =>
        string.Equals(status, Completed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Cancelled, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Abandoned, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Superseded, StringComparison.OrdinalIgnoreCase);
}

public static class RestoreAttemptStages
{
    public const string Creating = "creating";
    public const string PreparingSource = "preparing-source";
    public const string BackupReady = "backup-ready";
    public const string PrivateTest = "private-test";
    public const string CreateRestoredChatServer = "create-restored-chat-server";
    public const string PublicVerification = "public-verification";
    public const string NeedsAttention = "needs-attention";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// Immutable catalog-backed source identity for a new restore attempt. Uploaded
/// ZIP validation remains an ingestion concern and is intentionally absent.
/// </summary>
public sealed record RestoreAttemptSourceIdentity(
    string SourceKind,
    string SourceKey,
    Guid BackupCatalogEntryId,
    string CatalogEntryId,
    string DisplayName,
    string OriginKind,
    string? SourceStackSlug,
    string? SourceBackupId);

public sealed record RestoreAttemptSnapshot(
    Guid Id,
    string RestoreSessionId,
    string SourceKind,
    string SourceKey,
    string CatalogEntryId,
    string SourceDisplayName,
    string SourceOriginKind,
    string? SourceStackSlug,
    string? SourceBackupId,
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
    string SessionDirectoryPath,
    string? LogDirectoryPath,
    string? SupportReportPath,
    Guid? RuntimeOperationId,
    Guid? BackupCatalogEntryId = null);

public sealed record RestoreAttemptGetOrCreateResult(
    RestoreAttemptSnapshot Attempt,
    bool Created,
    bool Resumed);


public sealed record RestoreAttemptReconciliationItem(
    string RestoreSessionId,
    string Status,
    string CurrentStage,
    bool WorkspaceExists,
    bool RequiresAttention,
    string Detail);

public sealed record RestoreAttemptReconciliationReport(
    DateTimeOffset ReconciledAtUtc,
    IReadOnlyList<RestoreAttemptReconciliationItem> Attempts,
    IReadOnlyList<string> Warnings);

public sealed class RestoreSourcePreparationInProgressException : InvalidOperationException
{
    public RestoreSourcePreparationInProgressException(
        string restoreSessionId,
        string message)
        : base(message)
    {
        RestoreSessionId = restoreSessionId;
    }

    public string RestoreSessionId { get; }
}

public sealed record RestoreAttemptConflictResponse(
    string Code,
    string RestoreSessionId,
    string ResourceType,
    string ResourceValue,
    string Detail);

/// <summary>
/// Target resource categories reserved by an active restore attempt.
/// </summary>
public static class RestoreTargetResourceTypes
{
    public const string StackSlug = "stack-slug";
    public const string MatrixHost = "matrix-host";
    public const string ElementHost = "element-host";
}

/// <summary>
/// Canonical target resource supplied to the coordination layer. ResourceValue
/// is safe to show to an operator; ActiveClaimKey remains internal storage
/// detail and may use a broader collision domain for public host names.
/// </summary>
public sealed record RestoreTargetClaimResource(
    string ResourceType,
    string ResourceValue,
    string ActiveClaimKey);

public sealed record RestoreTargetClaimSnapshot(
    Guid Id,
    string ResourceType,
    string ResourceValue,
    string? ActiveClaimKey,
    DateTimeOffset ClaimedAtUtc,
    DateTimeOffset? ReleasedAtUtc,
    string? ReleaseReason);

/// <summary>
/// Read-only assessment of whether a canonical restore attempt can reserve the
/// requested Standard Recreate targets right now. This never creates claims,
/// changes attempt state, or starts an operation. The create command always
/// repeats these checks and acquires claims atomically.
/// </summary>
public sealed record RestoreStandardRecreateTargetAssessment(
    RestoreAttemptSnapshot Attempt,
    IReadOnlyList<RestoreTargetClaimResource> Resources,
    IReadOnlyList<RestoreStandardRecreateTargetAssessmentCheck> Checks,
    IReadOnlyList<string> Blockers)
{
    public bool CanCreate => Checks.All(check => check.Passed);
}

public sealed record RestoreStandardRecreateTargetAssessmentCheck(
    string Code,
    bool Passed,
    string Message);

/// <summary>
/// Durable reservation created immediately before Standard Recreate begins its
/// first real host mutation. It links the target claims and runtime operation
/// to the restore attempt in one SQLite transaction.
/// </summary>
public sealed record RestoreStandardRecreateReservation(
    RestoreAttemptSnapshot Attempt,
    Guid RuntimeOperationId,
    IReadOnlyList<RestoreTargetClaimSnapshot> Claims);

/// <summary>
/// Durable operation reservation for an isolated private restore test. The
/// linked runtime operation prevents concurrent private-test runs for the same
/// canonical restore attempt.
/// </summary>
public sealed record RestorePrivateTestReservation(
    RestoreAttemptSnapshot Attempt,
    Guid RuntimeOperationId);

/// <summary>
/// Curated private-test outcome retained with the restore operation and safe to
/// project into workspace evidence. Host paths, Docker IDs, runtime logs, and
/// credentials are deliberately excluded.
/// </summary>
public sealed record RestorePrivateTestEvidence(
    string SourceKind,
    string? CatalogEntryId,
    string? StagingId,
    string? MatrixServerName,
    string Status,
    bool PrivateOnly,
    bool DockerNetworkInternal,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool RequiresExplicitDestroy);

/// <summary>
/// Machine-readable conflict for a competing active restore, a live runtime
/// resource, or an already-running operation on this restore attempt.
/// </summary>
public sealed class RestoreAttemptConflictException : InvalidOperationException
{
    public RestoreAttemptConflictException(RestoreAttemptConflictResponse conflict)
        : base(conflict.Detail)
    {
        Conflict = conflict;
    }

    public RestoreAttemptConflictResponse Conflict { get; }
}
