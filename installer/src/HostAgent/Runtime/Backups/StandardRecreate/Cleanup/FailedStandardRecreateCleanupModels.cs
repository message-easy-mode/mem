namespace HostAgent.Runtime.Backups.StandardRecreate.Cleanup;

/// <summary>
/// Read-only assessment for cleaning resources created by a Standard Recreate
/// that failed before it became a normal MEM runtime stack.
/// </summary>
public sealed record FailedStandardRecreateCleanupAssessment(
    string Source,
    string Status,
    string RecreateId,
    string CatalogEntryId,
    string? RestoreSessionId,
    string? RestoreAttemptStatus,
    Guid RuntimeStackId,
    string TargetStackSlug,
    bool RequiresCancelledRestore,
    bool CanCleanup,
    bool MatrixContainerPresent,
    bool ElementContainerPresent,
    string? MatrixRouteId,
    string? ElementRouteId,
    bool DatabaseProvisioned,
    string? DatabaseName,
    string? DatabaseUsername,
    string InstanceDirectoryPath,
    bool InstanceDirectoryPresent,
    string WorkspaceDirectoryPath,
    bool WorkspaceDirectoryPresent,
    IReadOnlyList<FailedStandardRecreateCleanupCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string Detail);

/// <summary>
/// Explicit acknowledgement required before partial failed-recreate resources
/// are removed. The operation never infers target names from the request.
/// </summary>
public sealed record FailedStandardRecreateCleanupRequest(
    string? Operator,
    bool AcknowledgeCleanup);

public sealed record FailedStandardRecreateCleanupResult(
    string Source,
    string Status,
    string CleanupId,
    Guid? RuntimeOperationId,
    string RecreateId,
    string CatalogEntryId,
    string? RestoreSessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    bool MatrixRouteRemoved,
    bool ElementRouteRemoved,
    bool MatrixContainerRemoved,
    bool ElementContainerRemoved,
    bool DatabaseDropped,
    bool InstanceDirectoryDeleted,
    bool WorkspaceDirectoryDeleted,
    FailedStandardRecreateCleanupAssessment Assessment,
    IReadOnlyList<FailedStandardRecreateCleanupStep> Steps,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string Detail);

public sealed record FailedStandardRecreateCleanupCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

public sealed record FailedStandardRecreateCleanupStep(
    string Code,
    string Status,
    string Message,
    IReadOnlyDictionary<string, string?> Data);

public sealed class FailedStandardRecreateCleanupConflictException : Exception
{
    public FailedStandardRecreateCleanupConflictException(
        FailedStandardRecreateCleanupConflictResponse conflict)
        : base(conflict.Detail)
    {
        Conflict = conflict;
    }

    public FailedStandardRecreateCleanupConflictResponse Conflict { get; }
}

public sealed record FailedStandardRecreateCleanupConflictResponse(
    string Code,
    string RecreateId,
    string? RestoreSessionId,
    string Detail);
