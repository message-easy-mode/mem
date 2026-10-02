namespace Mem.Localization;

/// <summary>
/// Stable semantic message codes for forward-only API problem descriptors and
/// structured restore events. These are not HTTP routes, status values, or
/// translated prose. Once emitted, a code is a compatibility contract.
/// </summary>
public static class MemMessageCodes
{
    public static class BackupCatalog
    {
        public const string EntryNotFound = "backup-catalog.entry.not-found";
        public const string EntryUnavailable = "backup-catalog.entry.unavailable";
        public const string RestoreRequestInvalid = "backup-catalog.restore-request.invalid";
    }

    public static class Restore
    {
        public const string AttemptNotFound = "restore.attempt.not-found";
        public const string AttemptRequestInvalid = "restore.attempt-request.invalid";
        public const string WorkspaceRequestInvalid = "restore.workspace-request.invalid";
        public const string PrivateTestNotAvailable = "restore.private-test.not-available";
        public const string CancelAcknowledgementRequired = "restore.cancel.acknowledgement-required";
        public const string CancelNotAvailable = "restore.cancel.not-available";
        public const string HandoverAcknowledgementRequired = "restore.handover.acknowledgement-required";
        public const string HandoverNotAvailable = "restore.handover.not-available";
        public const string PrivateTestStarted = "restore.private-test.started";
    }
}

/// <summary>
/// Canonical lower-camel-case argument names shared by the first structured
/// Backup Catalog and Restore message descriptors.
/// </summary>
public static class MemMessageArgumentNames
{
    public const string CatalogEntryId = "catalogEntryId";
    public const string PayloadState = "payloadState";
    public const string RestoreSessionId = "restoreSessionId";
    public const string SourceKind = "sourceKind";
}
