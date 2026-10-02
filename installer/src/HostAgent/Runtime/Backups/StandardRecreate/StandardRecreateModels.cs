namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Operator request to create a real recovered MEM runtime stack from a
/// source-aware restore workspace. This is the normal supported production
/// recovery path for a managed Backup Catalog entry.
/// </summary>
public sealed record StandardRecreateRequest(
    string? TargetStackSlug,
    string? RequestedDomainId,
    string? MatrixHost,
    string? ElementHost,
    string? MatrixImage,
    string? ElementImage,
    string? Operator,
    string? Note,
    bool ExecuteProductionRecreate,
    bool AcknowledgeCreatesRealStack,
    bool AcknowledgeMutatesProductionPostgres,
    bool AcknowledgeMutatesNpmRoutes,
    bool AcknowledgeNoAutomaticRollback);

/// <summary>
/// Read-only request for a Standard Recreate availability assessment. The
/// canonical restore session id is supplied by the route, not this record.
/// </summary>
public sealed record StandardRecreatePreflightRequest(
    string? TargetStackSlug,
    string? RequestedDomainId,
    string? MatrixHost,
    string? ElementHost);

/// <summary>
/// Safe, operator-facing readiness assessment. A ready response never reserves
/// a target; execution repeats checks and claims the targets atomically.
/// </summary>
public sealed record StandardRecreatePreflightResponse(
    string Source,
    string Status,
    DateTimeOffset CheckedAtUtc,
    string RestoreSessionId,
    bool CanCreate,
    StandardRecreatePreflightTargets Targets,
    IReadOnlyList<StandardRecreatePreflightCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    string Detail)
{
    public StandardRecreateTurnSummary? Turn { get; init; }
}

public sealed record StandardRecreatePreflightTargets(
    string? TargetStackSlug,
    string? MatrixHost,
    string? ElementHost);

public sealed record StandardRecreatePreflightCheck(
    string Code,
    string Title,
    string State,
    string Message);

/// <summary>
/// Full recorded result of a standard production recreate operation.
/// </summary>
public sealed record StandardRecreateResult(
    string Source,
    string Status,
    string RecreateId,
    Guid RuntimeStackId,
    Guid MatrixInstanceId,
    Guid ElementInstanceId,
    string TargetStackSlug,
    string MatrixHost,
    string ElementHost,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string? Operator,
    string? Note,
    StandardRecreateDatabaseSummary Database,
    StandardRecreateRuntimeSummary Runtime,
    StandardRecreateRouteSummary Routes,
    StandardRecreateMutationSummary Mutations,
    IReadOnlyList<StandardRecreateCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string Detail,
    string CatalogEntryId)
{
    /// <summary>
    /// Standard Recreate is catalog-only. This field remains explicit in the
    /// result to make durable operation evidence self-describing.
    /// </summary>
    public string SourceKind => "backup-catalog";

    /// <summary>
    /// Safe, aggregate-only evidence for the MEM user projection recovered from
    /// the restored Synapse database. User identities are intentionally omitted.
    /// </summary>
    public StandardRecreateUserInventorySummary? UserInventory { get; init; }

    public StandardRecreateTurnSummary? Turn { get; init; }
}

public sealed record StandardRecreateTurnSummary(
    string Mode,
    string State,
    string Management,
    bool PlatformTurnRequired,
    bool? PlatformTurnReady,
    string? PublicHost,
    IReadOnlyList<string> TurnUris,
    string Detail);

public sealed record StandardRecreateUserInventorySummary(
    string Status,
    string? Source,
    int? UserCount,
    int? ActiveAdminCount,
    DateTimeOffset? SynchronizedAtUtc,
    string? ErrorCode);

/// <summary>
/// Database provisioning and import evidence for a recreated production stack.
/// </summary>
public sealed record StandardRecreateDatabaseSummary(
    bool Provisioned,
    string Host,
    int Port,
    string DatabaseName,
    string DatabaseUsername,
    bool ImportSucceeded,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    long? StateEventsCount);

/// <summary>
/// Runtime containers, files, networking, and persisted stack-registration evidence.
/// </summary>
public sealed record StandardRecreateRuntimeSummary(
    string MatrixContainerName,
    string? MatrixContainerId,
    bool MatrixStarted,
    bool MatrixHealthPassed,
    string? MatrixHealthResponse,
    string ElementContainerName,
    string? ElementContainerId,
    bool ElementStarted,
    bool ElementHealthPassed,
    string? ElementHealthResponse,
    string RuntimeNetworkName,
    string MatrixDataPath,
    string ElementDataPath,
    string HomeserverPath,
    string ElementConfigPath,
    bool ManifestSaved,
    bool DatabaseOwnershipSaved,
    bool StackRegistered);

/// <summary>
/// Public Matrix and Element route configuration and readiness evidence.
/// </summary>
public sealed record StandardRecreateRouteSummary(
    string MatrixPublicHost,
    string MatrixForwardHost,
    int MatrixForwardPort,
    string? MatrixRouteId,
    bool MatrixRouteReady,
    string ElementPublicHost,
    string ElementForwardHost,
    int ElementForwardPort,
    string? ElementRouteId,
    bool ElementRouteReady,
    bool PublicReadinessPassed);

/// <summary>
/// Explicit record of the production mutations performed during recreate.
/// </summary>
public sealed record StandardRecreateMutationSummary(
    bool RuntimeStackCreated,
    bool ProductionPostgresMutated,
    bool ProductionContainersTouched,
    bool NpmRoutesChanged,
    bool DnsChanged,
    bool CertificatesChanged,
    bool OldStacksDeleted,
    IReadOnlyList<string> Notes);

/// <summary>
/// One recorded safety, runtime, route, or readiness check.
/// </summary>
public sealed record StandardRecreateCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

/// <summary>
/// History inventory for standard production recreate operations.
/// </summary>
public sealed record StandardRecreateHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalRecreates,
    IReadOnlyList<StandardRecreateHistoryItem> Recreates,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Compact history row for one standard production recreate operation.
/// </summary>
public sealed record StandardRecreateHistoryItem(
    string RecreateId,
    string SourceKind,
    string CatalogEntryId,
    Guid RuntimeStackId,
    string TargetStackSlug,
    string MatrixHost,
    string ElementHost,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    bool StackRegistered,
    bool PublicReadinessPassed,
    int WarningCount,
    int ErrorCount,
    string Detail);

/// <summary>
/// Detailed read response for one standard production recreate operation.
/// </summary>
public sealed record StandardRecreateDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    StandardRecreateResult Recreate,
    IReadOnlyList<string> Warnings,
    string? Detail);