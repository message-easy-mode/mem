namespace HostAgent.Runtime.Backups.AdvancedCutover.Preview;

/// <summary>
/// Explicit input for a catalog-native, candidate-backed public cutover preview.
/// This is read-only: it evaluates future route actions but cannot create or
/// change NPM routes, DNS, certificates, public runtime registrations, or
/// federation exposure.
/// </summary>
public sealed record CatalogPublicCutoverPreviewRequest(
    string CandidateId,
    string? TargetStackSlug,
    string? RestoreMode,
    string? IntendedMatrixHost,
    string? IntendedElementHost);

/// <summary>
/// Catalog-native wrapper around the durable cutover preview. The embedded
/// preview retains ValidationId for legacy history compatibility; CatalogEntryId
/// and SourceKind identify the canonical recovery source.
/// </summary>
public sealed record CatalogPublicCutoverPreviewResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string CandidateId,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    RuntimeStackBackupPublicCutoverPreviewResult Preview,
    string Detail);
