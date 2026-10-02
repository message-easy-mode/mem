namespace Modules.Operator.Migrations;

public static class MigrationPackageRevisionStorage
{
    public const string EncryptedArchiveFileName = "source.memmigration.zip.age";
    public const string DecryptedArchiveFileName = "source.memmigration.zip";

    public static string ResolveIntakeRoot(string dataRoot, string migrationId)
    {
        ValidateSegment(migrationId, nameof(migrationId));
        return EnsureContained(
            dataRoot,
            Path.Combine(dataRoot, "migration-intakes", migrationId));
    }

    public static string ResolveRevisionRoot(
        string dataRoot,
        string migrationId,
        string packageRevisionId)
    {
        ValidateSegment(packageRevisionId, nameof(packageRevisionId));
        var intakeRoot = ResolveIntakeRoot(dataRoot, migrationId);
        return EnsureContained(
            intakeRoot,
            Path.Combine(intakeRoot, "revisions", packageRevisionId));
    }

    public static string ResolveEncryptedArchivePath(
        string dataRoot,
        string migrationId,
        string packageRevisionId) =>
        Path.Combine(
            ResolveRevisionRoot(dataRoot, migrationId, packageRevisionId),
            EncryptedArchiveFileName);

    public static string ResolveDecryptedArchivePath(
        string dataRoot,
        string migrationId,
        string packageRevisionId) =>
        Path.Combine(
            ResolveRevisionRoot(dataRoot, migrationId, packageRevisionId),
            DecryptedArchiveFileName);

    public static string ResolveLegacyEncryptedArchivePath(
        string dataRoot,
        string migrationId) =>
        Path.Combine(
            ResolveIntakeRoot(dataRoot, migrationId),
            EncryptedArchiveFileName);

    public static string ResolveLegacyDecryptedArchivePath(
        string dataRoot,
        string migrationId) =>
        Path.Combine(
            ResolveIntakeRoot(dataRoot, migrationId),
            DecryptedArchiveFileName);

    public static string ResolveExistingDecryptedArchivePath(
        string dataRoot,
        string migrationId,
        string packageRevisionId,
        bool allowLegacyPreviewFallback)
    {
        var revisionPath = ResolveDecryptedArchivePath(
            dataRoot,
            migrationId,
            packageRevisionId);
        if (File.Exists(revisionPath) || !allowLegacyPreviewFallback)
        {
            return revisionPath;
        }

        return ResolveLegacyDecryptedArchivePath(dataRoot, migrationId);
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value != Path.GetFileName(value) ||
            value.Contains(Path.DirectorySeparatorChar) ||
            value.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException(
                $"The server-owned migration identifier '{parameterName}' is invalid.");
        }
    }

    private static string EnsureContained(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The migration package path escaped its server-owned storage root.");
        }

        return fullPath;
    }
}
