using System.Text.Json;
using Microsoft.Extensions.Configuration;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Coordination;

namespace HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;

public sealed class LocalBackupCatalogService
{
    private static readonly int[] AllowedEntryPageSizes = [10, 25, 50, 100];

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfiguration _configuration;
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;

    public LocalBackupCatalogService(
        IConfiguration configuration,
        RestoreAttemptCoordinator restoreAttemptCoordinator)
    {
        _configuration = configuration;
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
    }

    public async Task<LocalBackupCatalogResponse> ListAsync(
        CancellationToken ct)
    {
        var backupsRoot = GetStackBackupsRoot();

        if (!Directory.Exists(backupsRoot))
        {
            return new LocalBackupCatalogResponse(
                Source: "control-plane",
                Status: "ok",
                BackupsRootPath: backupsRoot,
                Stacks: Array.Empty<LocalBackupCatalogStack>(),
                Detail: "Backup root does not exist yet. No stack backups have been created.");
        }

        var stacks = new List<LocalBackupCatalogStack>();

        foreach (var stackDirectory in SafeEnumerateDirectories(backupsRoot)
                     .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();

            var stackSlug = Path.GetFileName(stackDirectory);

            if (string.IsNullOrWhiteSpace(stackSlug))
            {
                continue;
            }

            var stackHistory = await InspectStackDirectoryAsync(
                stackSlug,
                stackDirectory,
                ct);

            if (stackHistory.BackupCount > 0)
            {
                stacks.Add(stackHistory);
            }
        }

        return new LocalBackupCatalogResponse(
            Source: "control-plane",
            Status: "ok",
            BackupsRootPath: backupsRoot,
            Stacks: stacks,
            Detail: null);
    }

    /// <summary>
    /// Returns a flat, paged local-backup inventory for the operator "By date" table.
    /// The existing <see cref="ListAsync"/> catalog remains unchanged for the separate
    /// grouped "By stack" projection.
    /// </summary>
    public async Task<LocalBackupEntryListResponse> ListEntriesAsync(
        LocalBackupEntryQuery request,
        CancellationToken ct)
    {
        var query = NormalizeEntryQuery(request);
        var catalog = await ListAsync(ct);

        var allEntries = catalog.Stacks
            .SelectMany(static stack => stack.Backups)
            .ToArray();

        var availableStacks = allEntries
            .Select(static entry => entry.StackSlug)
            .Where(static stackSlug => !string.IsNullOrWhiteSpace(stackSlug))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static stackSlug => stackSlug, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filtered = allEntries
            .Where(entry => MatchesEntrySearch(entry, query.Search))
            .Where(entry => MatchesEntryStack(entry, query.StackSlug))
            .ToArray();

        var ordered = ApplyEntrySort(
                filtered,
                query.SortBy,
                query.SortDirection)
            .ToArray();

        var totalBackups = ordered.Length;
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalBackups / (double)query.PageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);
        var skip = (page - 1) * query.PageSize;
        var pagedBackups = ordered
            .Skip(skip)
            .Take(query.PageSize)
            .ToArray();

        var latestBackup = ApplyEntrySort(filtered, "created", "desc")
            .FirstOrDefault();

        var summary = new LocalBackupEntryListSummary(
            TotalBackups: totalBackups,
            TotalStacks: filtered
                .Select(static entry => entry.StackSlug)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            TotalBytes: filtered.Sum(static entry => entry.TotalBytes),
            TotalFiles: filtered.Sum(static entry => entry.TotalFiles),
            LatestBackup: latestBackup);

        var warnings = catalog.Stacks
            .SelectMany(static stack => stack.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var firstItemNumber = totalBackups == 0 ? 0 : skip + 1;
        var lastItemNumber = skip + pagedBackups.Length;

        return new LocalBackupEntryListResponse(
            Source: "control-plane",
            Status: warnings.Length == 0 ? "ok" : "warning",
            BackupsRootPath: catalog.BackupsRootPath,
            Query: query with { Page = page },
            Summary: summary,
            TotalBackups: totalBackups,
            Page: page,
            PageSize: query.PageSize,
            TotalPages: totalPages,
            HasPreviousPage: page > 1,
            HasNextPage: page < totalPages,
            AvailableStacks: availableStacks,
            Backups: pagedBackups,
            Warnings: warnings,
            Detail: $"Showing {firstItemNumber}-{lastItemNumber} of {totalBackups} local backup(s).");
    }

    public async Task<LocalBackupStackCatalogResponse> ListStackAsync(
        string stackSlug,
        CancellationToken ct)
    {
        var normalizedStackSlug = NormalizePathSegment(
            stackSlug,
            "Stack slug is required.");

        var backupsRoot = GetStackBackupsRoot();
        var stackDirectory = Path.Combine(backupsRoot, normalizedStackSlug);

        if (!Directory.Exists(stackDirectory))
        {
            throw new DirectoryNotFoundException(
                $"No backup history was found for stack '{normalizedStackSlug}'.");
        }

        var stackHistory = await InspectStackDirectoryAsync(
            normalizedStackSlug,
            stackDirectory,
            ct);

        return new LocalBackupStackCatalogResponse(
            Source: "control-plane",
            Status: "ok",
            BackupsRootPath: backupsRoot,
            Stack: stackHistory,
            Detail: null);
    }

    public async Task<LocalBackupDetailResponse> InspectAsync(
        string stackSlug,
        string backupId,
        CancellationToken ct)
    {
        var normalizedStackSlug = NormalizePathSegment(
            stackSlug,
            "Stack slug is required.");

        var normalizedBackupId = NormalizePathSegment(
            backupId,
            "Backup id is required.");

        var backupRoot = Path.Combine(
            GetStackBackupsRoot(),
            normalizedStackSlug,
            normalizedBackupId);

        if (!Directory.Exists(backupRoot))
        {
            throw new DirectoryNotFoundException(
                $"Backup '{normalizedBackupId}' was not found for stack '{normalizedStackSlug}'.");
        }

        var item = await InspectBackupDirectoryAsync(
            normalizedStackSlug,
            backupRoot,
            ct);

        var warnings = item.Warnings.ToList();

        var manifest = item.ManifestPresent
            ? await TryReadManifestAsync(
                Path.Combine(backupRoot, "backup-manifest.json"),
                warnings,
                ct)
            : null;

        return new LocalBackupDetailResponse(
            Source: "control-plane",
            Status: "ok",
            StackSlug: item.StackSlug,
            BackupId: item.BackupId,
            BackupRootPath: item.BackupRootPath,
            ManifestPath: item.ManifestPath,
            ManifestPresent: item.ManifestPresent,
            CreatedAtUtc: item.CreatedAtUtc,
            Manifest: manifest,
            Components: item.Components,
            TotalBytes: item.TotalBytes,
            TotalFiles: item.TotalFiles,
            Warnings: warnings,
            Detail: null);
    }

    /// <summary>
    /// Deletes exactly one local backup directory after explicit acknowledgement.
    /// The method deliberately does not remove portable exports, validated imports,
    /// or restore-session/audit history because they are separate artifacts.
    /// </summary>
    public async Task<LocalBackupDeleteResponse> DeleteAsync(
        string stackSlug,
        string backupId,
        bool acknowledgeDelete,
        CancellationToken ct)
    {
        if (!acknowledgeDelete)
        {
            throw new InvalidOperationException(
                "Deleting a local backup requires acknowledgeDelete=true.");
        }

        var normalizedStackSlug = NormalizePathSegment(
            stackSlug,
            "Stack slug is required.");
        var normalizedBackupId = NormalizePathSegment(
            backupId,
            "Backup id is required.");

        var activeRestore = await _restoreAttemptCoordinator.GetActiveLocalBackupAttemptAsync(
            normalizedStackSlug,
            normalizedBackupId,
            ct);

        if (activeRestore is not null)
        {
            throw new LocalBackupActiveRestoreConflictException(
                activeRestore.RestoreSessionId,
                $"This backup cannot be deleted while restore '{activeRestore.RestoreSessionId}' is active. " +
                "Open the restore to complete, cancel, or resolve it first.");
        }

        var backupsRoot = Path.GetFullPath(GetStackBackupsRoot());
        var stackDirectory = CombineDirectChildPath(
            backupsRoot,
            normalizedStackSlug,
            "Stack slug resolves outside the local backups root.");
        var backupRoot = CombineDirectChildPath(
            stackDirectory,
            normalizedBackupId,
            "Backup id resolves outside the stack backup directory.");

        if (!Directory.Exists(backupRoot))
        {
            throw new DirectoryNotFoundException(
                $"Backup '{normalizedBackupId}' was not found for stack '{normalizedStackSlug}'.");
        }

        EnsurePathIsNotReparsePoint(
            stackDirectory,
            "Stack backup directory cannot be a symbolic link or reparse point.");
        EnsureDeleteTreeContainsNoReparsePoints(backupRoot, ct);

        // Capture the size and file count before deletion so the caller can
        // confirm what was reclaimed without trusting an inferred estimate.
        var deletedBackup = await InspectBackupDirectoryAsync(
            normalizedStackSlug,
            backupRoot,
            ct);

        ct.ThrowIfCancellationRequested();

        Directory.Delete(backupRoot, recursive: true);

        if (Directory.Exists(backupRoot))
        {
            throw new IOException(
                $"Backup '{normalizedBackupId}' still exists after the delete operation.");
        }

        var warnings = new List<string>
        {
            "Portable exports, validated imports, and restore-session history were not changed."
        };

        var stackDirectoryRemoved = TryRemoveEmptyStackDirectory(
            stackDirectory,
            warnings);

        return new LocalBackupDeleteResponse(
            Source: "control-plane",
            Status: "deleted",
            StackSlug: deletedBackup.StackSlug,
            BackupId: deletedBackup.BackupId,
            BackupRootPath: deletedBackup.BackupRootPath,
            DeletedBytes: deletedBackup.TotalBytes,
            DeletedFiles: deletedBackup.TotalFiles,
            StackDirectoryRemoved: stackDirectoryRemoved,
            DeletedAtUtc: DateTimeOffset.UtcNow,
            Warnings: warnings,
            Detail: $"Deleted local backup '{deletedBackup.BackupId}' for stack '{deletedBackup.StackSlug}'.");
    }

    private async Task<LocalBackupCatalogStack> InspectStackDirectoryAsync(
        string stackSlug,
        string stackDirectory,
        CancellationToken ct)
    {
        var backups = new List<LocalBackupCatalogEntry>();
        var warnings = new List<string>();

        foreach (var backupDirectory in SafeEnumerateDirectories(stackDirectory))
        {
            ct.ThrowIfCancellationRequested();

            var backupId = Path.GetFileName(backupDirectory);

            if (string.IsNullOrWhiteSpace(backupId))
            {
                continue;
            }

            backups.Add(await InspectBackupDirectoryAsync(
                stackSlug,
                backupDirectory,
                ct));
        }

        var sortedBackups = backups
            .OrderByDescending(x => x.CreatedAtUtc ?? GetDirectoryWriteTimeUtc(x.BackupRootPath))
            .ThenByDescending(x => x.BackupId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (Directory.Exists(stackDirectory) && sortedBackups.Length == 0)
        {
            warnings.Add("Stack backup directory exists but contains no backup folders.");
        }

        var latest = sortedBackups.FirstOrDefault();

        return new LocalBackupCatalogStack(
            StackSlug: stackSlug,
            BackupCount: sortedBackups.Length,
            LatestBackupId: latest?.BackupId,
            LatestCreatedAtUtc: latest?.CreatedAtUtc,
            TotalBytes: sortedBackups.Sum(x => x.TotalBytes),
            TotalFiles: sortedBackups.Sum(x => x.TotalFiles),
            Backups: sortedBackups,
            Warnings: warnings);
    }

    private async Task<LocalBackupCatalogEntry> InspectBackupDirectoryAsync(
        string stackSlug,
        string backupRoot,
        CancellationToken ct)
    {
        var backupId = Path.GetFileName(backupRoot);
        var warnings = new List<string>();

        var manifestPath = Path.Combine(backupRoot, "backup-manifest.json");
        var manifestPresent = File.Exists(manifestPath);

        LocalBackupManifest? manifest = null;

        if (manifestPresent)
        {
            manifest = await TryReadManifestAsync(
                manifestPath,
                warnings,
                ct);
        }
        else
        {
            warnings.Add("backup-manifest.json is missing.");
        }

        var databaseDump = InspectFileComponent(
            backupRoot,
            "database/synapse.sql",
            "Synapse database dump is missing.",
            warnings);

        var matrixConfig = InspectFileComponent(
            backupRoot,
            "matrix/homeserver.yaml",
            "Matrix homeserver.yaml is missing.",
            warnings);

        var matrixSigningKey = InspectSigningKeyComponent(
            backupRoot,
            warnings);

        var mediaStore = InspectDirectoryComponent(
            backupRoot,
            "matrix/media_store",
            "Matrix media_store directory is missing.",
            warnings);

        var elementConfig = InspectFileComponent(
            backupRoot,
            "element/config.json",
            "Element config.json is missing.",
            warnings);

        foreach (var manifestWarning in manifest?.Warnings ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(manifestWarning) &&
                !warnings.Contains(manifestWarning, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add(manifestWarning);
            }
        }

        var rootStats = GetDirectoryStats(backupRoot);

        return new LocalBackupCatalogEntry(
            StackSlug: manifest?.StackSlug ?? stackSlug,
            BackupId: manifest?.BackupId ?? backupId,
            BackupRootPath: backupRoot,
            ManifestPath: manifestPresent ? manifestPath : null,
            ManifestPresent: manifestPresent,
            CreatedAtUtc: manifest?.CreatedAtUtc
                ?? TryParseBackupIdTimestamp(backupId)
                ?? GetDirectoryWriteTimeUtc(backupRoot),
            Components: new LocalBackupCatalogComponents(
                DatabaseDump: databaseDump,
                MatrixConfig: matrixConfig,
                MatrixSigningKey: matrixSigningKey,
                MatrixMediaStore: mediaStore,
                ElementConfig: elementConfig),
            TotalBytes: rootStats.Bytes,
            TotalFiles: rootStats.Files,
            Warnings: warnings);
    }

    private async Task<LocalBackupManifest?> TryReadManifestAsync(
        string manifestPath,
        List<string> warnings,
        CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(manifestPath);

            var manifest = await JsonSerializer.DeserializeAsync<LocalBackupManifest>(
                stream,
                ManifestJsonOptions,
                ct);

            if (manifest is null)
            {
                warnings.Add("backup-manifest.json could not be read.");
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            warnings.Add($"backup-manifest.json is invalid JSON: {ex.Message}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            warnings.Add($"backup-manifest.json could not be read: {ex.Message}");
            return null;
        }
    }

    private LocalBackupCatalogFileComponent InspectSigningKeyComponent(
        string backupRoot,
        List<string> warnings)
    {
        var matrixDirectory = Path.Combine(backupRoot, "matrix");

        var signingKeyPath = SafeEnumerateFiles(matrixDirectory, "*.signing.key")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        signingKeyPath ??= Path.Combine(matrixDirectory, "signing.key");

        var relativePath = Path.GetRelativePath(backupRoot, signingKeyPath)
            .Replace(Path.DirectorySeparatorChar, '/');

        var present = File.Exists(signingKeyPath);

        if (!present)
        {
            warnings.Add("Matrix signing key is missing. This is critical for homeserver identity restore.");
        }

        return new LocalBackupCatalogFileComponent(
            Present: present,
            RelativePath: relativePath,
            AbsolutePath: signingKeyPath,
            Bytes: GetFileSize(signingKeyPath));
    }

    private LocalBackupCatalogFileComponent InspectFileComponent(
        string backupRoot,
        string relativePath,
        string missingWarning,
        List<string> warnings)
    {
        var absolutePath = Path.Combine(
            backupRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var present = File.Exists(absolutePath);

        if (!present)
        {
            warnings.Add(missingWarning);
        }

        return new LocalBackupCatalogFileComponent(
            Present: present,
            RelativePath: relativePath,
            AbsolutePath: absolutePath,
            Bytes: GetFileSize(absolutePath));
    }

    private LocalBackupCatalogDirectoryComponent InspectDirectoryComponent(
        string backupRoot,
        string relativePath,
        string missingWarning,
        List<string> warnings)
    {
        var absolutePath = Path.Combine(
            backupRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));

        var present = Directory.Exists(absolutePath);
        var stats = GetDirectoryStats(absolutePath);

        if (!present)
        {
            warnings.Add(missingWarning);
        }

        return new LocalBackupCatalogDirectoryComponent(
            Present: present,
            RelativePath: relativePath,
            AbsolutePath: absolutePath,
            Bytes: stats.Bytes,
            Files: stats.Files);
    }

    private static LocalBackupEntryQuery NormalizeEntryQuery(
        LocalBackupEntryQuery request)
    {
        var pageSize = AllowedEntryPageSizes.Contains(request.PageSize)
            ? request.PageSize
            : 10;

        return request with
        {
            Page = Math.Max(1, request.Page),
            PageSize = pageSize,
            Search = NormalizeOptionalValue(request.Search),
            StackSlug = NormalizeOptionalValue(request.StackSlug),
            SortBy = NormalizeEntrySortBy(request.SortBy),
            SortDirection = NormalizeSortDirection(request.SortDirection)
        };
    }

    private static bool MatchesEntrySearch(
        LocalBackupCatalogEntry entry,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return string.Join(
                " ",
                entry.StackSlug,
                entry.BackupId)
            .Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntryStack(
        LocalBackupCatalogEntry entry,
        string? stackSlug) =>
        string.IsNullOrWhiteSpace(stackSlug) ||
        string.Equals(
            entry.StackSlug,
            stackSlug,
            StringComparison.OrdinalIgnoreCase);

    private static IOrderedEnumerable<LocalBackupCatalogEntry> ApplyEntrySort(
        IEnumerable<LocalBackupCatalogEntry> entries,
        string? sortBy,
        string? sortDirection)
    {
        var descending = !string.Equals(
            sortDirection,
            "asc",
            StringComparison.OrdinalIgnoreCase);

        return NormalizeEntrySortBy(sortBy) switch
        {
            "backup" => descending
                ? entries
                    .OrderByDescending(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(static entry => entry.StackSlug, StringComparer.OrdinalIgnoreCase)
                : entries
                    .OrderBy(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static entry => entry.StackSlug, StringComparer.OrdinalIgnoreCase),
            "stack" => descending
                ? entries
                    .OrderByDescending(static entry => entry.StackSlug, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(GetEntryCreatedAtUtc)
                    .ThenByDescending(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase)
                : entries
                    .OrderBy(static entry => entry.StackSlug, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(GetEntryCreatedAtUtc)
                    .ThenByDescending(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase),
            "size" => descending
                ? entries
                    .OrderByDescending(static entry => entry.TotalBytes)
                    .ThenByDescending(GetEntryCreatedAtUtc)
                    .ThenByDescending(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase)
                : entries
                    .OrderBy(static entry => entry.TotalBytes)
                    .ThenByDescending(GetEntryCreatedAtUtc)
                    .ThenByDescending(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? entries
                    .OrderByDescending(GetEntryCreatedAtUtc)
                    .ThenByDescending(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase)
                : entries
                    .OrderBy(GetEntryCreatedAtUtc)
                    .ThenBy(static entry => entry.BackupId, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static DateTime GetEntryCreatedAtUtc(
        LocalBackupCatalogEntry entry) =>
        entry.CreatedAtUtc ??
        GetDirectoryWriteTimeUtc(entry.BackupRootPath) ??
        DateTime.MinValue;

    private static string NormalizeEntrySortBy(
        string? value)
    {
        var normalized = NormalizeOptionalValue(value)?.ToLowerInvariant();

        return normalized is "backup" or "stack" or "size" or "created"
            ? normalized
            : "created";
    }

    private static string NormalizeSortDirection(
        string? value) =>
        string.Equals(value, "asc", StringComparison.OrdinalIgnoreCase)
            ? "asc"
            : "desc";

    private static string? NormalizeOptionalValue(
        string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private string GetStackBackupsRoot()
    {
        return Path.Combine(
            GetDataRoot(),
            "backups",
            "stacks");
    }

    private string GetDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string NormalizePathSegment(
        string value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        var normalized = value.Trim();

        if (normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar) ||
            normalized == "." ||
            normalized == "..")
        {
            throw new InvalidOperationException("Path traversal is not allowed.");
        }

        return normalized;
    }

    private static string CombineDirectChildPath(
        string parentPath,
        string childSegment,
        string outsideParentMessage)
    {
        var normalizedParentPath = Path.GetFullPath(parentPath);
        var candidatePath = Path.GetFullPath(
            Path.Combine(normalizedParentPath, childSegment));
        var parentPrefix = normalizedParentPath.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParentPath
            : normalizedParentPath + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!candidatePath.StartsWith(parentPrefix, comparison))
        {
            throw new InvalidOperationException(outsideParentMessage);
        }

        return candidatePath;
    }

    private static void EnsureDeleteTreeContainsNoReparsePoints(
        string backupRoot,
        CancellationToken ct)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(backupRoot);

        while (pendingDirectories.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var directory = pendingDirectories.Pop();
            EnsurePathIsNotReparsePoint(
                directory,
                "Backup deletion refuses symbolic links or reparse points.");

            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();

                EnsurePathIsNotReparsePoint(
                    entry,
                    "Backup deletion refuses symbolic links or reparse points.");

                if (Directory.Exists(entry))
                {
                    pendingDirectories.Push(entry);
                }
            }
        }
    }

    private static void EnsurePathIsNotReparsePoint(
        string path,
        string message)
    {
        try
        {
            var attributes = File.GetAttributes(path);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(message);
            }
        }
        catch (FileNotFoundException)
        {
            throw new DirectoryNotFoundException(
                $"Backup path '{path}' was not found.");
        }
    }

    private static bool TryRemoveEmptyStackDirectory(
        string stackDirectory,
        List<string> warnings)
    {
        if (!Directory.Exists(stackDirectory))
        {
            return false;
        }

        try
        {
            EnsurePathIsNotReparsePoint(
                stackDirectory,
                "Stack backup directory cannot be a symbolic link or reparse point.");

            if (Directory.EnumerateFileSystemEntries(
                    stackDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Any())
            {
                return false;
            }

            Directory.Delete(stackDirectory, recursive: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            warnings.Add(
                $"Backup was deleted, but the now-empty stack backup directory could not be removed: {ex.Message}");
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(
        string path)
    {
        if (!Directory.Exists(path))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetDirectories(path);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(
        string path,
        string searchPattern)
    {
        if (!Directory.Exists(path))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static (long Bytes, long Files) GetDirectoryStats(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return (0, 0);
        }

        long bytes = 0;
        long files = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                path,
                "*",
                SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);

                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    bytes += info.Length;
                    files++;
                }
                catch
                {
                    // Ignore individual unreadable files for backup history v1.
                }
            }
        }
        catch
        {
            // Ignore unreadable directory for backup history v1.
        }

        return (bytes, files);
    }

    private static long GetFileSize(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static DateTime? TryParseBackupIdTimestamp(
        string backupId)
    {
        if (DateTime.TryParseExact(
                backupId,
                "yyyyMMdd-HHmmss'Z'",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTime? GetDirectoryWriteTimeUtc(
        string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.GetLastWriteTimeUtc(path)
                : null;
        }
        catch
        {
            return null;
        }
    }
}

public sealed class LocalBackupActiveRestoreConflictException : IOException
{
    public LocalBackupActiveRestoreConflictException(string restoreSessionId, string message)
        : base(message)
    {
        RestoreSessionId = restoreSessionId;
    }

    public string RestoreSessionId { get; }
}
