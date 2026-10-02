using System.Text.Json;
using HostAgent.Runtime.Backups.Coordination;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

/// <summary>
/// Manages the retained archive portion of a validated external import.
/// It deliberately does not own restore-attempt records, evidence, logs,
/// support reports, or runtime stacks. Removing an archive leaves those
/// durable operator/audit records in place.
/// </summary>
public sealed class ValidatedImportArtifactService
{
    private const string ValidationSnapshotFileName = "validation-result.json";
    private const string RemovalTombstoneFileName = "source-removal.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly ImportValidationService _validationService;

    public ValidatedImportArtifactService(
        IConfiguration configuration,
        ImportValidationService validationService)
    {
        _configuration = configuration;
        _validationService = validationService;
    }

    public async Task<ValidatedImportArtifactDetailResponse> InspectAsync(
        string validationId,
        CancellationToken ct)
    {
        var normalizedValidationId = NormalizeValidationId(validationId);
        var importDirectory = ResolveImportDirectory(normalizedValidationId);

        if (!Directory.Exists(importDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Uploaded ZIP '{normalizedValidationId}' was not found.");
        }

        return await InspectDirectoryAsync(
            normalizedValidationId,
            importDirectory,
            ct);
    }

    /// <summary>
    /// Lists source-artifact records from the uploaded-import root. This is a
    /// source catalogue, not a Restore Session ledger: invalid uploads, retained
    /// archives, and explicit source-removal tombstones can all be represented.
    /// No host filesystem paths are returned.
    /// </summary>
    public async Task<ValidatedImportArtifactInventoryResponse> ListInventoryAsync(
        CancellationToken ct)
    {
        var uploadsRoot = ResolveUploadsRoot();

        if (!Directory.Exists(uploadsRoot))
        {
            return new ValidatedImportArtifactInventoryResponse(
                Source: "control-plane",
                Status: "ok",
                TotalImports: 0,
                Imports: Array.Empty<ValidatedImportArtifactDetailResponse>(),
                Warnings: Array.Empty<string>(),
                Detail: "No uploaded ZIP sources have been retained yet.");
        }

        EnsurePathIsNotReparsePoint(
            uploadsRoot,
            "Uploaded ZIP root cannot be a symbolic link or reparse point.");

        var warnings = new List<string>();
        var directories = new List<DirectoryInfo>();

        foreach (var path in Directory.EnumerateDirectories(
                     uploadsRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();

            var directory = new DirectoryInfo(path);

            try
            {
                _ = NormalizeValidationId(directory.Name);
                EnsurePathIsNotReparsePoint(
                    directory.FullName,
                    "Uploaded ZIP directory cannot be a symbolic link or reparse point.");

                directories.Add(directory);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                warnings.Add(
                    $"Skipped uploaded import directory '{directory.Name}': {ex.Message}");
            }
        }

        var orderedDirectories = directories
            .OrderByDescending(directory => directory.LastWriteTimeUtc)
            .ThenBy(directory => directory.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();


        var imports = new List<ValidatedImportArtifactDetailResponse>();

        foreach (var directory in orderedDirectories)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                imports.Add(await InspectDirectoryAsync(
                    directory.Name,
                    directory.FullName,
                    ct));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            {
                warnings.Add(
                    $"Could not read uploaded import '{directory.Name}': {ex.Message}");
            }
        }

        var distinctWarnings = warnings
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new ValidatedImportArtifactInventoryResponse(
            Source: "control-plane",
            Status: distinctWarnings.Length == 0 ? "ok" : "warning",
            TotalImports: imports.Count,
            Imports: imports,
            Warnings: distinctWarnings,
            Detail: $"Found {imports.Count} uploaded ZIP source record(s).");
    }

    private async Task<ValidatedImportArtifactDetailResponse> InspectDirectoryAsync(
        string normalizedValidationId,
        string importDirectory,
        CancellationToken ct)
    {
        if (!Directory.Exists(importDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Uploaded ZIP '{normalizedValidationId}' was not found.");
        }

        EnsurePathIsNotReparsePoint(
            importDirectory,
            "Uploaded ZIP directory cannot be a symbolic link or reparse point.");

        var validation = await LoadValidationAsync(normalizedValidationId, ct);
        var tombstone = await ReadRemovalTombstoneAsync(importDirectory, ct);
        var archive = ResolveArchive(importDirectory, validation, tombstone);

        var validationSummary = ToValidationSummary(validation);
        var warnings = BuildWarnings(validation, archive, tombstone);
        var retention = BuildRetention(archive, tombstone);

        return new ValidatedImportArtifactDetailResponse(
            Source: "control-plane",
            Status: archive.State == ArchiveStates.Removed ? "removed" : "ok",
            ValidationId: normalizedValidationId,
            SourceKind: "uploaded-zip",
            UploadedFileName: validation?.UploadedFileName ?? archive.FileName,
            RecordedAtUtc: GetRecordedAtUtc(importDirectory),
            ArchiveBytes: archive.File?.Length ?? tombstone?.RemovedBytes,
            ArchiveState: archive.State,
            Validation: validationSummary,
            Manifest: ToManifestSummary(validation?.Manifest),
            Retention: retention,
            Warnings: warnings,
            Detail: BuildDetail(archive, tombstone, validation));
    }

    public async Task<ValidatedImportArtifactDeleteResponse> DeleteAsync(
        string validationId,
        bool acknowledgeDelete,
        CancellationToken ct)
    {
        if (!acknowledgeDelete)
        {
            throw new InvalidOperationException(
                "Deleting an uploaded ZIP requires acknowledgeDelete=true.");
        }

        var normalizedValidationId = NormalizeValidationId(validationId);
        var importDirectory = ResolveImportDirectory(normalizedValidationId);

        if (!Directory.Exists(importDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Uploaded ZIP '{normalizedValidationId}' was not found.");
        }

        EnsurePathIsNotReparsePoint(
            importDirectory,
            "Uploaded ZIP directory cannot be a symbolic link or reparse point.");

        var validation = await LoadValidationAsync(normalizedValidationId, ct);
        var tombstone = await ReadRemovalTombstoneAsync(importDirectory, ct);
        var archive = ResolveArchive(importDirectory, validation, tombstone);
        if (archive.State == ArchiveStates.Removed)
        {
            return new ValidatedImportArtifactDeleteResponse(
                Source: "control-plane",
                Status: "already-removed",
                ValidationId: normalizedValidationId,
                ArchiveState: ArchiveStates.Removed,
                DeletedBytes: 0,
                DeletedAtUtc: tombstone?.RemovedAtUtc,
                Warnings:
                [
                    "The retained archive was already removed. The materialised Backup Catalog payload was not changed."
                ],
                Detail: "No archive file was deleted because this uploaded ZIP was already removed.");
        }

        if (archive.State == ArchiveStates.Missing)
        {
            throw new IOException(
                "The retained archive is missing. Restore history was not changed.");
        }

        if (archive.State == ArchiveStates.Ambiguous)
        {
            throw new IOException(
                "The uploaded ZIP directory contains multiple possible archive files. The source was not deleted.");
        }

        if (archive.File is null)
        {
            throw new IOException("The retained archive could not be resolved safely.");
        }


        EnsurePathIsNotReparsePoint(
            archive.File.FullName,
            "Uploaded ZIP archive cannot be a symbolic link or reparse point.");

        var deletedBytes = archive.File.Length;
        var deletedAtUtc = DateTimeOffset.UtcNow;

        var removal = new ValidatedImportRemovalTombstone(
            State: ArchiveStates.Removed,
            RemovedAtUtc: deletedAtUtc,
            RemovedBy: "control-plane",
            OriginalFileName: validation?.UploadedFileName ?? archive.File.Name,
            RemovedBytes: deletedBytes);

        // Persist the durable removal record before deleting the archive. If the
        // process stops after the delete, the next operator can still see that the
        // source was intentionally removed rather than silently missing.
        ct.ThrowIfCancellationRequested();
        await WriteRemovalTombstoneAsync(importDirectory, removal, ct);

        try
        {
            File.Delete(archive.File.FullName);

            if (File.Exists(archive.File.FullName))
            {
                throw new IOException("The uploaded ZIP archive still exists after the delete operation.");
            }
        }
        catch
        {
            // When deletion failed and the source still exists, remove the
            // prewritten tombstone so the retained artifact remains actionable.
            if (File.Exists(archive.File.FullName))
            {
                TryDeleteRemovalTombstone(importDirectory);
            }

            throw;
        }

        return new ValidatedImportArtifactDeleteResponse(
            Source: "control-plane",
            Status: "deleted",
            ValidationId: normalizedValidationId,
            ArchiveState: ArchiveStates.Removed,
            DeletedBytes: deletedBytes,
            DeletedAtUtc: deletedAtUtc,
            Warnings:
            [
                "The retained uploaded archive was removed.",
                "The materialised Backup Catalog payload, restore history, and restored stacks were not changed.",
                "Future restores continue to start from the managed Backup Catalog item, not this retained archive."
            ],
            Detail: $"Deleted uploaded ZIP '{removal.OriginalFileName}'.");
    }

    /// <summary>
    /// Internal archive-state projection retained for legacy advanced-cutover
    /// history readers. Canonical Restore Workspace actions resolve catalog
    /// payloads and do not call this method.
    /// </summary>
    public async Task<ValidatedImportSourceAvailability> GetSourceAvailabilityAsync(
        string validationId,
        CancellationToken ct)
    {
        var detail = await InspectAsync(validationId, ct);

        var canContinueRestore = string.Equals(
            detail.ArchiveState,
            ArchiveStates.Retained,
            StringComparison.OrdinalIgnoreCase);

        return new ValidatedImportSourceAvailability(
            ArchiveState: detail.ArchiveState,
            CanContinueRestore: canContinueRestore,
            Detail: canContinueRestore
                ? null
                : "The retained uploaded archive was removed. Upload the ZIP again before continuing that archived workflow.",
            RemovedAtUtc: detail.Retention.RemovedAtUtc);
    }

    /// <summary>
    /// Guards only legacy internal history readers that still inspect a retained
    /// upload archive. New catalog-backed restore commands must never call it.
    /// </summary>
    public async Task EnsureRestoreCanContinueAsync(
        string validationId,
        CancellationToken ct)
    {
        var normalizedValidationId = NormalizeValidationId(validationId);
        var availability = await GetSourceAvailabilityAsync(
            normalizedValidationId,
            ct);

        if (string.Equals(
                availability.ArchiveState,
                ArchiveStates.Removed,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UploadedSourceRemovedException(normalizedValidationId);
        }
    }

    private async Task<ImportValidationResponse?> LoadValidationAsync(
        string validationId,
        CancellationToken ct)
    {
        var cached = await _validationService.GetCachedValidationAsync(validationId, ct);
        if (cached is not null)
        {
            return cached;
        }

        // Existing import directories normally contain a .zip archive. This
        // fallback rebuilds an absent/corrupt receipt without creating a new
        // validation id. Older non-.zip filename uploads remain inspectable
        // through their retained archive and may report an unknown validation
        // summary until the later storage-normalisation slice migrates them.
        return await _validationService.GetValidationAsync(validationId, ct);
    }

    private static ValidatedImportRetentionSummary BuildRetention(
        ResolvedArchive archive,
        ValidatedImportRemovalTombstone? tombstone)
    {
        if (archive.State == ArchiveStates.Removed)
        {
            return new ValidatedImportRetentionSummary(
                CanDelete: false,
                DeleteBlockReason: "The retained archive was already removed.",
                RemovedAtUtc: tombstone?.RemovedAtUtc,
                RemovedBy: tombstone?.RemovedBy);
        }

        if (archive.State == ArchiveStates.Missing)
        {
            return new ValidatedImportRetentionSummary(
                CanDelete: false,
                DeleteBlockReason: "The retained archive is missing. Restore history remains available.",
                RemovedAtUtc: tombstone?.RemovedAtUtc,
                RemovedBy: tombstone?.RemovedBy);
        }

        if (archive.State == ArchiveStates.Ambiguous)
        {
            return new ValidatedImportRetentionSummary(
                CanDelete: false,
                DeleteBlockReason: "Multiple possible archive files were found. Resolve the storage state before deleting.",
                RemovedAtUtc: tombstone?.RemovedAtUtc,
                RemovedBy: tombstone?.RemovedBy);
        }


        return new ValidatedImportRetentionSummary(
            CanDelete: true,
            DeleteBlockReason: null,
            RemovedAtUtc: tombstone?.RemovedAtUtc,
            RemovedBy: tombstone?.RemovedBy);
    }

    private static ValidatedImportValidationSummary ToValidationSummary(
        ImportValidationResponse? validation)
    {
        if (validation is null)
        {
            return new ValidatedImportValidationSummary(
                Status: "unknown",
                Summary: "No readable validation receipt is retained for this import.",
                ZipEntryCount: 0,
                TotalUncompressedBytes: 0,
                ManifestPresent: false,
                ChecksumsPresent: false,
                PassedChecks: 0,
                FailedChecks: 0,
                WarningCount: 0,
                Errors: []);
        }

        var passedChecks = validation.Checks.Count(check => check.Passed);
        var failedChecks = validation.Checks.Count(check => !check.Passed);
        var status = validation.Status;
        var summary = string.Equals(status, "valid", StringComparison.OrdinalIgnoreCase)
            ? "Manifest and checksum validation passed."
            : validation.Errors.Count > 0
                ? "Validation did not pass. Review the recorded validation errors."
                : "Validation receipt was recorded without a passing result.";

        return new ValidatedImportValidationSummary(
            Status: status,
            Summary: summary,
            ZipEntryCount: validation.ZipEntryCount,
            TotalUncompressedBytes: validation.TotalUncompressedBytes,
            ManifestPresent: validation.ManifestPresent,
            ChecksumsPresent: validation.ChecksumsPresent,
            PassedChecks: passedChecks,
            FailedChecks: failedChecks,
            WarningCount: validation.Warnings.Count,
            Errors: validation.Errors);
    }

    private static ValidatedImportManifestSummary? ToManifestSummary(
        MemStackExportManifestSummary? manifest)
    {
        if (manifest is null)
        {
            return null;
        }

        return new ValidatedImportManifestSummary(
            ManifestVersion: manifest.ManifestVersion,
            MemVersion: manifest.MemVersion,
            SourceStackSlug: manifest.Stack.Slug,
            SourceStackDisplayName: manifest.Stack.DisplayName,
            MatrixServerName: manifest.Stack.MatrixServerName,
            IncludedFileCount: manifest.IncludedFiles.Count);
    }

    private static IReadOnlyList<string> BuildWarnings(
        ImportValidationResponse? validation,
        ResolvedArchive archive,
        ValidatedImportRemovalTombstone? tombstone)
    {
        var warnings = new List<string>();

        if (validation is not null)
        {
            warnings.AddRange(validation.Warnings.Where(warning => !string.IsNullOrWhiteSpace(warning)));
        }

        if (archive.State == ArchiveStates.Removed)
        {
            warnings.Add("The retained uploaded archive was removed. Historical restore records remain available.");
        }
        else if (archive.State == ArchiveStates.Missing)
        {
            warnings.Add("No retained archive file could be found. Historical restore records remain available.");
        }
        else if (archive.State == ArchiveStates.Ambiguous)
        {
            warnings.Add("Multiple possible archive files were found. No archive action was performed.");
        }

        if (tombstone is not null && archive.State != ArchiveStates.Removed)
        {
            warnings.Add("A source-removal record exists alongside a retained archive. Review this import before acting on it.");
        }

        return warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? BuildDetail(
        ResolvedArchive archive,
        ValidatedImportRemovalTombstone? tombstone,
        ImportValidationResponse? validation)
    {
        if (archive.State == ArchiveStates.Removed)
        {
            return tombstone is null
                ? "The retained archive was removed; the removal record is unavailable."
                : "The retained archive was removed. Validation receipts and restore-session history were retained.";
        }

        if (archive.State == ArchiveStates.Missing)
        {
            return "No retained archive file could be found for this validation id.";
        }

        if (archive.State == ArchiveStates.Ambiguous)
        {
            return "Multiple possible archive files were found for this validation id.";
        }

        if (validation is null)
        {
            return "A retained archive exists, but no readable validation receipt is available.";
        }

        return null;
    }

    private ResolvedArchive ResolveArchive(
        string importDirectory,
        ImportValidationResponse? validation,
        ValidatedImportRemovalTombstone? tombstone)
    {
        var tombstonePath = Path.Combine(importDirectory, RemovalTombstoneFileName);
        var snapshotPath = Path.Combine(importDirectory, ValidationSnapshotFileName);
        var expectedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(validation?.StoredZipPath))
        {
            expectedFileNames.Add(Path.GetFileName(validation.StoredZipPath));
        }

        if (!string.IsNullOrWhiteSpace(validation?.UploadedFileName))
        {
            expectedFileNames.Add(Path.GetFileName(validation.UploadedFileName));
        }

        var candidates = Directory
            .EnumerateFiles(importDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(path, snapshotPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, tombstonePath, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .Where(file => !file.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var expectedFileName in expectedFileNames)
        {
            var match = candidates.FirstOrDefault(file =>
                string.Equals(file.Name, expectedFileName, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                EnsurePathIsNotReparsePoint(
                    match.FullName,
                    "Uploaded ZIP archive cannot be a symbolic link or reparse point.");

                return new ResolvedArchive(ArchiveStates.Retained, match.Name, match);
            }
        }

        if (candidates.Length == 0)
        {
            return tombstone is not null &&
                   string.Equals(tombstone.State, ArchiveStates.Removed, StringComparison.OrdinalIgnoreCase)
                ? new ResolvedArchive(ArchiveStates.Removed, tombstone.OriginalFileName, null)
                : new ResolvedArchive(ArchiveStates.Missing, null, null);
        }

        if (candidates.Length == 1)
        {
            var archive = candidates[0];
            EnsurePathIsNotReparsePoint(
                archive.FullName,
                "Uploaded ZIP archive cannot be a symbolic link or reparse point.");

            return new ResolvedArchive(ArchiveStates.Retained, archive.Name, archive);
        }

        return new ResolvedArchive(ArchiveStates.Ambiguous, null, null);
    }

    private async Task<ValidatedImportRemovalTombstone?> ReadRemovalTombstoneAsync(
        string importDirectory,
        CancellationToken ct)
    {
        var tombstonePath = Path.Combine(importDirectory, RemovalTombstoneFileName);

        if (!File.Exists(tombstonePath))
        {
            return null;
        }

        EnsurePathIsNotReparsePoint(
            tombstonePath,
            "Uploaded ZIP removal record cannot be a symbolic link or reparse point.");

        try
        {
            await using var stream = File.OpenRead(tombstonePath);
            return await JsonSerializer.DeserializeAsync<ValidatedImportRemovalTombstone>(
                stream,
                JsonOptions,
                ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void TryDeleteRemovalTombstone(string importDirectory)
    {
        var tombstonePath = Path.Combine(importDirectory, RemovalTombstoneFileName);

        try
        {
            if (File.Exists(tombstonePath))
            {
                EnsurePathIsNotReparsePoint(
                    tombstonePath,
                    "Uploaded ZIP removal record cannot be a symbolic link or reparse point.");
                File.Delete(tombstonePath);
            }
        }
        catch (IOException)
        {
            // Keep the original delete failure as the response. A retained
            // archive plus a tombstone is surfaced as a warning on inspection.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the original delete failure as the response. A retained
            // archive plus a tombstone is surfaced as a warning on inspection.
        }
    }

    private async Task WriteRemovalTombstoneAsync(
        string importDirectory,
        ValidatedImportRemovalTombstone tombstone,
        CancellationToken ct)
    {
        var destinationPath = Path.Combine(importDirectory, RemovalTombstoneFileName);
        var temporaryPath = destinationPath + ".tmp";

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, tombstone, JsonOptions, ct);
                await stream.FlushAsync(ct);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private string ResolveImportDirectory(string validationId)
    {
        var uploadsRoot = ResolveUploadsRoot();
        var importDirectory = Path.GetFullPath(Path.Combine(uploadsRoot, validationId));
        var requiredPrefix = uploadsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? uploadsRoot
            : uploadsRoot + Path.DirectorySeparatorChar;

        if (!importDirectory.StartsWith(requiredPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Validation id resolves outside the uploaded import root.");
        }

        return importDirectory;
    }

    private string ResolveUploadsRoot() =>
        Path.GetFullPath(Path.Combine(
            ResolveDataRoot(),
            "imports",
            "uploads"));

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string NormalizeValidationId(string validationId) =>
        RestoreSourceKeyFactory.NormalizePathSegment(
            validationId,
            "Validation id is required.");

    private static DateTimeOffset? GetRecordedAtUtc(string importDirectory)
    {
        var snapshotPath = Path.Combine(importDirectory, ValidationSnapshotFileName);
        var candidatePath = File.Exists(snapshotPath)
            ? snapshotPath
            : importDirectory;

        var writeTimeUtc = File.Exists(candidatePath)
            ? File.GetLastWriteTimeUtc(candidatePath)
            : Directory.GetLastWriteTimeUtc(candidatePath);

        return writeTimeUtc == DateTime.MinValue
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(writeTimeUtc, DateTimeKind.Utc));
    }

    private static void EnsurePathIsNotReparsePoint(string path, string message)
    {
        var attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(message);
        }
    }

    private static class ArchiveStates
    {
        public const string Retained = "retained";
        public const string Removed = "removed";
        public const string Missing = "missing";
        public const string Ambiguous = "ambiguous";
    }

    private sealed record ResolvedArchive(
        string State,
        string? FileName,
        FileInfo? File);

    private sealed record ValidatedImportRemovalTombstone(
        string State,
        DateTimeOffset RemovedAtUtc,
        string RemovedBy,
        string? OriginalFileName,
        long RemovedBytes);
}
