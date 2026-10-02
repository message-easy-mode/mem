using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using HostAgent.Runtime.Stacks.Identity;

namespace HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

public sealed class ImportValidationService
{
    private const string ExportRoot = "mem-stack-export/";
    private const string ManifestPath = "mem-stack-export/mem-export-manifest.json";
    private const string ChecksumsPath = "mem-stack-export/backup/checksums.sha256";
    private const string ValidationSnapshotFileName = "validation-result.json";
    private const string StoredArchiveFileName = "source.zip";

    private readonly IConfiguration _configuration;

    public ImportValidationService(
        IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<ImportValidationResponse> ValidateUploadAsync(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var validationId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'") +
                           "-" +
                           Guid.NewGuid().ToString("N")[..8];

        var checks = new List<ImportValidationCheck>();
        var warnings = new List<string>();
        var errors = new List<string>();

        if (file.Length <= 0)
        {
            errors.Add("Uploaded file is empty.");

            return BuildResponse(
                status: "invalid",
                validationId: validationId,
                uploadedFileName: file.FileName,
                storedZipPath: "",
                zipBytes: 0,
                zipEntryCount: 0,
                totalUncompressedBytes: 0,
                manifestPresent: false,
                checksumsPresent: false,
                manifest: null,
                integrity: new ImportValidationIntegritySummary(0, 0, 0, 0, 0),
                checks: checks,
                warnings: warnings,
                errors: errors,
                detail: "Uploaded file is empty.");
        }

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("Uploaded file does not use a .zip extension. Validation will continue anyway.");
        }

        var uploadDirectory = Path.Combine(
            ResolveDataRoot(),
            "imports",
            "uploads",
            validationId);

        Directory.CreateDirectory(uploadDirectory);

        // Keep the retained artifact under a canonical name. The original filename
        // remains in UploadedFileName/validation metadata, while downstream restore
        // services can always resolve a .zip archive even when the uploaded filename
        // did not include a .zip extension.
        var storedZipPath = Path.Combine(uploadDirectory, StoredArchiveFileName);

        await using (var output = File.Create(storedZipPath))
        await using (var input = file.OpenReadStream())
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        ImportValidationResponse response;

        try
        {
            response = await ValidateStoredZipAsync(
                validationId,
                file.FileName,
                storedZipPath,
                file.Length,
                checks,
                warnings,
                errors,
                cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            errors.Add("Uploaded file is not a valid ZIP archive.");

            response = BuildResponse(
                status: "invalid",
                validationId: validationId,
                uploadedFileName: file.FileName,
                storedZipPath: storedZipPath,
                zipBytes: file.Length,
                zipEntryCount: 0,
                totalUncompressedBytes: 0,
                manifestPresent: false,
                checksumsPresent: false,
                manifest: null,
                integrity: new ImportValidationIntegritySummary(0, 0, 0, 0, 0),
                checks: checks,
                warnings: warnings,
                errors: errors,
                detail: ex.Message);
        }

        await SaveValidationSnapshotAsync(
            uploadDirectory,
            response,
            cancellationToken);

        return response;
    }

    /// <summary>
    /// Reads the validation receipt captured when this import was first checked. This is intentionally
    /// a fast read model for restore history and UI navigation; destructive restore operations should
    /// still perform their own required safety checks against the immutable stored archive.
    /// </summary>
    public async Task<ImportValidationResponse?> GetCachedValidationAsync(
        string validationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(validationId) ||
            !IsSafePathSegment(validationId))
        {
            throw new InvalidOperationException("Validation id is invalid.");
        }

        var validationDirectory = Path.Combine(
            ResolveDataRoot(),
            "imports",
            "uploads",
            validationId);

        var snapshotPath = Path.Combine(
            validationDirectory,
            ValidationSnapshotFileName);

        if (!File.Exists(snapshotPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(snapshotPath);

            return await JsonSerializer.DeserializeAsync<ImportValidationResponse>(
                stream,
                JsonOptions(),
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            // A corrupt or partially-written receipt must never make a historical import disappear.
            // The caller falls back to a full archive validation and rewrites the receipt.
            return null;
        }
    }

    /// <summary>
    /// Re-runs validation against a ZIP already stored under this validation id.
    /// This lets the restore workflow rebuild its server-side summary after a browser refresh,
    /// without inventing a new validation id or relying on browser session storage.
    /// </summary>
    public async Task<ImportValidationResponse?> GetValidationAsync(
        string validationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(validationId) ||
            !IsSafePathSegment(validationId))
        {
            throw new InvalidOperationException("Validation id is invalid.");
        }

        var validationDirectory = Path.Combine(
            ResolveDataRoot(),
            "imports",
            "uploads",
            validationId);

        if (!Directory.Exists(validationDirectory))
        {
            return null;
        }

        var zipPath = Directory.EnumerateFiles(
                validationDirectory,
                "*.zip",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(zipPath) ||
            !File.Exists(zipPath))
        {
            return null;
        }

        var fileInfo = new FileInfo(zipPath);
        var checks = new List<ImportValidationCheck>();
        var warnings = new List<string>();
        var errors = new List<string>();

        ImportValidationResponse response;

        try
        {
            response = await ValidateStoredZipAsync(
                validationId,
                Path.GetFileName(zipPath),
                zipPath,
                fileInfo.Length,
                checks,
                warnings,
                errors,
                cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            errors.Add("Stored import ZIP is not a valid ZIP archive.");

            response = BuildResponse(
                status: "invalid",
                validationId: validationId,
                uploadedFileName: Path.GetFileName(zipPath),
                storedZipPath: zipPath,
                zipBytes: fileInfo.Length,
                zipEntryCount: 0,
                totalUncompressedBytes: 0,
                manifestPresent: false,
                checksumsPresent: false,
                manifest: null,
                integrity: new ImportValidationIntegritySummary(0, 0, 0, 0, 0),
                checks: checks,
                warnings: warnings,
                errors: errors,
                detail: ex.Message);
        }

        await SaveValidationSnapshotAsync(
            validationDirectory,
            response,
            cancellationToken);

        return response;
    }

    private async Task<ImportValidationResponse> ValidateStoredZipAsync(
        string validationId,
        string uploadedFileName,
        string storedZipPath,
        long zipBytes,
        List<ImportValidationCheck> checks,
        List<string> warnings,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(storedZipPath);

        var fileEntries = archive.Entries
            .Where(entry => !IsDirectoryEntry(entry))
            .ToList();

        var totalUncompressedBytes = fileEntries.Sum(entry => entry.Length);

        AddCheck(
            checks,
            errors,
            code: "zip.opens",
            severity: "error",
            passed: true,
            message: "ZIP archive opens successfully.",
            detail: null);

        var unsafeEntries = fileEntries
            .Select(entry => entry.FullName)
            .Where(entryName => !IsSafeZipPath(entryName))
            .ToList();

        AddCheck(
            checks,
            errors,
            code: "zip.paths.safe",
            severity: "error",
            passed: unsafeEntries.Count == 0,
            message: unsafeEntries.Count == 0
                ? "ZIP entry paths are safe."
                : "ZIP contains unsafe entry paths.",
            detail: unsafeEntries.Count == 0
                ? null
                : string.Join(Environment.NewLine, unsafeEntries));

        var nonExportRootEntries = fileEntries
            .Select(entry => NormalizeZipPath(entry.FullName))
            .Where(entryName => !entryName.StartsWith(ExportRoot, StringComparison.Ordinal))
            .ToList();

        AddCheck(
            checks,
            errors,
            code: "zip.root",
            severity: "error",
            passed: nonExportRootEntries.Count == 0,
            message: nonExportRootEntries.Count == 0
                ? "ZIP uses the expected mem-stack-export/ root."
                : "ZIP contains files outside mem-stack-export/.",
            detail: nonExportRootEntries.Count == 0
                ? null
                : string.Join(Environment.NewLine, nonExportRootEntries));

        var entriesByName = fileEntries
            .GroupBy(
                entry => NormalizeZipPath(entry.FullName),
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.Ordinal);

        var manifestPresent = entriesByName.TryGetValue(
            ManifestPath,
            out var manifestEntry);

        var checksumsPresent = entriesByName.TryGetValue(
            ChecksumsPath,
            out var checksumsEntry);

        AddCheck(
            checks,
            errors,
            code: "manifest.present",
            severity: "error",
            passed: manifestPresent,
            message: manifestPresent
                ? "mem-export-manifest.json is present."
                : "mem-export-manifest.json is missing.",
            detail: ManifestPath);

        AddCheck(
            checks,
            errors,
            code: "checksums.present",
            severity: "error",
            passed: checksumsPresent,
            message: checksumsPresent
                ? "checksums.sha256 is present."
                : "checksums.sha256 is missing.",
            detail: ChecksumsPath);

        MemStackExportManifestFile? manifestFile = null;
        MemStackExportManifestSummary? manifestSummary = null;

        if (manifestEntry is not null)
        {
            await using var manifestStream = manifestEntry.Open();

            manifestFile = await JsonSerializer.DeserializeAsync<MemStackExportManifestFile>(
                manifestStream,
                JsonOptions(),
                cancellationToken);

            manifestSummary = ToSummary(manifestFile);

            ValidateManifest(
                manifestFile,
                entriesByName,
                checks,
                warnings,
                errors);

            if (manifestFile is not null)
            {
                await ValidateTopologyMetadataAsync(
                    manifestFile,
                    entriesByName,
                    checks,
                    warnings,
                    errors,
                    cancellationToken);
            }
        }

        var integrity = checksumsEntry is null
            ? new ImportValidationIntegritySummary(0, 0, 0, 0, 0)
            : await ValidateChecksumsAsync(
                checksumsEntry,
                entriesByName,
                checks,
                errors,
                cancellationToken);

        if (manifestSummary?.Matrix.SigningKey is not null)
        {
            warnings.Add("This export references Matrix signing identity material. Store and handle it securely.");
        }

        if (manifestSummary?.RestorePolicy.RequiresOldServerStoppedForSameServerName == true)
        {
            warnings.Add("Restore policy says the old public homeserver must be stopped before restoring the same Matrix server name.");
        }

        if (manifestSummary?.Warnings.Count > 0)
        {
            foreach (var warning in manifestSummary.Warnings)
            {
                warnings.Add($"Manifest warning: {warning}");
            }
        }

        var status = errors.Count == 0
            ? "valid"
            : "invalid";

        return BuildResponse(
            status: status,
            validationId: validationId,
            uploadedFileName: uploadedFileName,
            storedZipPath: storedZipPath,
            zipBytes: zipBytes,
            zipEntryCount: fileEntries.Count,
            totalUncompressedBytes: totalUncompressedBytes,
            manifestPresent: manifestPresent,
            checksumsPresent: checksumsPresent,
            manifest: manifestSummary,
            integrity: integrity,
            checks: checks,
            warnings: warnings.Distinct(StringComparer.Ordinal).ToList(),
            errors: errors,
            detail: status == "valid"
                ? "MEM stack export ZIP validation passed."
                : "MEM stack export ZIP validation failed.");
    }

    private static void ValidateManifest(
        MemStackExportManifestFile? manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
        List<ImportValidationCheck> checks,
        List<string> warnings,
        List<string> errors)
    {
        if (manifest is null)
        {
            AddCheck(
                checks,
                errors,
                code: "manifest.parse",
                severity: "error",
                passed: false,
                message: "mem-export-manifest.json could not be parsed.",
                detail: null);

            return;
        }

        AddCheck(
            checks,
            errors,
            code: "manifest.parse",
            severity: "error",
            passed: true,
            message: "mem-export-manifest.json parsed successfully.",
            detail: null);

        AddCheck(
            checks,
            errors,
            code: "manifest.version",
            severity: "error",
            passed: manifest.ManifestVersion is 1 or 2,
            message: manifest.ManifestVersion is 1 or 2
                ? "Manifest version is supported."
                : $"Unsupported manifest version: {manifest.ManifestVersion}.",
            detail: null);

        AddCheck(
            checks,
            errors,
            code: "manifest.kind",
            severity: "error",
            passed: string.Equals(
                manifest.ExportKind,
                "mem-stack-export",
                StringComparison.Ordinal),
            message: string.Equals(
                manifest.ExportKind,
                "mem-stack-export",
                StringComparison.Ordinal)
                ? "Manifest export kind is mem-stack-export."
                : $"Unexpected export kind: {manifest.ExportKind ?? "null"}.",
            detail: null);

        if (manifest.Database?.Present == true)
        {
            RequireManifestFile(
                entriesByName,
                checks,
                errors,
                code: "database.dump.present",
                manifestPath: manifest.Database.DumpFile,
                fallbackPath: "database/synapse.sql",
                description: "database dump");
        }

        if (manifest.Matrix?.Present == true)
        {
            RequireManifestFile(
                entriesByName,
                checks,
                errors,
                code: "matrix.homeserver.present",
                manifestPath: manifest.Matrix.HomeserverConfig,
                fallbackPath: "matrix/homeserver.yaml",
                description: "homeserver config");

            if (manifest.RestorePolicy?.RequiresSigningKey == true)
            {
                RequireManifestFile(
                    entriesByName,
                    checks,
                    errors,
                    code: "matrix.signing-key.present",
                    manifestPath: manifest.Matrix.SigningKey,
                    fallbackPath: "matrix/signing.key",
                    description: "Matrix signing key");
            }

            ValidateMediaStore(
                manifest,
                entriesByName,
                checks,
                errors);
        }

        if (manifest.Element?.Present == true)
        {
            RequireManifestFile(
                entriesByName,
                checks,
                errors,
                code: "element.config.present",
                manifestPath: manifest.Element.Config,
                fallbackPath: "element/config.json",
                description: "Element config");
        }

        if (manifest.Logo?.Present == true)
        {
            var logoPath = NormalizeManifestRelativePath(
                manifest.Logo.File,
                "identity/logo.png");
            var logoPathValid = string.Equals(
                logoPath,
                "identity/logo.png",
                StringComparison.Ordinal);

            AddCheck(
                checks,
                errors,
                code: "identity.logo.path",
                severity: "error",
                passed: logoPathValid,
                message: logoPathValid
                    ? "Custom stack logo uses the canonical identity/logo.png path."
                    : "Custom stack logo must use the canonical identity/logo.png path.",
                detail: logoPath);

            RequireManifestFile(
                entriesByName,
                checks,
                errors,
                code: "identity.logo.present",
                manifestPath: manifest.Logo.File,
                fallbackPath: "identity/logo.png",
                description: "custom stack logo");

            var metadataValid =
                !string.IsNullOrWhiteSpace(manifest.Logo.Sha256) &&
                manifest.Logo.Sha256.Length == 64 &&
                manifest.Logo.Sha256.All(Uri.IsHexDigit) &&
                manifest.Logo.Bytes is > 0 and <= RuntimeStackLogoService.MaximumBytes &&
                manifest.Logo.Width is >= RuntimeStackLogoService.MinimumDimension and <= RuntimeStackLogoService.MaximumDimension &&
                manifest.Logo.Height is >= RuntimeStackLogoService.MinimumDimension and <= RuntimeStackLogoService.MaximumDimension;

            AddCheck(
                checks,
                errors,
                code: "identity.logo.metadata",
                severity: "error",
                passed: metadataValid,
                message: metadataValid
                    ? "Custom stack logo metadata is valid and bounded."
                    : "Custom stack logo metadata is incomplete, invalid, or outside supported bounds.",
                detail: null);
        }

        ValidateIncludedFiles(
            manifest,
            entriesByName,
            checks,
            errors);

        if (string.IsNullOrWhiteSpace(manifest.Stack?.MatrixServerName))
        {
            warnings.Add("Manifest does not include stack.matrixServerName.");
        }
    }

    private static async Task ValidateTopologyMetadataAsync(
        MemStackExportManifestFile manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
        List<ImportValidationCheck> checks,
        List<string> warnings,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var elementHost = FirstNonEmpty(
            manifest.Routes?.ElementHost,
            TryGetHostFromUrl(manifest.Stack?.ElementPublicUrl));

        if (manifest.Element?.Present == true)
        {
            AddCheck(
                checks,
                errors,
                code: "routes.element-host.present",
                severity: "warning",
                passed: !string.IsNullOrWhiteSpace(elementHost),
                message: !string.IsNullOrWhiteSpace(elementHost)
                    ? "Element public host is present in backup route metadata."
                    : "Element public host is absent from backup metadata. Choose it during production recreate.",
                detail: elementHost);
        }

        var homeserverPath = ExportRoot + NormalizeManifestRelativePath(
            manifest.Matrix?.HomeserverConfig,
            "matrix/homeserver.yaml");

        var homeserverHasTurnUris = false;

        if (entriesByName.TryGetValue(homeserverPath, out var homeserverEntry))
        {
            await using var stream = homeserverEntry.Open();

            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 1024,
                leaveOpen: false);

            var yaml = await reader.ReadToEndAsync(cancellationToken);

            homeserverHasTurnUris = yaml
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Any(line => line.StartsWith(
                    "turn_uris:",
                    StringComparison.Ordinal));
        }

        var coturnConfigured = manifest.Coturn?.Configured == true;
        var coturnHasUris = manifest.Coturn?.TurnUris?.Count > 0;

        if (homeserverHasTurnUris)
        {
            AddCheck(
                checks,
                errors,
                code: "coturn.metadata.matches-homeserver",
                severity: "warning",
                passed: coturnConfigured && coturnHasUris,
                message: coturnConfigured && coturnHasUris
                    ? "TURN metadata matches TURN settings present in homeserver.yaml."
                    : "homeserver.yaml contains TURN settings, but backup TURN metadata is missing or incomplete.",
                detail: coturnHasUris
                    ? string.Join(", ", manifest.Coturn!.TurnUris!)
                    : null);

            if (!coturnConfigured || !coturnHasUris)
            {
                warnings.Add("The backup contains TURN settings in homeserver.yaml but not complete TURN metadata. Production recreate can still preserve the homeserver TURN block.");
            }
        }
        else if (coturnConfigured)
        {
            AddCheck(
                checks,
                errors,
                code: "coturn.metadata.matches-homeserver",
                severity: "warning",
                passed: false,
                message: "Backup TURN metadata says configured, but homeserver.yaml does not contain turn_uris.",
                detail: null);
        }
    }

    private static string? TryGetHostFromUrl(
        string? value) =>
        Uri.TryCreate(
            value,
            UriKind.Absolute,
            out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : null;

    private static string? FirstNonEmpty(
        params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void ValidateMediaStore(
        MemStackExportManifestFile manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
        List<ImportValidationCheck> checks,
        List<string> errors)
    {
        var mediaStorePath = NormalizeManifestRelativePath(
            manifest.Matrix?.MediaStore,
            "matrix/media_store");

        var zipMediaPrefix = ExportRoot +
                             mediaStorePath.TrimEnd('/') +
                             "/";

        var mediaEntries = entriesByName
            .Where(pair => pair.Key.StartsWith(
                zipMediaPrefix,
                StringComparison.Ordinal))
            .Select(pair => pair.Value)
            .ToList();

        var mediaFiles = mediaEntries.Count;
        var mediaBytes = mediaEntries.Sum(entry => entry.Length);

        AddCheck(
            checks,
            errors,
            code: "matrix.media.count",
            severity: "error",
            passed: mediaFiles == manifest.Matrix?.MediaFiles,
            message: mediaFiles == manifest.Matrix?.MediaFiles
                ? "Media file count matches manifest."
                : $"Media file count does not match manifest. Expected {manifest.Matrix?.MediaFiles}, found {mediaFiles}.",
            detail: zipMediaPrefix);

        AddCheck(
            checks,
            errors,
            code: "matrix.media.bytes",
            severity: "error",
            passed: mediaBytes == manifest.Matrix?.MediaBytes,
            message: mediaBytes == manifest.Matrix?.MediaBytes
                ? "Media byte count matches manifest."
                : $"Media byte count does not match manifest. Expected {manifest.Matrix?.MediaBytes}, found {mediaBytes}.",
            detail: zipMediaPrefix);
    }

    private static void ValidateIncludedFiles(
        MemStackExportManifestFile manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
        List<ImportValidationCheck> checks,
        List<string> errors)
    {
        var includedFiles = manifest.IncludedFiles ?? [];

        var missing = includedFiles
            .Select(path => ExportRoot + NormalizeManifestRelativePath(path, path))
            .Where(path => !entriesByName.ContainsKey(path))
            .ToList();

        AddCheck(
            checks,
            errors,
            code: "manifest.included-files.present",
            severity: "error",
            passed: missing.Count == 0,
            message: missing.Count == 0
                ? "All manifest includedFiles entries exist in the ZIP."
                : "Some manifest includedFiles entries are missing from the ZIP.",
            detail: missing.Count == 0
                ? null
                : string.Join(Environment.NewLine, missing));
    }

    private static void RequireManifestFile(
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
        List<ImportValidationCheck> checks,
        List<string> errors,
        string code,
        string? manifestPath,
        string fallbackPath,
        string description)
    {
        var relativePath = NormalizeManifestRelativePath(
            manifestPath,
            fallbackPath);

        var zipPath = ExportRoot + relativePath;

        AddCheck(
            checks,
            errors,
            code: code,
            severity: "error",
            passed: entriesByName.ContainsKey(zipPath),
            message: entriesByName.ContainsKey(zipPath)
                ? $"{description} exists."
                : $"{description} is missing.",
            detail: zipPath);
    }

    private static async Task<ImportValidationIntegritySummary> ValidateChecksumsAsync(
        ZipArchiveEntry checksumsEntry,
        IReadOnlyDictionary<string, ZipArchiveEntry> entriesByName,
        List<ImportValidationCheck> checks,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        string checksumsText;

        await using (var stream = checksumsEntry.Open())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            checksumsText = await reader.ReadToEndAsync(cancellationToken);
        }

        var lines = checksumsText
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("#", StringComparison.Ordinal))
            .ToList();

        var checkedFiles = 0;
        var missingFiles = 0;
        var failedFiles = 0;
        var passedFiles = 0;

        foreach (var line in lines)
        {
            var parsed = ParseChecksumLine(line);

            if (parsed is null)
            {
                failedFiles++;
                errors.Add($"Invalid checksum line: {line}");
                continue;
            }

            var expectedHash = parsed.Value.Hash;
            var path = NormalizeZipPath(parsed.Value.Path);

            if (!entriesByName.TryGetValue(path, out var entry))
            {
                missingFiles++;
                errors.Add($"Checksum references a missing file: {path}");
                continue;
            }

            checkedFiles++;

            await using var entryStream = entry.Open();

            var actualHash = await ComputeSha256HexAsync(
                entryStream,
                cancellationToken);

            if (string.Equals(
                    expectedHash,
                    actualHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                passedFiles++;
            }
            else
            {
                failedFiles++;
                errors.Add($"Checksum mismatch for {path}");
            }
        }

        AddCheck(
            checks,
            errors,
            code: "checksums.verify",
            severity: "error",
            passed: missingFiles == 0 && failedFiles == 0,
            message: missingFiles == 0 && failedFiles == 0
                ? "All checksum entries verified successfully."
                : "One or more checksum entries failed verification.",
            detail: $"checked={checkedFiles}; passed={passedFiles}; missing={missingFiles}; failed={failedFiles}");

        return new ImportValidationIntegritySummary(
            ChecksumLines: lines.Count,
            CheckedFiles: checkedFiles,
            MissingFiles: missingFiles,
            FailedFiles: failedFiles,
            PassedFiles: passedFiles);
    }

    private static async Task<string> ComputeSha256HexAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();

        var hash = await sha256.ComputeHashAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static (string Hash, string Path)? ParseChecksumLine(
        string line)
    {
        var separatorIndex = line.IndexOf(
            "  ",
            StringComparison.Ordinal);

        if (separatorIndex < 0)
        {
            separatorIndex = line.IndexOf(' ');
        }

        if (separatorIndex <= 0 ||
            separatorIndex >= line.Length - 1)
        {
            return null;
        }

        var hash = line[..separatorIndex].Trim();
        var path = line[(separatorIndex + 1)..].Trim();

        if (hash.Length != 64)
        {
            return null;
        }

        return (hash, path);
    }

    private static void AddCheck(
        List<ImportValidationCheck> checks,
        List<string> errors,
        string code,
        string severity,
        bool passed,
        string message,
        string? detail)
    {
        checks.Add(new ImportValidationCheck(
            Code: code,
            Severity: severity,
            Passed: passed,
            Message: message,
            Detail: detail));

        if (!passed &&
            severity == "error")
        {
            errors.Add(message);
        }
    }

    private static async Task SaveValidationSnapshotAsync(
        string validationDirectory,
        ImportValidationResponse response,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(validationDirectory);

        var finalPath = Path.Combine(
            validationDirectory,
            ValidationSnapshotFileName);

        var temporaryPath = finalPath +
                            "." +
                            Guid.NewGuid().ToString("N") +
                            ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(
                response,
                JsonOptions());

            await File.WriteAllTextAsync(
                temporaryPath,
                json,
                cancellationToken);

            File.Move(
                temporaryPath,
                finalPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static ImportValidationResponse BuildResponse(
        string status,
        string validationId,
        string uploadedFileName,
        string storedZipPath,
        long zipBytes,
        int zipEntryCount,
        long totalUncompressedBytes,
        bool manifestPresent,
        bool checksumsPresent,
        MemStackExportManifestSummary? manifest,
        ImportValidationIntegritySummary integrity,
        IReadOnlyList<ImportValidationCheck> checks,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        string? detail)
    {
        return new ImportValidationResponse(
            Source: "control-plane",
            Status: status,
            ValidationId: validationId,
            UploadedFileName: uploadedFileName,
            StoredZipPath: storedZipPath,
            ZipBytes: zipBytes,
            ZipEntryCount: zipEntryCount,
            TotalUncompressedBytes: totalUncompressedBytes,
            ManifestPresent: manifestPresent,
            ChecksumsPresent: checksumsPresent,
            Manifest: manifest,
            Integrity: integrity,
            Checks: checks,
            Warnings: warnings,
            Errors: errors,
            Detail: detail);
    }

    private static bool IsDirectoryEntry(
        ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith(
            "/",
            StringComparison.Ordinal);
    }

    private static bool IsSafeZipPath(
        string value)
    {
        var normalized = NormalizeZipPath(value);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        var segments = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        return segments.All(segment =>
            segment != "." &&
            segment != "..");
    }

    private static string NormalizeZipPath(
        string value)
    {
        return value.Replace('\\', '/').Trim();
    }

    private static string NormalizeManifestRelativePath(
        string? value,
        string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value)
            ? fallback
            : value;

        result = NormalizeZipPath(result);

        while (result.StartsWith(
                   ExportRoot,
                   StringComparison.Ordinal))
        {
            result = result[ExportRoot.Length..];
        }

        return result.TrimStart('/');
    }

    private static bool IsSafePathSegment(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return value
            .Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries)
            .All(segment =>
                segment != "." &&
                segment != "..");
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static MemStackExportManifestSummary? ToSummary(
        MemStackExportManifestFile? manifest)
    {
        if (manifest is null)
        {
            return null;
        }

        return new MemStackExportManifestSummary(
            ManifestVersion: manifest.ManifestVersion,
            ExportKind: manifest.ExportKind,
            CreatedAtUtc: manifest.CreatedAtUtc,
            CreatedBy: manifest.CreatedBy,
            MemVersion: manifest.MemVersion,
            Stack: new MemStackExportStackSummary(
                StackId: manifest.Stack?.StackId,
                Slug: manifest.Stack?.Slug,
                DisplayName: manifest.Stack?.DisplayName,
                MatrixServerName: manifest.Stack?.MatrixServerName,
                MatrixPublicUrl: manifest.Stack?.MatrixPublicUrl,
                ElementPublicUrl: manifest.Stack?.ElementPublicUrl),
            Database: new MemStackExportDatabaseSummary(
                Engine: manifest.Database?.Engine,
                DumpFile: manifest.Database?.DumpFile,
                DatabaseName: manifest.Database?.DatabaseName,
                Username: manifest.Database?.Username,
                Present: manifest.Database?.Present ?? false),
            Matrix: new MemStackExportMatrixSummary(
                HomeserverConfig: manifest.Matrix?.HomeserverConfig,
                SigningKey: manifest.Matrix?.SigningKey,
                MediaStore: manifest.Matrix?.MediaStore,
                MediaBytes: manifest.Matrix?.MediaBytes ?? 0,
                MediaFiles: manifest.Matrix?.MediaFiles ?? 0,
                Present: manifest.Matrix?.Present ?? false),
            Element: new MemStackExportElementSummary(
                Config: manifest.Element?.Config,
                Present: manifest.Element?.Present ?? false),
            Routes: new MemStackExportRoutesSummary(
                MatrixHost: manifest.Routes?.MatrixHost,
                ElementHost: manifest.Routes?.ElementHost,
                RequiresDns: manifest.Routes?.RequiresDns ?? false),
            Coturn: new MemStackExportCoturnSummary(
                Configured: manifest.Coturn?.Configured ?? false,
                PublicHost: manifest.Coturn?.PublicHost,
                Realm: manifest.Coturn?.Realm,
                TurnUris: manifest.Coturn?.TurnUris ?? [],
                SharedSecretPresent: manifest.Coturn?.SharedSecretPresent ?? false,
                UserLifetime: manifest.Coturn?.UserLifetime,
                AllowGuests: manifest.Coturn?.AllowGuests),
            RestorePolicy: new MemStackExportRestorePolicySummary(
                CanRestoreToFreshMemServer: manifest.RestorePolicy?.CanRestoreToFreshMemServer ?? false,
                RequiresPostgres: manifest.RestorePolicy?.RequiresPostgres ?? false,
                RequiresDomainMapping: manifest.RestorePolicy?.RequiresDomainMapping ?? false,
                RequiresSigningKey: manifest.RestorePolicy?.RequiresSigningKey ?? false,
                RequiresOldServerStoppedForSameServerName:
                    manifest.RestorePolicy?.RequiresOldServerStoppedForSameServerName ?? false),
            IncludedFiles: manifest.IncludedFiles ?? [],
            Warnings: manifest.Warnings ?? []);
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    private sealed class MemStackExportManifestFile
    {
        public int ManifestVersion { get; set; }
        public string? ExportKind { get; set; }
        public DateTimeOffset? CreatedAtUtc { get; set; }
        public string? CreatedBy { get; set; }
        public string? MemVersion { get; set; }
        public MemStackExportStackFile? Stack { get; set; }
        public MemStackExportDatabaseFile? Database { get; set; }
        public MemStackExportMatrixFile? Matrix { get; set; }
        public MemStackExportElementFile? Element { get; set; }
        public MemStackExportLogoFile? Logo { get; set; }
        public MemStackExportRoutesFile? Routes { get; set; }
        public MemStackExportCoturnFile? Coturn { get; set; }
        public MemStackExportRestorePolicyFile? RestorePolicy { get; set; }
        public List<string>? IncludedFiles { get; set; }
        public List<string>? Warnings { get; set; }
    }

    private sealed class MemStackExportStackFile
    {
        public string? StackId { get; set; }
        public string? Slug { get; set; }
        public string? DisplayName { get; set; }
        public string? MatrixServerName { get; set; }
        public string? MatrixPublicUrl { get; set; }
        public string? ElementPublicUrl { get; set; }
    }

    private sealed class MemStackExportDatabaseFile
    {
        public string? Engine { get; set; }
        public string? DumpFile { get; set; }
        public string? DatabaseName { get; set; }
        public string? Username { get; set; }
        public bool Present { get; set; }
    }

    private sealed class MemStackExportMatrixFile
    {
        public string? HomeserverConfig { get; set; }
        public string? SigningKey { get; set; }
        public string? MediaStore { get; set; }
        public long MediaBytes { get; set; }
        public long MediaFiles { get; set; }
        public bool Present { get; set; }
    }

    private sealed class MemStackExportElementFile
    {
        public string? Config { get; set; }
        public bool Present { get; set; }
    }

    private sealed class MemStackExportLogoFile
    {
        public string? File { get; set; }
        public string? Sha256 { get; set; }
        public long Bytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Present { get; set; }
    }

    private sealed class MemStackExportRoutesFile
    {
        public string? MatrixHost { get; set; }
        public string? ElementHost { get; set; }
        public bool RequiresDns { get; set; }
    }

    private sealed class MemStackExportCoturnFile
    {
        public bool Configured { get; set; }
        public string? PublicHost { get; set; }
        public string? Realm { get; set; }
        public List<string>? TurnUris { get; set; }
        public bool SharedSecretPresent { get; set; }
        public string? UserLifetime { get; set; }
        public bool? AllowGuests { get; set; }
    }

    private sealed class MemStackExportRestorePolicyFile
    {
        public bool CanRestoreToFreshMemServer { get; set; }
        public bool RequiresPostgres { get; set; }
        public bool RequiresDomainMapping { get; set; }
        public bool RequiresSigningKey { get; set; }
        public bool RequiresOldServerStoppedForSameServerName { get; set; }
    }
}