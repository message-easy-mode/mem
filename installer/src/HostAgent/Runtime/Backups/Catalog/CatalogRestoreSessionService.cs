using HostAgent.Runtime.Backups.Coordination;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Starts or resumes a durable restore workspace from the canonical Backup
/// Catalog identity. It deliberately creates no legacy upload/validation
/// artifacts and performs no destructive restore work.
/// </summary>
public sealed class CatalogRestoreSessionService
{
    private readonly BackupCatalogStore _catalogStore;
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;

    public CatalogRestoreSessionService(
        BackupCatalogStore catalogStore,
        RestoreAttemptCoordinator restoreAttemptCoordinator)
    {
        _catalogStore = catalogStore;
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
    }

    public async Task<CatalogRestoreSessionResponse> PrepareAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var source = await _catalogStore.FindRestoreSourceAsync(
            catalogEntryId,
            ct);

        if (source is null)
        {
            throw new CatalogRestoreSourceNotFoundException(
                $"Backup Catalog entry '{catalogEntryId}' was not found.");
        }

        if (!string.Equals(
                source.PayloadState,
                BackupCatalogPayloadStates.Available,
                StringComparison.Ordinal))
        {
            throw new CatalogRestoreSourceUnavailableException(
                source.CatalogEntryId,
                source.PayloadState,
                $"Backup Catalog entry '{source.CatalogEntryId}' cannot begin a restore while payload state is '{source.PayloadState}'.");
        }

        if (!Directory.Exists(source.PayloadDirectoryPath))
        {
            throw new CatalogRestoreSourceUnavailableException(
                source.CatalogEntryId,
                source.PayloadState,
                $"Backup Catalog entry '{source.CatalogEntryId}' is marked available but its managed payload is missing.");
        }

        var attempt = await _restoreAttemptCoordinator.GetOrCreateBackupCatalogAsync(
            source,
            ct);

        return new CatalogRestoreSessionResponse(
            Source: "control-plane",
            Status: "ok",
            CatalogEntryId: source.CatalogEntryId,
            RestoreSessionId: attempt.Attempt.RestoreSessionId,
            RestoreAttemptCreated: attempt.Created,
            RestoreAttemptResumed: attempt.Resumed,
            SourceKind: attempt.Attempt.SourceKind,
            PayloadState: source.PayloadState,
            IntegrityStatus: source.IntegrityStatus,
            WarningCount: source.WarningCount,
            Detail: attempt.Created
                ? "A restore workspace was created for the Backup Catalog entry."
                : "The existing active restore workspace was resumed for the Backup Catalog entry.");
    }
}

public sealed record CatalogRestoreSessionResponse(
    string Source,
    string Status,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string SourceKind,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    string Detail);

public sealed class CatalogRestoreSourceNotFoundException : InvalidOperationException
{
    public CatalogRestoreSourceNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class CatalogRestoreSourceUnavailableException : InvalidOperationException
{
    public CatalogRestoreSourceUnavailableException(
        string catalogEntryId,
        string payloadState,
        string message)
        : base(message)
    {
        CatalogEntryId = catalogEntryId;
        PayloadState = payloadState;
    }

    public string CatalogEntryId { get; }
    public string PayloadState { get; }
}
