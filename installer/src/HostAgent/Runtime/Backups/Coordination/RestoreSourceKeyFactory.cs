namespace HostAgent.Runtime.Backups.Coordination;

/// <summary>
/// Centralizes canonical Backup Catalog restore-source identity. Uploaded ZIP
/// validation receipts deliberately do not participate in restore identity.
/// </summary>
public static class RestoreSourceKeyFactory
{
    public static RestoreAttemptSourceIdentity ForBackupCatalog(
        string catalogEntryId,
        Guid backupCatalogEntryId,
        string displayName,
        string originKind,
        string? sourceStackSlug,
        string? sourceBackupId)
    {
        var normalizedCatalogEntryId = NormalizePathSegment(
            catalogEntryId,
            "Backup Catalog entry id is required.");

        if (backupCatalogEntryId == Guid.Empty)
        {
            throw new InvalidOperationException("Backup Catalog entry key is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Backup Catalog display name is required.");
        }

        if (string.IsNullOrWhiteSpace(originKind))
        {
            throw new InvalidOperationException("Backup Catalog origin kind is required.");
        }

        return new RestoreAttemptSourceIdentity(
            SourceKind: "backup-catalog",
            SourceKey: $"backup-catalog:{normalizedCatalogEntryId.ToLowerInvariant()}",
            BackupCatalogEntryId: backupCatalogEntryId,
            CatalogEntryId: normalizedCatalogEntryId,
            DisplayName: displayName.Trim(),
            OriginKind: originKind.Trim(),
            SourceStackSlug: NormalizeOptionalPathSegment(sourceStackSlug),
            SourceBackupId: NormalizeOptionalPathSegment(sourceBackupId));
    }

    private static string? NormalizeOptionalPathSegment(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizePathSegment(value, "Restore source identifier is invalid.");

    public static string NormalizePathSegment(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        var normalized = value.Trim();
        if (normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            string.Equals(normalized, ".", StringComparison.Ordinal) ||
            string.Equals(normalized, "..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path traversal is not allowed in restore source identity.");
        }

        return normalized;
    }
}
