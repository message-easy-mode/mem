using Infrastructure.Data.Entities.Migrations;

namespace Modules.Operator.Migrations;

internal sealed record MigrationSessionTargetStorageInspection(
    bool Verified,
    bool HasPackageMaterial,
    bool HasDecryptedPackageMaterial);

/// <summary>
/// Verifies and removes only the server-owned target package directory for a
/// Migration Session. Unknown files, directories, links, or conversion
/// workspaces fail closed and are never traversed or deleted.
/// </summary>
internal static class MigrationSessionTargetStorageInspector
{
    private static readonly MigrationSessionTargetStorageInspection Unverified =
        new(
            Verified: false,
            HasPackageMaterial: false,
            HasDecryptedPackageMaterial: false);

    public static MigrationSessionTargetStorageInspection Inspect(
        string dataRoot,
        string migrationId,
        IReadOnlyCollection<string> packageRevisionIds)
    {
        try
        {
            var intakeRoot = MigrationPackageRevisionStorage.ResolveIntakeRoot(
                dataRoot,
                migrationId);
            var conversionRoot = ResolveConversionRoot(dataRoot, migrationId);

            if (HasReparsePointInExistingPath(dataRoot, intakeRoot) ||
                HasReparsePointInExistingPath(dataRoot, conversionRoot))
            {
                return Unverified;
            }

            if (Directory.Exists(conversionRoot) || File.Exists(conversionRoot))
            {
                return Unverified;
            }

            if (!Directory.Exists(intakeRoot) && !File.Exists(intakeRoot))
            {
                return new MigrationSessionTargetStorageInspection(
                    Verified: true,
                    HasPackageMaterial: false,
                    HasDecryptedPackageMaterial: false);
            }

            if (File.Exists(intakeRoot) || IsReparsePoint(intakeRoot))
            {
                return Unverified;
            }

            var revisionIds = packageRevisionIds.ToHashSet(StringComparer.Ordinal);
            var hasPackageMaterial = false;
            var hasDecryptedPackageMaterial = false;

            foreach (var entry in Directory.EnumerateFileSystemEntries(intakeRoot).ToArray())
            {
                if (IsReparsePoint(entry))
                {
                    return Unverified;
                }

                if (File.Exists(entry))
                {
                    if (!TryClassifyPackageFile(
                            Path.GetFileName(entry),
                            out var decrypted))
                    {
                        return Unverified;
                    }

                    hasPackageMaterial = true;
                    hasDecryptedPackageMaterial |= decrypted;
                    continue;
                }

                if (!Directory.Exists(entry) ||
                    !string.Equals(
                        Path.GetFileName(entry),
                        "revisions",
                        StringComparison.Ordinal) ||
                    !TryInspectRevisionRoot(
                        entry,
                        revisionIds,
                        ref hasPackageMaterial,
                        ref hasDecryptedPackageMaterial))
                {
                    return Unverified;
                }
            }

            return new MigrationSessionTargetStorageInspection(
                Verified: true,
                HasPackageMaterial: hasPackageMaterial,
                HasDecryptedPackageMaterial: hasDecryptedPackageMaterial);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            return Unverified;
        }
    }

    public static void DeleteVerifiedPackageMaterial(
        string dataRoot,
        string migrationId,
        IReadOnlyCollection<string> packageRevisionIds)
    {
        if (!Inspect(dataRoot, migrationId, packageRevisionIds).Verified)
        {
            throw new InvalidDataException(
                "Target-side package or workspace state could not be proven safe for permanent deletion.");
        }

        var intakeRoot = MigrationPackageRevisionStorage.ResolveIntakeRoot(
            dataRoot,
            migrationId);
        if (Directory.Exists(intakeRoot))
        {
            if (IsReparsePoint(intakeRoot) ||
                HasReparsePointInExistingPath(dataRoot, intakeRoot))
            {
                throw new InvalidDataException(
                    "A linked target-side package directory cannot be permanently deleted.");
            }

            DeleteKnownIntakeContents(
                intakeRoot,
                packageRevisionIds.ToHashSet(StringComparer.Ordinal));
            Directory.Delete(intakeRoot, recursive: false);
        }

        if (Directory.Exists(intakeRoot) || File.Exists(intakeRoot))
        {
            throw new IOException(
                "Target-side Migration Session package material remained after deletion.");
        }

        var conversionRoot = ResolveConversionRoot(dataRoot, migrationId);
        if (Directory.Exists(conversionRoot) || File.Exists(conversionRoot))
        {
            throw new IOException(
                "A target-side conversion workspace exists for this Migration Session.");
        }
    }

    /// <summary>
    /// Removes only conversion workspaces whose exact attempt identities and recorded
    /// server-owned paths belong to this Migration Session. Historical database rows are
    /// deliberately retained by the lifecycle service; this method only removes disposable
    /// pre-production filesystem material.
    /// </summary>
    public static void VerifyConversionMaterial(
        string dataRoot,
        MigrationIntakeEntity intake) =>
        _ = ValidateConversionMaterial(dataRoot, intake);

    public static void DeleteVerifiedConversionMaterial(
        string dataRoot,
        MigrationIntakeEntity intake)
    {
        var verified = ValidateConversionMaterial(dataRoot, intake);
        if (!verified.Exists)
        {
            return;
        }

        foreach (var attemptRoot in verified.AttemptRoots)
        {
            DeleteKnownConversionAttemptRoot(attemptRoot);
        }

        Directory.Delete(verified.ConversionRoot, recursive: false);
        if (Directory.Exists(verified.ConversionRoot) || File.Exists(verified.ConversionRoot))
        {
            throw new IOException(
                "Migration conversion material remained after cancellation cleanup.");
        }
    }

    private static VerifiedConversionMaterial ValidateConversionMaterial(
        string dataRoot,
        MigrationIntakeEntity intake)
    {
        var conversionRoot = ResolveConversionRoot(dataRoot, intake.IntakeId);
        if (HasReparsePointInExistingPath(dataRoot, conversionRoot))
        {
            throw new InvalidDataException(
                "A linked migration conversion root cannot be removed.");
        }

        var attemptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attempt in intake.ConversionAttempts)
        {
            if (string.IsNullOrWhiteSpace(attempt.ConversionAttemptId) ||
                !attemptIds.Add(attempt.ConversionAttemptId))
            {
                throw new InvalidDataException(
                    "Migration conversion attempt identity is missing or ambiguous.");
            }

            var operationRoot = ResolveConversionAttemptRoot(
                conversionRoot,
                attempt.ConversionAttemptId);
            var workspaceRoot = Path.Combine(operationRoot, "work");
            var outputRoot = Path.Combine(operationRoot, "output");
            var logsRoot = Path.Combine(operationRoot, "logs");

            VerifyExactRecordedPath(attempt.WorkspacePath, workspaceRoot);
            VerifyExactRecordedPath(attempt.EvidenceDirectoryPath, outputRoot);
            VerifyExactRecordedPath(attempt.LogDirectoryPath, logsRoot);
            VerifyContainedRecordedPath(attempt.CompletionReportPath, outputRoot);

            if (attempt.CandidateArtifact is { } candidate)
            {
                if (string.Equals(
                        candidate.RetentionState,
                        "active",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(candidate.ArtifactPath))
                {
                    throw new InvalidDataException(
                        "The retained Migration candidate has no recorded artifact path.");
                }

                VerifyContainedRecordedPath(candidate.ArtifactPath, outputRoot);
                VerifyContainedRecordedPath(candidate.VerificationReportPath, outputRoot);
            }
        }

        if (!Directory.Exists(conversionRoot) && !File.Exists(conversionRoot))
        {
            return new VerifiedConversionMaterial(
                conversionRoot,
                Exists: false,
                AttemptRoots: []);
        }

        if (File.Exists(conversionRoot) || IsReparsePoint(conversionRoot))
        {
            throw new InvalidDataException(
                "Migration conversion storage is not a normal server-owned directory.");
        }

        var attemptRoots = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(conversionRoot).ToArray())
        {
            if (!Directory.Exists(entry) ||
                IsReparsePoint(entry) ||
                !attemptIds.Contains(Path.GetFileName(entry)))
            {
                throw new InvalidDataException(
                    "Unknown material exists in the Migration conversion root.");
            }

            ValidateKnownConversionAttemptRoot(entry);
            attemptRoots.Add(entry);
        }

        return new VerifiedConversionMaterial(
            conversionRoot,
            Exists: true,
            AttemptRoots: attemptRoots);
    }

    private sealed record VerifiedConversionMaterial(
        string ConversionRoot,
        bool Exists,
        IReadOnlyList<string> AttemptRoots);

    private static void ValidateKnownConversionAttemptRoot(string operationRoot)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "work",
            "output",
            "logs",
            "worker-request.json",
        };

        foreach (var entry in Directory.EnumerateFileSystemEntries(operationRoot).ToArray())
        {
            var name = Path.GetFileName(entry);
            if (!allowed.Contains(name) || IsReparsePoint(entry))
            {
                throw new InvalidDataException(
                    "Unknown or linked material exists in a Migration conversion attempt.");
            }

            if (string.Equals(name, "worker-request.json", StringComparison.Ordinal))
            {
                if (!File.Exists(entry))
                {
                    throw new InvalidDataException(
                        "The Migration conversion worker request path is not a normal file.");
                }
                continue;
            }

            if (!Directory.Exists(entry))
            {
                throw new InvalidDataException(
                    "A Migration conversion workspace path is not a normal directory.");
            }

            EnsureTreeContainsNoReparsePoints(entry);
        }
    }

    private static void DeleteKnownConversionAttemptRoot(string operationRoot)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "work",
            "output",
            "logs",
            "worker-request.json",
        };

        foreach (var entry in Directory.EnumerateFileSystemEntries(operationRoot).ToArray())
        {
            var name = Path.GetFileName(entry);
            if (!allowed.Contains(name) || IsReparsePoint(entry))
            {
                throw new InvalidDataException(
                    "Unknown or linked material exists in a Migration conversion attempt.");
            }

            if (string.Equals(name, "worker-request.json", StringComparison.Ordinal))
            {
                if (!File.Exists(entry))
                {
                    throw new InvalidDataException(
                        "The Migration conversion worker request path is not a normal file.");
                }

                File.Delete(entry);
                continue;
            }

            if (!Directory.Exists(entry))
            {
                throw new InvalidDataException(
                    "A Migration conversion workspace path is not a normal directory.");
            }

            EnsureTreeContainsNoReparsePoints(entry);
            Directory.Delete(entry, recursive: true);
        }

        Directory.Delete(operationRoot, recursive: false);
        if (Directory.Exists(operationRoot) || File.Exists(operationRoot))
        {
            throw new IOException(
                "A Migration conversion attempt remained after cancellation cleanup.");
        }
    }

    private static void EnsureTreeContainsNoReparsePoints(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (IsReparsePoint(current))
            {
                throw new InvalidDataException(
                    "Linked material exists inside a Migration conversion workspace.");
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (IsReparsePoint(entry))
                {
                    throw new InvalidDataException(
                        "Linked material exists inside a Migration conversion workspace.");
                }

                if (Directory.Exists(entry))
                {
                    pending.Push(entry);
                }
                else if (!File.Exists(entry))
                {
                    throw new InvalidDataException(
                        "Unknown filesystem material exists inside a Migration conversion workspace.");
                }
            }
        }
    }

    private static void VerifyExactRecordedPath(
        string? recordedPath,
        string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(recordedPath))
        {
            return;
        }

        if (!string.Equals(
                Path.GetFullPath(recordedPath),
                Path.GetFullPath(expectedPath),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Recorded Migration conversion workspace identity does not match server-owned storage.");
        }
    }

    private static void VerifyContainedRecordedPath(
        string? recordedPath,
        string expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(recordedPath))
        {
            return;
        }

        var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(expectedRoot)) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(recordedPath);
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Recorded Migration conversion evidence escaped server-owned output storage.");
        }
    }

    private static string ResolveConversionAttemptRoot(
        string conversionRoot,
        string attemptId)
    {
        var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(conversionRoot)) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(conversionRoot, attemptId));
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Migration conversion attempt path escaped its server-owned Session root.");
        }

        return path;
    }

    private static void DeleteKnownIntakeContents(
        string intakeRoot,
        IReadOnlySet<string> revisionIds)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(intakeRoot).ToArray())
        {
            if (IsReparsePoint(entry))
            {
                throw new InvalidDataException(
                    "A linked target-side package entry cannot be permanently deleted.");
            }

            if (File.Exists(entry))
            {
                if (!TryClassifyPackageFile(Path.GetFileName(entry), out _))
                {
                    throw new InvalidDataException(
                        "An unknown target-side package file cannot be permanently deleted.");
                }

                File.Delete(entry);
                continue;
            }

            if (!Directory.Exists(entry) ||
                !string.Equals(
                    Path.GetFileName(entry),
                    "revisions",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "An unknown target-side package directory cannot be permanently deleted.");
            }

            DeleteKnownRevisionContents(entry, revisionIds);
            Directory.Delete(entry, recursive: false);
        }
    }

    private static void DeleteKnownRevisionContents(
        string revisionsRoot,
        IReadOnlySet<string> revisionIds)
    {
        if (!Directory.Exists(revisionsRoot) || IsReparsePoint(revisionsRoot))
        {
            throw new InvalidDataException(
                "A linked target-side package revision root cannot be permanently deleted.");
        }

        foreach (var revisionEntry in Directory.EnumerateFileSystemEntries(revisionsRoot).ToArray())
        {
            if (!Directory.Exists(revisionEntry) ||
                IsReparsePoint(revisionEntry) ||
                !revisionIds.Contains(Path.GetFileName(revisionEntry)))
            {
                throw new InvalidDataException(
                    "An unknown target-side package revision cannot be permanently deleted.");
            }

            foreach (var packageEntry in Directory.EnumerateFileSystemEntries(revisionEntry).ToArray())
            {
                if (!File.Exists(packageEntry) ||
                    IsReparsePoint(packageEntry) ||
                    !TryClassifyPackageFile(Path.GetFileName(packageEntry), out _))
                {
                    throw new InvalidDataException(
                        "An unknown target-side package revision entry cannot be permanently deleted.");
                }

                File.Delete(packageEntry);
            }

            Directory.Delete(revisionEntry, recursive: false);
        }
    }

    private static bool TryInspectRevisionRoot(
        string revisionsRoot,
        IReadOnlySet<string> revisionIds,
        ref bool hasPackageMaterial,
        ref bool hasDecryptedPackageMaterial)
    {
        foreach (var revisionEntry in Directory.EnumerateFileSystemEntries(revisionsRoot).ToArray())
        {
            if (!Directory.Exists(revisionEntry) ||
                IsReparsePoint(revisionEntry) ||
                !revisionIds.Contains(Path.GetFileName(revisionEntry)))
            {
                return false;
            }

            foreach (var packageEntry in Directory.EnumerateFileSystemEntries(revisionEntry).ToArray())
            {
                if (!File.Exists(packageEntry) ||
                    IsReparsePoint(packageEntry) ||
                    !TryClassifyPackageFile(
                        Path.GetFileName(packageEntry),
                        out var decrypted))
                {
                    return false;
                }

                hasPackageMaterial = true;
                hasDecryptedPackageMaterial |= decrypted;
            }
        }

        return true;
    }

    private static bool HasReparsePointInExistingPath(
        string dataRoot,
        string targetPath)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(dataRoot));
        var fullTarget = Path.GetFullPath(targetPath);
        var relative = Path.GetRelativePath(fullRoot, fullTarget);
        if (relative == ".." ||
            relative.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The target storage path escaped the configured data root.");
        }

        if ((Directory.Exists(fullRoot) || File.Exists(fullRoot)) &&
            IsReparsePoint(fullRoot))
        {
            return true;
        }

        var current = fullRoot;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                IsReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryClassifyPackageFile(
        string fileName,
        out bool decrypted)
    {
        decrypted = false;

        if (string.Equals(
                fileName,
                MigrationPackageRevisionStorage.EncryptedArchiveFileName,
                StringComparison.Ordinal) ||
            IsKnownPartialPackageFile(
                fileName,
                MigrationPackageRevisionStorage.EncryptedArchiveFileName))
        {
            return true;
        }

        if (string.Equals(
                fileName,
                MigrationPackageRevisionStorage.DecryptedArchiveFileName,
                StringComparison.Ordinal) ||
            IsKnownPartialPackageFile(
                fileName,
                MigrationPackageRevisionStorage.DecryptedArchiveFileName))
        {
            decrypted = true;
            return true;
        }

        return false;
    }

    private static bool IsKnownPartialPackageFile(
        string fileName,
        string baseFileName) =>
        fileName.StartsWith(baseFileName + ".", StringComparison.Ordinal) &&
        fileName.EndsWith(".partial", StringComparison.Ordinal) &&
        fileName.Length > baseFileName.Length + ".x.partial".Length;

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string ResolveConversionRoot(
        string dataRoot,
        string migrationId)
    {
        // Reuse the package resolver to validate the server-owned identifier.
        _ = MigrationPackageRevisionStorage.ResolveIntakeRoot(dataRoot, migrationId);
        var root = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(dataRoot)) +
            Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(
            Path.Combine(dataRoot, "migration-conversions", migrationId));
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The migration conversion path escaped its server-owned storage root.");
        }

        return path;
    }
}
