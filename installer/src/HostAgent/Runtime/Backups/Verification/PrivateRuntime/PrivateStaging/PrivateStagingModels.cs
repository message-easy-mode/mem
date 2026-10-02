namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

/// <summary>
/// Operator options for creating a retained, private recovered runtime.
/// This environment is isolated evidence and must not become public production infrastructure.
/// </summary>
public sealed record PrivateStagingRunRequest(
    bool KeepOnFailure,
    string? PostgresImage,
    string? SynapseImage,
    string? TargetStackSlug,
    bool RequireElementRuntime = false,
    bool RequireApprovedRuntimeImages = false);

/// <summary>
/// Full recorded result of one private staging run.
/// </summary>
public sealed record PrivateStagingRunResult(
    string Source,
    string Status,
    string Mode,
    string StagingId,
    string? ValidationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? UploadedZipPath,
    string WorkspacePath,
    string RuntimePath,
    string MatrixDataPath,
    string? ElementDataPath,
    string DatabaseDumpPath,
    string NetworkName,
    string? NetworkId,
    string PostgresContainerName,
    string? PostgresContainerId,
    string SynapseContainerName,
    string? SynapseContainerId,
    string PostgresImage,
    string SynapseImage,
    string DatabaseName,
    string DatabaseUser,
    string? MatrixServerName,
    string TargetStackSlug,
    PrivateStagingSafetySummary Safety,
    PrivateStagingDatabaseSummary Database,
    PrivateStagingRuntimeSummary Runtime,
    IReadOnlyList<PrivateStagingCheck> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    PrivateStagingDestroySummary? Destroy,
    string? Detail,
    string? SourceKind = null,
    string? CatalogEntryId = null,
    string? SynapseApprovedReference = null,
    string? ElementContainerName = null,
    string? ElementContainerId = null,
    string? ElementImage = null,
    string? ElementApprovedReference = null,
    string? ElementConfigSha256 = null);

/// <summary>
/// Explicit evidence that private staging remained isolated from public production systems.
/// </summary>
public sealed record PrivateStagingSafetySummary(
    bool PrivateOnly,
    bool DockerNetworkInternal,
    bool PublicRoutesCreated,
    bool DnsChanged,
    bool CertificatesChanged,
    bool ProductionContainersTouched,
    bool ProductionDatabasesTouched,
    bool RequiresExplicitDestroy,
    IReadOnlyList<string> Notes);

/// <summary>
/// Database import and Synapse schema evidence captured in private staging.
/// </summary>
public sealed record PrivateStagingDatabaseSummary(
    bool ImportSucceeded,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    long? StateEventsCount,
    string? DumpFormat = null,
    string? ImportMechanism = null);

/// <summary>
/// Recovered Matrix and Element runtime preparation, startup, and health evidence.
/// </summary>
public sealed record PrivateStagingRuntimeSummary(
    bool HomeserverConfigExtracted,
    bool HomeserverConfigPatched,
    bool SigningKeyExtracted,
    bool MediaStoreExtracted,
    long MediaFiles,
    long MediaBytes,
    bool ElementConfigExtracted,
    bool PostgresContainerStarted,
    bool SynapseContainerStarted,
    bool SynapseHealthPassed,
    string? HealthResponse,
    string? SynapseLogsTail,
    bool ElementConfigPatched = false,
    bool ElementContainerStarted = false,
    bool ElementHealthPassed = false,
    bool ElementSynapseConnectivityPassed = false,
    bool ElementNetworkAttached = false,
    string? ElementHealthResponse = null,
    string? ElementLogsTail = null);

/// <summary>
/// One safety, extraction, import, startup, or health check recorded during private staging.
/// </summary>
public sealed record PrivateStagingCheck(
    string Code,
    string Severity,
    bool Passed,
    string Message,
    string? Detail);

/// <summary>
/// Explicit destruction outcome for a retained private staging environment.
/// </summary>
public sealed record PrivateStagingDestroySummary(
    DateTimeOffset DestroyedAtUtc,
    bool SynapseContainerRemoved,
    bool PostgresContainerRemoved,
    bool NetworkRemoved,
    bool WorkspaceRemoved,
    IReadOnlyList<string> Warnings,
    bool ElementContainerRemoved = false);

/// <summary>
/// History inventory for private staging runs.
/// </summary>
public sealed record PrivateStagingHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalRuns,
    IReadOnlyList<PrivateStagingRunSummary> Runs,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Detailed read response for one private staging run.
/// </summary>
public sealed record PrivateStagingRunDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    PrivateStagingRunResult? Run,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Compact history row for one retained private staging environment.
/// </summary>
public sealed record PrivateStagingRunSummary(
    string StagingId,
    string? ValidationId,
    string Mode,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    double? DurationSeconds,
    string? UploadedZipName,
    string? UploadedZipPath,
    string TargetStackSlug,
    string? MatrixServerName,
    string PostgresContainerName,
    string SynapseContainerName,
    string? NetworkName,
    bool ImportSucceeded,
    bool SynapseHealthPassed,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    bool Destroyed,
    int WarningCount,
    int ErrorCount,
    string? Detail,
    string? SourceKind = null,
    string? CatalogEntryId = null);

/// <summary>
/// Browser-safe summary of a retained private staging run.
/// Deliberately excludes host paths, Docker identifiers, database identities,
/// raw command output, check details, and runtime logs.
/// </summary>
public sealed record PrivateStagingSafeRunSummary(
    string StagingId,
    string? ValidationId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    DateTimeOffset? DestroyedAtUtc,
    bool PrivateOnly,
    bool PublicRoutesCreated,
    bool ProductionContainersTouched,
    bool ProductionDatabasesTouched,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool Destroyed,
    bool DestroyAvailable,
    string SafeSummary,
    string? SourceKind = null,
    string? CatalogEntryId = null,
    string? MatrixServerName = null);

/// <summary>
/// Browser-safe history response for private staging runs.
/// </summary>
public sealed record PrivateStagingSafeHistoryResponse(
    string Status,
    int TotalRuns,
    IReadOnlyList<PrivateStagingSafeRunSummary> Runs,
    string? Detail);

/// <summary>
/// Browser-safe detail response for one private staging run.
/// </summary>
public sealed record PrivateStagingSafeRunDetailResponse(
    string Status,
    PrivateStagingSafeRunSummary? Run,
    string? Detail);

public sealed class PrivateStagingDestroyIncompleteException : InvalidOperationException
{
    public PrivateStagingDestroyIncompleteException(
        string message,
        PrivateStagingRunResult result)
        : base(message)
    {
        Result = result;
    }

    public PrivateStagingRunResult Result { get; }
}

