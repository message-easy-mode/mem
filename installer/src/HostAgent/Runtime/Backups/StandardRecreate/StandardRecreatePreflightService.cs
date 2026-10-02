using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Builds a safe, read-only Standard Recreate assessment for the Restore
/// Workspace. It must never reserve targets or mutate restore state. The
/// executor remains authoritative and repeats all ownership checks inside its
/// atomic reservation transaction.
/// </summary>
public sealed class StandardRecreatePreflightService
{
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;
    private readonly BackupCatalogStore _catalogStore;
    private readonly CatalogStandardRecreatePreflightService _catalogPreflightService;

    public StandardRecreatePreflightService(
        RestoreAttemptCoordinator restoreAttemptCoordinator,
        BackupCatalogStore catalogStore,
        CatalogStandardRecreatePreflightService catalogPreflightService)
    {
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
        _catalogStore = catalogStore;
        _catalogPreflightService = catalogPreflightService;
    }

    public async Task<StandardRecreatePreflightResponse?> AssessAsync(
        string restoreSessionId,
        StandardRecreatePreflightRequest request,
        CancellationToken ct)
    {
        var attempt = await _restoreAttemptCoordinator.GetByRestoreSessionIdAsync(
            restoreSessionId,
            ct);

        if (attempt is null)
        {
            return null;
        }

        if (!string.Equals(attempt.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) ||
            attempt.BackupCatalogEntryId is null ||
            attempt.BackupCatalogEntryId == Guid.Empty)
        {
            return BuildCatalogOnlyResponse(attempt, request);
        }

        return await AssessCatalogSourceAsync(attempt, request, ct);
    }

    private async Task<StandardRecreatePreflightResponse> AssessCatalogSourceAsync(
        RestoreAttemptSnapshot attempt,
        StandardRecreatePreflightRequest request,
        CancellationToken ct)
    {
        if (attempt.BackupCatalogEntryId is null ||
            attempt.BackupCatalogEntryId == Guid.Empty)
        {
            return BuildMissingMatrixIdentityResponse(attempt, request);
        }

        var catalogSource = await _catalogStore.FindRestoreSourceByEntryIdAsync(
            attempt.BackupCatalogEntryId.Value,
            ct);

        if (catalogSource is null)
        {
            return BuildMissingMatrixIdentityResponse(attempt, request);
        }

        var catalogResult = await _catalogPreflightService.AssessAsync(
            catalogSource.CatalogEntryId,
            request,
            ct);

        return new StandardRecreatePreflightResponse(
            Source: catalogResult.Source,
            Status: catalogResult.Status,
            CheckedAtUtc: catalogResult.CheckedAtUtc,
            RestoreSessionId: attempt.RestoreSessionId,
            CanCreate: catalogResult.CanCreate,
            Targets: catalogResult.Targets,
            Checks: catalogResult.Checks,
            Blockers: catalogResult.Blockers,
            Warnings: catalogResult.Warnings,
            Detail: catalogResult.Detail)
        {
            Turn = catalogResult.Turn
        };
    }


    private static StandardRecreatePreflightResponse BuildCatalogOnlyResponse(
        RestoreAttemptSnapshot attempt,
        StandardRecreatePreflightRequest request)
    {
        const string safeMessage = "This restore workspace is not backed by a managed Backup Catalog entry.";
        return new StandardRecreatePreflightResponse(
            Source: "control-plane",
            Status: "blocked",
            CheckedAtUtc: DateTimeOffset.UtcNow,
            RestoreSessionId: attempt.RestoreSessionId,
            CanCreate: false,
            Targets: new StandardRecreatePreflightTargets(
                NormalizeDisplayValue(request.TargetStackSlug),
                null,
                NormalizeDisplayValue(request.ElementHost)),
            Checks: [new StandardRecreatePreflightCheck("backup-catalog-source-required", "Backup Catalog source is required", "blocked", safeMessage)],
            Blockers: [safeMessage],
            Warnings: ["No resources were created or reserved."],
            Detail: "Materialise the backup into the Backup Catalog before beginning a restore.");
    }

    private static StandardRecreatePreflightResponse BuildMissingMatrixIdentityResponse(
        RestoreAttemptSnapshot attempt,
        StandardRecreatePreflightRequest request)
    {
        const string safeMessage = "This backup does not declare the Matrix server identity required for a standard restore.";

        return new StandardRecreatePreflightResponse(
            Source: "control-plane",
            Status: "blocked",
            CheckedAtUtc: DateTimeOffset.UtcNow,
            RestoreSessionId: attempt.RestoreSessionId,
            CanCreate: false,
            Targets: new StandardRecreatePreflightTargets(
                NormalizeDisplayValue(request.TargetStackSlug),
                null,
                NormalizeDisplayValue(request.ElementHost)),
            Checks:
            [
                new StandardRecreatePreflightCheck(
                    "matrix-server-identity-available",
                    "Matrix server identity is available",
                    "blocked",
                    safeMessage)
            ],
            Blockers: [safeMessage],
            Warnings:
            [
                "No resources were created or reserved.",
                "MEM repeats every ownership check and reserves targets atomically when creation begins."
            ],
            Detail: "The backup must identify its original Matrix server before MEM can assess a standard restore.");
    }

    private static StandardRecreatePreflightResponse BuildInvalidTargetResponse(
        RestoreAttemptSnapshot attempt,
        StandardRecreatePreflightRequest request,
        string sourceMatrixIdentity)
    {
        const string safeMessage = "Enter a valid replacement stack name and Element public host. The Matrix server identity is preserved from the backup.";

        return new StandardRecreatePreflightResponse(
            Source: "control-plane",
            Status: "blocked",
            CheckedAtUtc: DateTimeOffset.UtcNow,
            RestoreSessionId: attempt.RestoreSessionId,
            CanCreate: false,
            Targets: new StandardRecreatePreflightTargets(
                NormalizeDisplayValue(request.TargetStackSlug),
                sourceMatrixIdentity,
                NormalizeDisplayValue(request.ElementHost)),
            Checks:
            [
                new StandardRecreatePreflightCheck(
                    "target-values-valid",
                    "Replacement details are valid",
                    "blocked",
                    safeMessage)
            ],
            Blockers: [safeMessage],
            Warnings:
            [
                "No resources were created or reserved.",
                "MEM repeats every ownership check and reserves targets atomically when creation begins."
            ],
            Detail: "Replacement details need attention before MEM can assess availability.");
    }

    private static string? NormalizeDisplayValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }


}
