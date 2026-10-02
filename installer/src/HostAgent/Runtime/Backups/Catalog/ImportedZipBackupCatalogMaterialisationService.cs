using System.IO.Compression;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Converts a validated retained uploaded ZIP into the canonical managed
/// Backup Catalog payload directory. The upload archive remains an independent
/// source artifact; this service neither deletes it nor changes its retention.
/// </summary>
public sealed class ImportedZipBackupCatalogMaterialisationService
{
    private const string ExportRoot = "mem-stack-export/";

    private readonly IConfiguration _configuration;
    private readonly ImportValidationService _validationService;
    private readonly BackupCatalogStore _catalogStore;

    public ImportedZipBackupCatalogMaterialisationService(
        IConfiguration configuration,
        ImportValidationService validationService,
        BackupCatalogStore catalogStore)
    {
        _configuration = configuration;
        _validationService = validationService;
        _catalogStore = catalogStore;
    }

    public async Task<BackupCatalogImportedMaterialisationResponse> MaterialiseAsync(
        string validationId,
        CancellationToken ct)
    {
        var normalizedValidationId = RequireSafeValidationId(validationId);
        var validation = await _validationService.GetValidationAsync(
            normalizedValidationId,
            ct);

        if (validation is null)
        {
            throw new DirectoryNotFoundException(
                $"Uploaded ZIP '{normalizedValidationId}' was not found.");
        }

        if (!string.Equals(validation.Status, "valid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only a valid uploaded ZIP may be materialised into the Backup Catalog.");
        }

        var integrity = ImportedZipCatalogIntegrityClassifier.Classify(validation)
            ?? throw new InvalidOperationException(
                "Only a valid uploaded ZIP may be materialised into the Backup Catalog.");

        var registration = CreateRegistration(validation, integrity);
        var provisionalPayloadDirectory = ResolveManagedPayloadDirectory(
            normalizedValidationId);

        var start = await _catalogStore.StartImportedMaterialisationAsync(
            registration,
            provisionalPayloadDirectory,
            ct);

        if (start.PayloadState == BackupCatalogPayloadStates.Available)
        {
            return new BackupCatalogImportedMaterialisationResponse(
                Source: "control-plane",
                Status: "ok",
                ValidationId: normalizedValidationId,
                CatalogEntryId: start.CatalogEntryId,
                Action: start.Action,
                PayloadState: start.PayloadState,
                IntegrityStatus: integrity.IntegrityStatus,
                WarningCount: integrity.IntegrityWarningCount,
                PayloadBytes: null,
                MaterialisedAtUtc: null,
                Detail: "The uploaded ZIP is already materialised in the Backup Catalog.",
                AdvisoryCount: integrity.Advisories.Count);
        }

        if (start.PayloadState == BackupCatalogPayloadStates.Removed)
        {
            return new BackupCatalogImportedMaterialisationResponse(
                "control-plane", "ok", normalizedValidationId, start.CatalogEntryId,
                start.Action, start.PayloadState, BackupCatalogIntegrityStatuses.Unknown,
                0, null, null,
                "The catalog payload was intentionally removed and was not reactivated.",
                integrity.Advisories.Count);
        }

        try
        {
            var materialised = await ExtractToManagedPayloadAsync(
                validation.StoredZipPath,
                start.PayloadDirectoryPath,
                ct);

            await _catalogStore.CompleteImportedMaterialisationAsync(
                start.CatalogEntryId,
                materialised.PayloadBytes,
                ct);

            return new BackupCatalogImportedMaterialisationResponse(
                Source: "control-plane",
                Status: "ok",
                ValidationId: normalizedValidationId,
                CatalogEntryId: start.CatalogEntryId,
                Action: start.Action,
                PayloadState: BackupCatalogPayloadStates.Available,
                IntegrityStatus: registration.IntegrityStatus,
                WarningCount: registration.WarningCount,
                PayloadBytes: materialised.PayloadBytes,
                MaterialisedAtUtc: materialised.MaterialisedAtUtc,
                Detail: $"Materialised {materialised.FileCount} archive file(s) into the Backup Catalog.",
                AdvisoryCount: integrity.Advisories.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            await _catalogStore.FailImportedMaterialisationAsync(
                start.CatalogEntryId,
                $"Materialisation failed: {ex.Message}",
                ct);

            throw;
        }
    }

    private async Task<MaterialisedPayload> ExtractToManagedPayloadAsync(
        string storedZipPath,
        string finalPayloadDirectory,
        CancellationToken ct)
    {
        if (!File.Exists(storedZipPath))
        {
            throw new FileNotFoundException(
                "The retained uploaded ZIP archive is no longer available.",
                storedZipPath);
        }

        var finalPath = Path.GetFullPath(finalPayloadDirectory);
        var parentDirectory = Directory.GetParent(finalPath)?.FullName
            ?? throw new InvalidOperationException("Managed catalog payload path has no parent directory.");

        Directory.CreateDirectory(parentDirectory);

        var stagingDirectory = Path.Combine(
            parentDirectory,
            $".{Path.GetFileName(finalPath)}.materialising-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingDirectory);

            var totalBytes = 0L;
            var fileCount = 0;

            using var archive = ZipFile.OpenRead(storedZipPath);

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(entry.FullName))
                {
                    continue;
                }

                if (!entry.FullName.StartsWith(ExportRoot, StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Uploaded ZIP contains content outside the expected mem-stack-export root.");
                }

                var relativePath = entry.FullName[ExportRoot.Length..];

                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                if (Path.IsPathRooted(relativePath) ||
                    relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Any(segment => segment is "." or ".."))
                {
                    throw new InvalidDataException(
                        "Uploaded ZIP contains an unsafe extraction path.");
                }

                var destinationPath = Path.GetFullPath(Path.Combine(
                    stagingDirectory,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));

                if (!destinationPath.StartsWith(
                        stagingDirectory + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Uploaded ZIP entry resolves outside its managed payload directory.");
                }

                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                await using var source = entry.Open();
                await using var destination = File.Create(destinationPath);
                await source.CopyToAsync(destination, ct);

                totalBytes += entry.Length;
                fileCount++;
            }

            if (fileCount == 0)
            {
                throw new InvalidDataException(
                    "Uploaded ZIP contains no materialisable export files.");
            }

            if (Directory.Exists(finalPath))
            {
                Directory.Delete(finalPath, recursive: true);
            }

            Directory.Move(stagingDirectory, finalPath);

            return new MaterialisedPayload(
                PayloadBytes: totalBytes,
                FileCount: fileCount,
                MaterialisedAtUtc: DateTime.UtcNow);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private BackupCatalogImportedMaterialisationRegistration CreateRegistration(
        ImportValidationResponse validation,
        ImportedZipCatalogIntegrityPresentation integrity)
    {
        var manifest = validation.Manifest;
        var stack = manifest?.Stack;
        var routes = manifest?.Routes;

        var displayName = !string.IsNullOrWhiteSpace(stack?.DisplayName)
            ? $"Imported backup {stack.DisplayName}"
            : !string.IsNullOrWhiteSpace(stack?.Slug)
                ? $"Imported backup {stack.Slug}"
                : $"Imported backup {validation.ValidationId}";

        return new BackupCatalogImportedMaterialisationRegistration(
            validation.ValidationId,
            displayName,
            stack?.Slug,
            manifest?.ManifestVersion,
            manifest?.MemVersion,
            stack?.MatrixServerName,
            routes?.MatrixHost,
            routes?.ElementHost,
            DateTime.UtcNow,
            integrity.IntegrityStatus,
            integrity.IntegritySummary,
            integrity.IntegrityWarningCount);
    }

    private string ResolveManagedPayloadDirectory(string validationId) =>
        Path.Combine(
            ResolveDataRoot(),
            "backups",
            "catalog",
            "imported",
            validationId,
            "payload");

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string RequireSafeValidationId(string value)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar) ||
            normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Validation id is invalid.");
        }

        return normalized;
    }

    private sealed record MaterialisedPayload(
        long PayloadBytes,
        int FileCount,
        DateTime MaterialisedAtUtc);
}
