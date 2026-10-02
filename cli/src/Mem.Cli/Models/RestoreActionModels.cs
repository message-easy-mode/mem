namespace Mem.Cli.Models;

/// <summary>
/// Safe result of creating or resuming a canonical Restore Workspace from a
/// Backup Catalog entry. The catalog entry remains the restore source; the
/// returned restore session id becomes the identity for workspace operations.
/// </summary>
public sealed record CatalogRestoreSessionCliResult(
    string Source,
    string Status,
    string? CatalogEntryId,
    string? RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string? SourceKind,
    string? PayloadState,
    string? IntegrityStatus,
    int WarningCount,
    string? Detail);

/// <summary>
/// Safe response from the canonical Restore Workspace private-test action.
/// The response intentionally contains no staging filesystem, Docker, or
/// credential details.
/// </summary>
public sealed record RestorePrivateTestCliResult(
    string Source,
    string Status,
    string? RestoreSessionId,
    Guid? OperationId,
    string? SourceKind,
    string? CatalogEntryId,
    string? StagingId,
    bool? PrivateOnly,
    bool? DatabaseImportSucceeded,
    bool? SynapseHealthPassed,
    bool? RequiresExplicitDestroy,
    string? Detail);


/// <summary>
/// Safe result of explicitly destroying the retained private staging runtime
/// associated with a canonical Restore Workspace. The CLI resolves staging
/// identity from workspace evidence; callers never supply a validation ID,
/// host path, container ID, or raw staging ID.
/// </summary>
public sealed record RestorePrivateTestDestroyCliResult(
    string Source,
    string Status,
    string RestoreSessionId,
    string? StagingId,
    DateTimeOffset? DestroyedAtUtc,
    bool? SynapseContainerRemoved,
    bool? PostgresContainerRemoved,
    bool? NetworkRemoved,
    bool? WorkspaceRemoved,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Read-only target assessment for Standard Recreate. The preflight route does
/// not reserve targets or mutate a restore attempt.
/// </summary>
public sealed record RestoreStandardRecreatePreflightCliRequest(
    string TargetStackSlug,
    string ElementHost,
    string? RequestedDomainId,
    string? MatrixHost);

public sealed record RestoreStandardRecreatePreflightCliResult(
    string Source,
    string Status,
    DateTimeOffset? CheckedAtUtc,
    string? RestoreSessionId,
    bool CanCreate,
    RestoreStandardRecreatePreflightTargets? Targets,
    IReadOnlyList<RestoreStandardRecreatePreflightCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RestoreStandardRecreatePreflightTargets(
    string? TargetStackSlug,
    string? MatrixHost,
    string? ElementHost);

public sealed record RestoreStandardRecreatePreflightCheck(
    string Code,
    string Title,
    string State,
    string Message);

/// <summary>
/// Explicit request to create a real recovered runtime stack from a canonical
/// Restore Workspace. The CLI maps acknowledgement flags to the HostAgent's
/// existing production-recreate acknowledgement contract.
/// </summary>
public sealed record RestoreStandardRecreateCliRequest(
    string TargetStackSlug,
    string ElementHost,
    string? RequestedDomainId,
    string? MatrixHost,
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
/// Safe CLI projection of a Standard Recreate result. The HostAgent's detailed
/// execution response includes internal runtime paths, container identities,
/// and database details that are deliberately not represented here.
/// </summary>
public sealed record RestoreStandardRecreateCliResult(
    string Source,
    string Status,
    string? RestoreSessionId,
    string? RecreateId,
    string? CatalogEntryId,
    string SourceKind,
    string? TargetStackSlug,
    string? MatrixHost,
    string? ElementHost,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? Operator,
    string? Note,
    RestoreStandardRecreateDatabaseCliSummary? Database,
    RestoreStandardRecreateRuntimeCliSummary? Runtime,
    RestoreStandardRecreateRouteCliSummary? Routes,
    RestoreStandardRecreateMutationCliSummary? Mutations,
    IReadOnlyList<RestoreStandardRecreateCheckCliProjection> Checks,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail);

public sealed record RestoreStandardRecreateDatabaseCliSummary(
    bool Provisioned,
    bool ImportSucceeded,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    long? StateEventsCount);

public sealed record RestoreStandardRecreateRuntimeCliSummary(
    bool MatrixStarted,
    bool MatrixHealthPassed,
    bool ElementStarted,
    bool ElementHealthPassed,
    bool StackRegistered);

public sealed record RestoreStandardRecreateRouteCliSummary(
    string? MatrixPublicHost,
    bool MatrixRouteReady,
    string? ElementPublicHost,
    bool ElementRouteReady,
    bool PublicReadinessPassed);

public sealed record RestoreStandardRecreateMutationCliSummary(
    bool RuntimeStackCreated,
    bool ProductionPostgresMutated,
    bool ProductionContainersTouched,
    bool NpmRoutesChanged,
    bool DnsChanged,
    bool CertificatesChanged,
    bool OldStacksDeleted);

public sealed record RestoreStandardRecreateCheckCliProjection(
    string Code,
    string Severity,
    bool Passed,
    string Message);

/// <summary>
/// Safe summary of a cancellation or handover transition. HostAgent snapshots
/// include internal workspace paths, so the CLI projects only durable operator
/// lifecycle fields.
/// </summary>
public sealed record RestoreSessionActionCliResult(
    string Source,
    string Status,
    string Action,
    string? RestoreSessionId,
    string? AttemptStatus,
    string? CurrentStage,
    DateTimeOffset? UpdatedAtUtc,
    DateTimeOffset? TerminalAtUtc,
    int WarningCount,
    int ErrorCount,
    string? LastErrorCode,
    string? LastErrorSummary,
    string? Detail);

internal sealed record RestoreStandardRecreateApiResponse(
    string Source,
    string Status,
    string RecreateId,
    string TargetStackSlug,
    string MatrixHost,
    string ElementHost,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset FinishedAtUtc,
    string? Operator,
    string? Note,
    RestoreStandardRecreateDatabaseApiSummary Database,
    RestoreStandardRecreateRuntimeApiSummary Runtime,
    RestoreStandardRecreateRouteApiSummary Routes,
    RestoreStandardRecreateMutationApiSummary Mutations,
    IReadOnlyList<RestoreStandardRecreateCheckApiResponse>? Checks,
    IReadOnlyList<string>? Warnings,
    IReadOnlyList<string>? Errors,
    string Detail,
    string CatalogEntryId);

internal sealed record RestoreStandardRecreateDatabaseApiSummary(
    bool Provisioned,
    bool ImportSucceeded,
    int PublicTableCount,
    int SynapseKnownTableCount,
    long? UsersCount,
    long? EventsCount,
    long? RoomsCount,
    long? StateEventsCount);

internal sealed record RestoreStandardRecreateRuntimeApiSummary(
    bool MatrixStarted,
    bool MatrixHealthPassed,
    bool ElementStarted,
    bool ElementHealthPassed,
    bool StackRegistered);

internal sealed record RestoreStandardRecreateRouteApiSummary(
    string MatrixPublicHost,
    bool MatrixRouteReady,
    string ElementPublicHost,
    bool ElementRouteReady,
    bool PublicReadinessPassed);

internal sealed record RestoreStandardRecreateMutationApiSummary(
    bool RuntimeStackCreated,
    bool ProductionPostgresMutated,
    bool ProductionContainersTouched,
    bool NpmRoutesChanged,
    bool DnsChanged,
    bool CertificatesChanged,
    bool OldStacksDeleted);

internal sealed record RestoreStandardRecreateCheckApiResponse(
    string Code,
    string Severity,
    bool Passed,
    string Message);

internal sealed record PrivateStagingDestroyApiResponse(
    string Source,
    string Status,
    string StagingId,
    PrivateStagingDestroyApiSummary? Destroy,
    IReadOnlyList<string>? Warnings,
    IReadOnlyList<string>? Errors,
    string? Detail);

internal sealed record PrivateStagingDestroyApiSummary(
    DateTimeOffset DestroyedAtUtc,
    bool SynapseContainerRemoved,
    bool PostgresContainerRemoved,
    bool NetworkRemoved,
    bool WorkspaceRemoved,
    IReadOnlyList<string>? Warnings);

internal sealed record RestoreAttemptActionApiResponse(
    string RestoreSessionId,
    string Status,
    string CurrentStage,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? TerminalAtUtc,
    string? LastErrorCode,
    string? LastErrorSummary,
    int WarningCount,
    int ErrorCount);
