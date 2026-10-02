namespace HostAgent.Runtime.Backups.AdvancedCutover.Candidate;

public sealed record RuntimeStackBackupProductionCandidateRequest(
    bool KeepOnFailure,
    string? PostgresImage,
    string? SynapseImage,
    string? ElementImage,
    string? TargetStackSlug,
    string? RestoreMode);

public sealed record RuntimeStackBackupProductionCandidateResult(
    string Source,
    string Status,
    string Mode,
    string CandidateId,
    string ValidationId,
    string RestoreMode,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string TargetStackSlug,
    string? MatrixServerName,
    string PrivateRuntimeId,
    string PrivateRuntimeStatus,
    string WorkspacePath,
    string RuntimePath,
    string MatrixDataPath,
    string? ElementDataPath,
    string NetworkName,
    string? NetworkId,
    string PostgresContainerName,
    string? PostgresContainerId,
    string SynapseContainerName,
    string? SynapseContainerId,
    string? ElementContainerName,
    string? ElementContainerId,
    string? ElementImage,
    string DatabaseName,
    string DatabaseUser,
    RuntimeStackBackupProductionCandidateSafetySummary Safety,
    RuntimeStackBackupProductionCandidateDatabaseSummary Database,
    RuntimeStackBackupProductionCandidateRuntimeSummary Runtime,
    IReadOnlyList<RuntimeStackBackupProductionCandidateCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    RuntimeStackBackupProductionCandidateDestroySummary? Destroy,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null,
    string? RestoreSessionId = null);

public sealed record RuntimeStackBackupProductionCandidateSafetySummary(
    bool PrivateOnly,
    bool DockerNetworkInternal,
    bool PublicRoutesCreated,
    bool DnsChanged,
    bool CertificatesChanged,
    bool NpmRoutesChanged,
    bool ProductionContainersTouched,
    bool ProductionDatabasesTouched,
    bool PublicCutoverPerformed,
    bool ProductionExecutionLocked,
    bool RequiresExplicitDestroy,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionCandidateDatabaseSummary(
    bool ImportSucceeded,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    long? StateEventsCount);

public sealed record RuntimeStackBackupProductionCandidateRuntimeSummary(
    bool HomeserverConfigExtracted,
    bool HomeserverConfigPatched,
    bool SigningKeyExtracted,
    bool MediaStoreExtracted,
    long MediaFiles,
    long MediaBytes,
    bool ElementConfigExtracted,
    bool ElementConfigPatched,
    bool ElementContainerStarted,
    bool ElementHealthPassed,
    string? ElementHealthResponse,
    string? ElementLogsTail,
    bool PostgresContainerStarted,
    bool SynapseContainerStarted,
    bool SynapseHealthPassed,
    string? HealthResponse,
    string? SynapseLogsTail);

public sealed record RuntimeStackBackupProductionCandidateCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

public sealed record RuntimeStackBackupProductionCandidateDestroySummary(
    DateTimeOffset DestroyedAtUtc,
    bool PrivateRuntimeDestroyed,
    bool ElementContainerRemoved,
    IReadOnlyList<string> Warnings);

public sealed record RuntimeStackBackupProductionCandidateHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalCandidates,
    IReadOnlyList<RuntimeStackBackupProductionCandidateSummary> Candidates,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupProductionCandidateDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    RuntimeStackBackupProductionCandidateResult? Candidate,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupProductionCandidateSummary(
    string CandidateId,
    string ValidationId,
    string RestoreMode,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    double? DurationSeconds,
    string TargetStackSlug,
    string? MatrixServerName,
    string PrivateRuntimeId,
    string PrivateRuntimeStatus,
    string PostgresContainerName,
    string SynapseContainerName,
    string? ElementContainerName,
    string? NetworkName,
    bool ImportSucceeded,
    bool SynapseHealthPassed,
    bool ElementHealthPassed,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    bool Destroyed,
    int WarningCount,
    int ErrorCount,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null,
    string? RestoreSessionId = null);


/// <summary>
/// Response for a private Production Restore candidate created from the
/// canonical Backup Catalog. The candidate is private-only; it is not an
/// authorization to perform a public cutover.
/// </summary>
public sealed record CatalogProductionCandidateResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    bool CandidateCreated,
    bool CandidateResumed,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    RuntimeStackBackupProductionCandidateResult Candidate,
    string Detail);

/// <summary>
/// Explicit input for a catalog-native private Production Restore candidate.
/// Source material always comes from catalogEntryId in the route, never from
/// a validation receipt or an uploaded-ZIP storage path.
/// </summary>
public sealed record CatalogProductionCandidateRequest(
    bool? KeepOnFailure,
    string? PostgresImage,
    string? SynapseImage,
    string? ElementImage,
    string? TargetStackSlug,
    string? RestoreMode)
{
    public RuntimeStackBackupProductionCandidateRequest ToCandidateRequest() =>
        new(
            KeepOnFailure: KeepOnFailure ?? false,
            PostgresImage: PostgresImage,
            SynapseImage: SynapseImage,
            ElementImage: ElementImage,
            TargetStackSlug: TargetStackSlug,
            RestoreMode: RestoreMode);
}
