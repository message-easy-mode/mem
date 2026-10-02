using HostAgent.Runtime.Backups.Catalog;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

/// <summary>
/// Catalog-native, read-only Production Restore planning boundary.
///
/// It resolves immutable export material from the managed Backup Catalog
/// payload, prepares/resumes the durable catalog restore workspace, and delegates
/// only to the existing read-only plan evaluator. It never reads the original
/// uploaded ZIP or uses a validation receipt as a storage locator.
/// </summary>
public sealed class CatalogProductionRestorePlanService
{
    private readonly BackupCatalogPayloadResolver _payloadResolver;
    private readonly CatalogRestoreSessionService _catalogRestoreSessions;
    private readonly RuntimeStackBackupProductionRestorePlanService _planService;

    public CatalogProductionRestorePlanService(
        BackupCatalogPayloadResolver payloadResolver,
        CatalogRestoreSessionService catalogRestoreSessions,
        RuntimeStackBackupProductionRestorePlanService planService)
    {
        _payloadResolver = payloadResolver;
        _catalogRestoreSessions = catalogRestoreSessions;
        _planService = planService;
    }

    public async Task<CatalogProductionRestorePlanResponse> CreatePlanAsync(
        string catalogEntryId,
        CatalogProductionRestorePlanRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var payload = await _payloadResolver.ResolveProductionRestorePlanPayloadAsync(
            catalogEntryId,
            ct);

        // This is durable workflow/audit state only. It does not reserve a
        // target or mutate Docker, databases, DNS, certificates, NPM, or routes.
        var session = await _catalogRestoreSessions.PrepareAsync(
            payload.CatalogEntryId,
            ct);

        var source = ToSourceSummary(payload);

        var plan = await _planService.CreatePlanFromCatalogSourceAsync(
            catalogEntryId: payload.CatalogEntryId,
            legacyValidationId: null,
            source: source,
            stagingId: request.StagingId,
            candidateId: request.CandidateId,
            targetStackSlug: request.TargetStackSlug,
            restoreMode: request.RestoreMode,
            intendedMatrixHost: request.IntendedMatrixHost,
            intendedElementHost: request.IntendedElementHost,
            ct: ct);

        return new CatalogProductionRestorePlanResponse(
            Source: "control-plane",
            Status: plan.Status,
            CatalogEntryId: payload.CatalogEntryId,
            RestoreSessionId: session.RestoreSessionId,
            RestoreAttemptCreated: session.RestoreAttemptCreated,
            RestoreAttemptResumed: session.RestoreAttemptResumed,
            PayloadState: BackupCatalogPayloadStates.Available,
            IntegrityStatus: payload.IntegrityStatus,
            WarningCount: payload.WarningCount,
            Plan: plan,
            Detail: plan.Status == "blocked"
                ? "Catalog-native Production Restore plan was created, but blockers require attention before any future execution sprint."
                : "Catalog-native Production Restore plan is ready for review. Production execution remains locked.");
    }

    private static RuntimeStackBackupProductionRestoreSourceSummary ToSourceSummary(
        BackupCatalogProductionRestorePlanPayload payload)
    {
        var manifest = payload.Manifest;

        return new RuntimeStackBackupProductionRestoreSourceSummary(
            UploadedZipFound: false,
            UploadedZipPath: null,
            UploadedZipName: null,
            UploadedZipSizeBytes: null,
            ManifestPresent: true,
            ManifestVersion: manifest.ManifestVersion,
            ExportKind: manifest.ExportKind,
            CreatedAtUtc: new DateTimeOffset(
                DateTime.SpecifyKind(manifest.CreatedAtUtc, DateTimeKind.Utc)),
            CreatedBy: manifest.CreatedBy,
            MemVersion: manifest.MemVersion,
            SourceStackSlug: manifest.Stack.Slug,
            SourceDisplayName: manifest.Stack.DisplayName,
            SourceMatrixServerName: manifest.Stack.MatrixServerName,
            MatrixPublicUrl: manifest.Stack.MatrixPublicUrl,
            ElementPublicUrl: manifest.Stack.ElementPublicUrl,
            ManifestMatrixHost: manifest.Routes.MatrixHost,
            ManifestElementHost: manifest.Routes.ElementHost,
            DatabaseDumpPresent: true,
            DatabaseDumpPath: "database/synapse.sql",
            HomeserverConfigPresent: true,
            HomeserverConfigPath: "matrix/homeserver.yaml",
            SigningKeyPresent: true,
            SigningKeyPath: "matrix/signing.key",
            MediaStorePresent: payload.MediaStorePresent,
            MediaFiles: payload.MediaFiles,
            MediaBytes: payload.MediaBytes,
            ElementConfigPresent: payload.ElementConfigPresent,
            ElementConfigPath: payload.ElementConfigPresent
                ? "element/config.json"
                : null,
            ManifestWarnings: manifest.Warnings ?? [],
            SourceKind: "backup-catalog",
            CatalogEntryId: payload.CatalogEntryId);
    }
}

/// <summary>
/// Explicit read-only input for a catalog-native Production Restore plan.
/// Omit no values accidentally: staging evidence and restore mode remain
/// operator choices, while source material always comes from catalogEntryId.
/// </summary>
public sealed record CatalogProductionRestorePlanRequest(
    string? StagingId,
    string? CandidateId,
    string? TargetStackSlug,
    string? RestoreMode,
    string? IntendedMatrixHost,
    string? IntendedElementHost);

public sealed record CatalogProductionRestorePlanResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    RuntimeStackBackupProductionRestorePlanResult Plan,
    string Detail);
