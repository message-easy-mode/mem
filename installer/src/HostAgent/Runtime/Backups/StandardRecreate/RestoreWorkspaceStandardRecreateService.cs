using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Source-aware canonical Restore Workspace dispatcher for Standard Recreate.
/// It resolves a restore attempt by its canonical session identity and delegates
/// to the managed Backup Catalog payload linked to that workspace.
/// </summary>
public sealed class RestoreWorkspaceStandardRecreateService
{
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;
    private readonly BackupCatalogStore _catalogStore;
    private readonly IStandardRecreateExecutor _executor;

    public RestoreWorkspaceStandardRecreateService(
        RestoreAttemptCoordinator restoreAttemptCoordinator,
        BackupCatalogStore catalogStore,
        IStandardRecreateExecutor executor)
    {
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
        _catalogStore = catalogStore;
        _executor = executor;
    }

    public async Task<StandardRecreateResult?> ExecuteAsync(
        string restoreSessionId,
        StandardRecreateRequest request,
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
            throw new InvalidOperationException(
                "Standard Recreate is available only for a catalog-bound restore workspace.");
        }

        var catalogSource = await _catalogStore.FindRestoreSourceByEntryIdAsync(
            attempt.BackupCatalogEntryId.Value,
            ct);

        if (catalogSource is null)
        {
            throw new BackupCatalogPayloadResolutionException(
                "backup_catalog_entry_not_found",
                "The Backup Catalog source for this restore workspace was not found.");
        }

        return await _executor.ExecuteCatalogAsync(
            catalogSource.CatalogEntryId,
            attempt.RestoreSessionId,
            request,
            ct);
    }
}

/// <summary>
/// Narrow execution boundary used by the canonical dispatcher. Keeping this
/// interface source-aware allows integration tests to assert catalog dispatch
/// without attempting real Docker, Postgres, or NPM mutations.
/// </summary>
public interface IStandardRecreateExecutor
{
    Task<StandardRecreateResult> ExecuteCatalogAsync(
        string catalogEntryId,
        string restoreSessionId,
        StandardRecreateRequest request,
        CancellationToken ct);
}
