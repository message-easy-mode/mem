using Mem.Localization;

namespace HostAgent.Runtime.Backups.Contracts;

/// <summary>
/// Forward-only semantic descriptors for canonical Backup Catalog and Restore
/// workflow responses. Existing error/detail contracts remain intact; new or
/// materially touched handlers append one of these descriptors when a client
/// could localise the operator-facing outcome.
/// </summary>
public static class HostAgentStructuredMessages
{
    public static LocalizedMessage BackupCatalogEntryNotFound(
        string catalogEntryId) =>
        Create(
            MemMessageCodes.BackupCatalog.EntryNotFound,
            (MemMessageArgumentNames.CatalogEntryId, catalogEntryId));

    public static LocalizedMessage BackupCatalogEntryUnavailable(
        string catalogEntryId,
        string payloadState) =>
        Create(
            MemMessageCodes.BackupCatalog.EntryUnavailable,
            (MemMessageArgumentNames.CatalogEntryId, catalogEntryId),
            (MemMessageArgumentNames.PayloadState, payloadState));

    public static LocalizedMessage BackupCatalogRestoreRequestInvalid(
        string catalogEntryId) =>
        Create(
            MemMessageCodes.BackupCatalog.RestoreRequestInvalid,
            (MemMessageArgumentNames.CatalogEntryId, catalogEntryId));

    public static LocalizedMessage RestoreAttemptNotFound(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.AttemptNotFound,
            restoreSessionId);

    public static LocalizedMessage RestoreAttemptRequestInvalid(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.AttemptRequestInvalid,
            restoreSessionId);

    public static LocalizedMessage RestoreWorkspaceRequestInvalid(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.WorkspaceRequestInvalid,
            restoreSessionId);

    public static LocalizedMessage RestorePrivateTestNotAvailable(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.PrivateTestNotAvailable,
            restoreSessionId);

    public static LocalizedMessage RestoreCancelAcknowledgementRequired(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.CancelAcknowledgementRequired,
            restoreSessionId);

    public static LocalizedMessage RestoreCancelNotAvailable(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.CancelNotAvailable,
            restoreSessionId);

    public static LocalizedMessage RestoreHandoverAcknowledgementRequired(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.HandoverAcknowledgementRequired,
            restoreSessionId);

    public static LocalizedMessage RestoreHandoverNotAvailable(
        string restoreSessionId) =>
        CreateRestore(
            MemMessageCodes.Restore.HandoverNotAvailable,
            restoreSessionId);

    public static LocalizedMessage RestorePrivateTestStarted(
        string restoreSessionId,
        string sourceKind,
        string catalogEntryId) =>
        Create(
            MemMessageCodes.Restore.PrivateTestStarted,
            (MemMessageArgumentNames.RestoreSessionId, restoreSessionId),
            (MemMessageArgumentNames.SourceKind, sourceKind),
            (MemMessageArgumentNames.CatalogEntryId, catalogEntryId));

    private static LocalizedMessage CreateRestore(
        string code,
        string restoreSessionId) =>
        Create(
            code,
            (MemMessageArgumentNames.RestoreSessionId, restoreSessionId));

    private static LocalizedMessage Create(
        string code,
        params (string Name, string? Value)[] arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var argument in arguments)
        {
            values.Add(argument.Name, argument.Value);
        }

        return MemStructuredMessage.Create(code, values);
    }
}
