using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Archive;

public sealed class MigrationArchiveReader : IMigrationArchiveReader
{
    private const long MaximumEnvelopeOverheadBytes = 1024L * 1024 * 1024;
    private const string ManifestPath =
        "mem-migration/migration-manifest.json";
    private const string ChecksumPath =
        "mem-migration/checksums/sha256.json";
    private const string EvidencePath =
        "mem-migration/evidence/capture-report.json";
    private const string CanonicalExportPath =
        "mem-migration/legacy-mem/canonical-export.json";

    public async Task<MigrationArchiveInspection> InspectAsync(
        string archivePath,
        ArchiveSafetyLimits limits,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(archivePath);
        var archiveInfo = ValidateArchiveFile(path);
        ValidateEnvelopeFileSize(archiveInfo.Length, limits);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entries = ValidateEntryEnvelope(archive, limits);
        var entriesByPath = entries.ToDictionary(
            entry => entry.FullName,
            StringComparer.Ordinal);
        var manifestEntry = entriesByPath.GetValueOrDefault(ManifestPath)
            ?? throw new InvalidDataException(
                "The migration archive manifest is missing.");

        if (!entriesByPath.ContainsKey(ChecksumPath) ||
            !entriesByPath.ContainsKey(EvidencePath) ||
            !entriesByPath.ContainsKey(CanonicalExportPath))
        {
            throw new InvalidDataException(
                "The migration archive control files are incomplete.");
        }

        var manifest = await ReadJsonAsync<MigrationArchiveManifest>(
            manifestEntry,
            cancellationToken);
        ValidateManifest(manifest, entriesByPath, limits);
        var selectedExport = await ReadJsonAsync<SelectedStackExport>(
            entriesByPath[CanonicalExportPath],
            cancellationToken);
        ValidateSelectedStackExport(selectedExport, manifest.Stacks[0]);
        var sha256 = await Sha256File.ComputeAsync(path, cancellationToken);

        return new MigrationArchiveInspection(
            InputPath: path,
            InputSha256: sha256,
            InputBytes: archiveInfo.Length,
            VerifiedZipSha256: sha256,
            VerifiedZipBytes: archiveInfo.Length,
            Manifest: manifest,
            EncryptedInput: false);
    }

    public async Task<MigrationArchiveVerificationResult> VerifyAsync(
        string archivePath,
        ArchiveSafetyLimits limits,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(archivePath);
        var findings = new List<MigrationArchiveVerificationFinding>();
        MigrationArchiveManifest? manifest = null;
        var verifiedCount = 0;
        var verifiedBytes = 0L;

        try
        {
            var archiveInfo = ValidateArchiveFile(path);
            ValidateEnvelopeFileSize(archiveInfo.Length, limits);

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                useAsync: true);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entries = ValidateEntryEnvelope(archive, limits);
            var entriesByPath = entries.ToDictionary(
                entry => entry.FullName,
                StringComparer.Ordinal);

            var manifestEntry = entriesByPath.GetValueOrDefault(ManifestPath)
                ?? throw new InvalidDataException(
                    "The migration archive manifest is missing.");
            var checksumEntry = entriesByPath.GetValueOrDefault(ChecksumPath)
                ?? throw new InvalidDataException(
                    "The migration archive checksum index is missing.");

            manifest = await ReadJsonAsync<MigrationArchiveManifest>(
                manifestEntry,
                cancellationToken);
            ValidateManifest(manifest, entriesByPath, limits);
            var selectedExport = await ReadJsonAsync<SelectedStackExport>(
                entriesByPath[CanonicalExportPath],
                cancellationToken);
            ValidateSelectedStackExport(selectedExport, manifest.Stacks[0]);

            var checksumIndex = await ReadJsonAsync<MigrationArchiveChecksumIndex>(
                checksumEntry,
                cancellationToken);
            ValidateChecksumIndex(checksumIndex, entriesByPath);

            foreach (var expected in checksumIndex.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entriesByPath[expected.Path];
                var actualHash = await ComputeEntryHashAsync(
                    entry,
                    cancellationToken);

                if (!string.Equals(
                        actualHash,
                        expected.Sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new MigrationArchiveVerificationFinding(
                        "checksum_mismatch",
                        $"Checksum mismatch for '{expected.Path}'."));
                    continue;
                }

                verifiedCount++;
                verifiedBytes = checked(verifiedBytes + entry.Length);
            }

            ValidateManifestFiles(manifest, checksumIndex, findings);
        }
        catch (Exception ex) when (
            ex is InvalidDataException or
            IOException or
            JsonException or
            UnauthorizedAccessException or
            OverflowException or
            ArgumentException or
            FormatException or
            NullReferenceException)
        {
            findings.Add(new MigrationArchiveVerificationFinding(
                "archive_invalid",
                ex.Message));
        }

        var exists = File.Exists(path);
        var archiveBytes = exists ? new FileInfo(path).Length : 0;
        var archiveSha256 = exists
            ? await Sha256File.ComputeAsync(path, cancellationToken)
            : string.Empty;

        return new MigrationArchiveVerificationResult(
            Valid: findings.Count == 0,
            InputPath: path,
            InputSha256: archiveSha256,
            InputBytes: archiveBytes,
            VerifiedZipSha256: archiveSha256,
            VerifiedZipBytes: archiveBytes,
            EncryptedInput: false,
            Manifest: manifest,
            VerifiedFileCount: verifiedCount,
            VerifiedExpandedBytes: verifiedBytes,
            Findings: findings.ToArray());
    }



    private static void ValidateEnvelopeFileSize(
        long archiveBytes,
        ArchiveSafetyLimits limits)
    {
        var maximumBytes = limits.MaximumExpandedBytes >
                long.MaxValue - MaximumEnvelopeOverheadBytes
            ? long.MaxValue
            : limits.MaximumExpandedBytes + MaximumEnvelopeOverheadBytes;

        if (archiveBytes > maximumBytes)
        {
            throw new InvalidDataException(
                "The migration archive file exceeds the supported envelope-size limit.");
        }
    }

    private static FileInfo ValidateArchiveFile(string path)
    {
        var information = new FileInfo(Path.GetFullPath(path));

        if (!information.Exists)
        {
            throw new FileNotFoundException(
                "Migration archive was not found.",
                information.FullName);
        }

        UnixFileTypeSafety.EnsureRegularFile(information.FullName);

        if ((information.Attributes & FileAttributes.ReparsePoint) != 0 ||
            information.LinkTarget is not null)
        {
            throw new InvalidDataException(
                "Migration archives may not be symbolic links.");
        }

        return information;
    }

    private static ZipArchiveEntry[] ValidateEntryEnvelope(
        ZipArchive archive,
        ArchiveSafetyLimits limits)
    {
        if (archive.Entries.Count == 0)
        {
            throw new InvalidDataException("The migration archive is empty.");
        }

        if (archive.Entries.Count > limits.MaximumEntries)
        {
            throw new InvalidDataException(
                "The migration archive exceeds the supported entry-count limit.");
        }

        var exact = new HashSet<string>(StringComparer.Ordinal);
        var caseFolded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<ZipArchiveEntry>();
        var total = 0L;

        foreach (var entry in archive.Entries)
        {
            var isDirectory = string.IsNullOrEmpty(entry.Name);
            var normalized = isDirectory
                ? ArchivePathPolicy.NormalizeDirectory(entry.FullName)
                : ArchivePathPolicy.Normalize(entry.FullName);
            ValidateTopLevelPath(normalized, isDirectory);
            EnsureUnique(normalized, exact, caseFolded);

            if (isDirectory)
            {
                ValidateDirectoryEntry(entry);
                continue;
            }

            ValidateRegularFileEntry(entry);

            if (entry.Length > limits.MaximumEntryBytes)
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' exceeds the supported per-file limit.");
            }

            total = checked(total + entry.Length);

            if (total > limits.MaximumExpandedBytes)
            {
                throw new InvalidDataException(
                    "The migration archive exceeds the supported expanded-size limit.");
            }

            if (entry.Length > 0)
            {
                if (entry.CompressedLength == 0)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' has an invalid compression ratio.");
                }

                var ratio = (double)entry.Length / entry.CompressedLength;

                if (ratio > limits.MaximumCompressionRatio)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' exceeds the supported compression ratio.");
                }
            }

            files.Add(entry);
        }

        if (files.Count == 0)
        {
            throw new InvalidDataException(
                "The migration archive contains no regular files.");
        }

        return files.ToArray();
    }

    private static void ValidateTopLevelPath(
        string path,
        bool isDirectory)
    {
        var segments = path.Split('/');

        if (isDirectory && IsSupportedDirectoryPath(segments))
        {
            return;
        }

        if (segments.Length == 2 &&
            segments[1] == "migration-manifest.json")
        {
            return;
        }

        if (path is
            CanonicalExportPath or
            "mem-migration/checksums/sha256.json" or
            "mem-migration/evidence/capture-report.json")
        {
            return;
        }

        if (segments.Length >= 4 &&
            string.Equals(segments[1], "stacks", StringComparison.Ordinal) &&
            Guid.TryParseExact(segments[2], "D", out _) &&
            IsSupportedStackFilePath(segments))
        {
            return;
        }

        throw new InvalidDataException(
            $"Archive path is outside the fixed single-stack schema-v2 layout: '{path}'.");
    }


    private static bool IsSupportedDirectoryPath(string[] segments)
    {
        if (segments.Length == 1)
        {
            return string.Equals(
                segments[0],
                ArchivePathPolicy.Root,
                StringComparison.Ordinal);
        }

        if (!string.Equals(
                segments[0],
                ArchivePathPolicy.Root,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (segments.Length == 2)
        {
            return segments[1] is
                "legacy-mem" or "stacks" or "checksums" or "evidence";
        }

        if (!string.Equals(segments[1], "stacks", StringComparison.Ordinal) ||
            !Guid.TryParseExact(segments[2], "D", out _))
        {
            return false;
        }

        if (segments.Length == 3)
        {
            return true;
        }

        if (segments[3] is "media")
        {
            return true;
        }

        if (segments.Length == 4)
        {
            return segments[3] is "synapse" or "element" or "runtime";
        }

        return string.Equals(segments[3], "synapse", StringComparison.Ordinal) &&
            string.Equals(
                segments[4],
                "additional-config",
                StringComparison.Ordinal);
    }

    private static bool IsSupportedStackFilePath(string[] segments)
    {
        if (segments.Length == 4 &&
            string.Equals(
                segments[3],
                "stack-manifest.json",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (segments.Length >= 5 &&
            string.Equals(segments[3], "media", StringComparison.Ordinal))
        {
            return true;
        }

        if (segments.Length == 5 &&
            string.Equals(segments[3], "element", StringComparison.Ordinal) &&
            string.Equals(segments[4], "config.json", StringComparison.Ordinal))
        {
            return true;
        }

        if (segments.Length == 5 &&
            string.Equals(segments[3], "runtime", StringComparison.Ordinal) &&
            segments[4] is
                "docker-inspect.json" or
                "image-identity.json" or
                "routes.json")
        {
            return true;
        }

        if (!string.Equals(segments[3], "synapse", StringComparison.Ordinal))
        {
            return false;
        }

        if (segments.Length == 5 &&
            segments[4] is
                "homeserver.db" or
                "homeserver.yaml" or
                "signing.key")
        {
            return true;
        }

        return segments.Length >= 6 &&
            string.Equals(
                segments[4],
                "additional-config",
                StringComparison.Ordinal);
    }

    private static void EnsureUnique(
        string path,
        ISet<string> exact,
        ISet<string> caseFolded)
    {
        if (!exact.Add(path))
        {
            throw new InvalidDataException(
                $"Duplicate archive path detected: '{path}'.");
        }

        if (!caseFolded.Add(path))
        {
            throw new InvalidDataException(
                $"Case-insensitive archive path collision detected: '{path}'.");
        }
    }

    private static void ValidateRegularFileEntry(ZipArchiveEntry entry)
    {
        var unixMode = (uint)entry.ExternalAttributes >> 16;
        var fileType = unixMode & 0xF000;

        if (fileType != 0 && fileType != 0x8000)
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.FullName}' is not a regular file.");
        }

        if (HasWindowsAttribute(entry, FileAttributes.ReparsePoint) ||
            HasWindowsAttribute(entry, FileAttributes.Directory))
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.FullName}' is not a regular file.");
        }
    }

    private static void ValidateDirectoryEntry(ZipArchiveEntry entry)
    {
        var unixMode = (uint)entry.ExternalAttributes >> 16;
        var fileType = unixMode & 0xF000;

        if (fileType != 0 && fileType != 0x4000)
        {
            throw new InvalidDataException(
                $"Archive directory entry '{entry.FullName}' has an unsafe entry type.");
        }

        if (HasWindowsAttribute(entry, FileAttributes.ReparsePoint))
        {
            throw new InvalidDataException(
                $"Archive directory entry '{entry.FullName}' is a reparse point.");
        }
    }

    private static bool HasWindowsAttribute(
        ZipArchiveEntry entry,
        FileAttributes attribute) =>
        ((FileAttributes)(entry.ExternalAttributes & 0xFFFF) & attribute) != 0;

    private static async Task<T> ReadJsonAsync<T>(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Length > 16 * 1024 * 1024)
        {
            throw new InvalidDataException(
                $"JSON control entry '{entry.FullName}' is too large.");
        }

        await using var stream = entry.Open();
        var value = await JsonSerializer.DeserializeAsync<T>(
            stream,
            CaptureJson.Options,
            cancellationToken);
        return value
            ?? throw new InvalidDataException(
                $"JSON control entry '{entry.FullName}' was empty.");
    }

    private static void ValidateManifest(
        MigrationArchiveManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        ArchiveSafetyLimits limits)
    {
        if (!string.Equals(
                manifest.Schema,
                "mem-v010-migration",
                StringComparison.Ordinal) ||
            manifest.SchemaVersion != 2)
        {
            throw new InvalidDataException(
                "The migration archive manifest version is unsupported.");
        }

        if (!CaptureIdentifier.IsValid(manifest.MigrationId))
        {
            throw new InvalidDataException(
                "The migration archive has an invalid migration ID.");
        }

        if (manifest.Producer is null ||
            manifest.Source is null ||
            manifest.Capture is null ||
            manifest.Stacks is null ||
            manifest.IncludedFiles is null ||
            manifest.Limits is null)
        {
            throw new InvalidDataException(
                "The migration archive manifest is incomplete.");
        }

        foreach (var requiredPath in new[]
        {
            CanonicalExportPath,
            EvidencePath,
            ChecksumPath
        })
        {
            ValidateRequiredStackPath(requiredPath, entries);
        }

        if (!string.Equals(
                manifest.Producer.Product,
                "mem-migrate",
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(manifest.Producer.Version))
        {
            throw new InvalidDataException(
                "The migration archive producer identity is invalid.");
        }

        if (!string.Equals(
                manifest.Source.Product,
                "MatrixEasyMode",
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Source.Version,
                "0.1.0",
                StringComparison.Ordinal) ||
            !string.Equals(
                manifest.Source.LegacyMigration,
                "20260507085953_InitialApplicationSchema",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The migration archive source compatibility identity is unsupported.");
        }

        if (!IsSha256(manifest.Source.StartFingerprint) ||
            !IsSha256(manifest.Source.CompletionFingerprint))
        {
            throw new InvalidDataException(
                "The migration archive source fingerprints are invalid.");
        }

        var sourceChanged = !string.Equals(
            manifest.Source.StartFingerprint,
            manifest.Source.CompletionFingerprint,
            StringComparison.Ordinal);

        if (manifest.Capture.SourceChangedDuringCapture != sourceChanged ||
            manifest.Capture.CompletedAtUtc < manifest.Capture.StartedAtUtc ||
            manifest.CreatedAtUtc < manifest.Capture.StartedAtUtc ||
            manifest.CreatedAtUtc > manifest.Capture.CompletedAtUtc ||
            string.IsNullOrWhiteSpace(manifest.Capture.ConsistencyMethod))
        {
            throw new InvalidDataException(
                "The migration archive capture consistency evidence is contradictory.");
        }

        var isPreviewCapture =
            string.Equals(
                manifest.Capture.Kind,
                "preview",
                StringComparison.Ordinal) &&
            !manifest.Capture.SourceFrozen &&
            manifest.Capture.RehearsalOnly;
        var isFinalFrozenCapture =
            string.Equals(
                manifest.Capture.Kind,
                "final",
                StringComparison.Ordinal) &&
            manifest.Capture.SourceFrozen &&
            !manifest.Capture.RehearsalOnly &&
            !manifest.Capture.SourceChangedDuringCapture;

        if (!isPreviewCapture && !isFinalFrozenCapture)
        {
            throw new InvalidDataException(
                "The migration archive capture mode is invalid. Expected either a live rehearsal-only preview or a stable final frozen-source capture.");
        }

        if (manifest.Stacks.Length != 1)
        {
            throw new InvalidDataException(
                "The migration archive must contain exactly one selected source stack.");
        }

        if (manifest.Stacks.Any(stack => stack is null) ||
            manifest.IncludedFiles.Any(file => file is null))
        {
            throw new InvalidDataException(
                "The migration archive manifest contains null inventory entries.");
        }

        ArchivePathPolicy.EnsureUnique(
            manifest.IncludedFiles.Select(file => file.Path));

        foreach (var included in manifest.IncludedFiles)
        {
            if (!entries.TryGetValue(included.Path, out var entry) ||
                included.SizeBytes < 0 ||
                entry.Length != included.SizeBytes ||
                !IsSha256(included.Sha256))
            {
                throw new InvalidDataException(
                    $"Manifest file metadata is invalid for '{included.Path}'.");
            }
        }

        if (manifest.Stacks.Select(stack => stack.SourceStackId).Distinct().Count() !=
                manifest.Stacks.Length ||
            manifest.Stacks.Select(stack => stack.Slug)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
                manifest.Stacks.Length)
        {
            throw new InvalidDataException(
                "The migration archive stack inventory contains duplicate identities.");
        }

        foreach (var stack in manifest.Stacks)
        {
            var stackRoot =
                $"{ArchivePathPolicy.Root}/stacks/{stack.SourceStackId:D}";

            ValidateRequiredStackPath(
                $"{stackRoot}/stack-manifest.json",
                entries);
            ValidateRequiredStackPath(
                $"{stackRoot}/runtime/docker-inspect.json",
                entries);
            ValidateRequiredStackPath(
                $"{stackRoot}/runtime/image-identity.json",
                entries);
            ValidateRequiredStackPath(
                $"{stackRoot}/runtime/routes.json",
                entries);

            if (stack.SourceStackId == Guid.Empty ||
                stack.MatrixServiceId == Guid.Empty ||
                stack.ElementServiceId == Guid.Empty ||
                string.IsNullOrWhiteSpace(stack.Slug) ||
                string.IsNullOrWhiteSpace(stack.DisplayName) ||
                string.IsNullOrWhiteSpace(stack.MatrixServerName) ||
                !IsCanonicalHttpsAuthority(stack.MatrixPublicUrl) ||
                !IsCanonicalHttpsAuthority(stack.ElementPublicUrl))
            {
                throw new InvalidDataException(
                    $"Stack '{stack.SourceStackId}' has invalid canonical public identity metadata.");
            }

            ValidateExpectedStackFile(
                stack.SqlitePath,
                $"{stackRoot}/synapse/homeserver.db",
                entries);
            ValidateExpectedStackFile(
                stack.HomeserverConfigurationPath,
                $"{stackRoot}/synapse/homeserver.yaml",
                entries);
            ValidateExpectedStackFile(
                stack.SigningKeyPath,
                $"{stackRoot}/synapse/signing.key",
                entries);

            if (stack.AdditionalConfigurationPaths is null)
            {
                throw new InvalidDataException(
                    $"Stack '{stack.SourceStackId}' has no additional-configuration inventory.");
            }

            ArchivePathPolicy.EnsureUnique(stack.AdditionalConfigurationPaths);
            var additionalPrefix = $"{stackRoot}/synapse/additional-config/";

            foreach (var additionalPath in stack.AdditionalConfigurationPaths)
            {
                var normalized = ArchivePathPolicy.Normalize(additionalPath);

                if (!normalized.StartsWith(
                        additionalPrefix,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Stack '{stack.SourceStackId}' has an invalid additional-configuration path.");
                }

                ValidateRequiredStackPath(normalized, entries);
            }

            if (!string.IsNullOrWhiteSpace(stack.ElementConfigurationPath))
            {
                ValidateExpectedStackFile(
                    stack.ElementConfigurationPath,
                    $"{stackRoot}/element/config.json",
                    entries);
            }

            if (!string.IsNullOrWhiteSpace(stack.MediaPath))
            {
                var normalizedMedia =
                    ArchivePathPolicy.NormalizeDirectory(stack.MediaPath);

                if (!string.Equals(
                        normalizedMedia,
                        $"{stackRoot}/media",
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"Stack '{stack.SourceStackId}' has an invalid media path.");
                }
            }
        }

        var selectedStackRoot =
            $"{ArchivePathPolicy.Root}/stacks/{manifest.Stacks[0].SourceStackId:D}/";
        var allowedPackageFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            ManifestPath,
            EvidencePath,
            ChecksumPath,
            CanonicalExportPath
        };
        if (entries.Keys.Any(path =>
                !allowedPackageFiles.Contains(path) &&
                !path.StartsWith(selectedStackRoot, StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The migration archive contains material outside the selected source stack.");
        }

        if (manifest.Limits.EntryCount != entries.Count)
        {
            throw new InvalidDataException(
                "The manifest entry count does not match the archive.");
        }

        var expandedBytes = entries.Values.Sum(entry => entry.Length);

        if (manifest.Limits.ExpandedBytes != expandedBytes)
        {
            throw new InvalidDataException(
                "The manifest expanded byte count does not match the archive.");
        }

        if (manifest.Limits.MaximumEntryBytes <= 0 ||
            manifest.Limits.MaximumExpandedBytes <= 0 ||
            manifest.Limits.MaximumEntries <= 0 ||
            manifest.Limits.MaximumEntryBytes >
                manifest.Limits.MaximumExpandedBytes ||
            manifest.Limits.MaximumEntryBytes > limits.MaximumEntryBytes ||
            manifest.Limits.MaximumExpandedBytes > limits.MaximumExpandedBytes ||
            manifest.Limits.MaximumEntries > limits.MaximumEntries)
        {
            throw new InvalidDataException(
                "The archive declares limits outside the verifier support envelope.");
        }
    }

    private static void ValidateSelectedStackExport(
        SelectedStackExport selectedExport,
        MigrationArchiveStack manifestStack)
    {
        if (!string.Equals(
                selectedExport.Schema,
                "mem-v010-selected-stack-export",
                StringComparison.Ordinal) ||
            selectedExport.SchemaVersion != 2 ||
            selectedExport.SelectedSourceStackId == Guid.Empty ||
            selectedExport.Stack is null ||
            selectedExport.Services is null)
        {
            throw new InvalidDataException(
                "The selected-stack canonical export is incomplete or unsupported.");
        }

        if (selectedExport.SelectedSourceStackId != manifestStack.SourceStackId ||
            selectedExport.Stack.Id != manifestStack.SourceStackId ||
            !string.Equals(selectedExport.Stack.Slug, manifestStack.Slug, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The selected-stack canonical export does not match the migration manifest.");
        }

        if (selectedExport.Services.Length == 0 ||
            selectedExport.Services.Any(service =>
                service is null ||
                service.Id == Guid.Empty ||
                service.StackId != manifestStack.SourceStackId ||
                string.IsNullOrWhiteSpace(service.ServiceKey)) ||
            selectedExport.Services.Select(service => service.Id).Distinct().Count() !=
                selectedExport.Services.Length)
        {
            throw new InvalidDataException(
                "The selected-stack canonical export contains invalid service provenance.");
        }

        var matrixServices = selectedExport.Services
            .Where(service =>
                string.Equals(service.ServiceKey, "matrix", StringComparison.Ordinal))
            .ToArray();
        if (matrixServices.Length != 1 ||
            !string.Equals(
                matrixServices[0].ServerName,
                manifestStack.MatrixServerName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The selected-stack canonical export does not match the Matrix server identity in the migration manifest.");
        }
    }

    private static void ValidateExpectedStackFile(
        string actualPath,
        string expectedPath,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var normalized = ArchivePathPolicy.Normalize(actualPath);

        if (!string.Equals(normalized, expectedPath, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"A stack manifest file path is not canonical: '{normalized}'.");
        }

        ValidateRequiredStackPath(normalized, entries);
    }

    private static void ValidateRequiredStackPath(
        string path,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        var normalized = ArchivePathPolicy.Normalize(path);

        if (!entries.ContainsKey(normalized))
        {
            throw new InvalidDataException(
                $"A required stack file is missing: '{normalized}'.");
        }
    }

    private static bool IsCanonicalHttpsAuthority(string? value)
    {
        if (value is null)
        {
            return true;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IsDefaultPort)
        {
            return false;
        }

        return string.Equals(
            value,
            uri.GetLeftPart(UriPartial.Authority),
            StringComparison.Ordinal);
    }

    private static void ValidateChecksumIndex(
        MigrationArchiveChecksumIndex index,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (!string.Equals(
                index.Schema,
                "mem-migration-sha256",
                StringComparison.Ordinal) ||
            index.SchemaVersion != 1 ||
            index.Files is null)
        {
            throw new InvalidDataException(
                "The migration checksum index version is unsupported.");
        }

        if (index.Files.Any(file => file is null))
        {
            throw new InvalidDataException(
                "The migration checksum index contains null entries.");
        }

        ArchivePathPolicy.EnsureUnique(index.Files.Select(file => file.Path));
        var expectedPaths = entries.Keys
            .Where(path => !string.Equals(
                path,
                ChecksumPath,
                StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var indexedPaths = index.Files
            .Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (!expectedPaths.SequenceEqual(indexedPaths, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "The checksum index does not account for every archive file exactly once.");
        }

        foreach (var expected in index.Files)
        {
            if (!entries.TryGetValue(expected.Path, out var entry))
            {
                throw new InvalidDataException(
                    $"Checksum entry '{expected.Path}' does not exist in the archive.");
            }

            if (entry.Length != expected.SizeBytes)
            {
                throw new InvalidDataException(
                    $"Checksum size does not match for '{expected.Path}'.");
            }

            if (!IsSha256(expected.Sha256))
            {
                throw new InvalidDataException(
                    $"Checksum value is invalid for '{expected.Path}'.");
            }
        }
    }

    private static void ValidateManifestFiles(
        MigrationArchiveManifest manifest,
        MigrationArchiveChecksumIndex index,
        ICollection<MigrationArchiveVerificationFinding> findings)
    {
        var checksums = index.Files.ToDictionary(
            file => file.Path,
            StringComparer.Ordinal);
        var expectedPayloadPaths = index.Files
            .Select(file => file.Path)
            .Where(path => !string.Equals(path, ManifestPath, StringComparison.Ordinal))
            .Where(path => !string.Equals(path, EvidencePath, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        var manifestPayloadPaths = manifest.IncludedFiles
            .Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (!expectedPayloadPaths.SequenceEqual(
                manifestPayloadPaths,
                StringComparer.Ordinal))
        {
            findings.Add(new MigrationArchiveVerificationFinding(
                "manifest_inventory_mismatch",
                "The manifest payload inventory does not account for every non-control archive file exactly once."));
        }

        foreach (var file in manifest.IncludedFiles)
        {
            if (!checksums.TryGetValue(file.Path, out var checksum))
            {
                findings.Add(new MigrationArchiveVerificationFinding(
                    "manifest_file_missing",
                    $"Manifest file '{file.Path}' is absent from the checksum index."));
                continue;
            }

            if (file.SizeBytes != checksum.SizeBytes ||
                !string.Equals(
                    file.Sha256,
                    checksum.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new MigrationArchiveVerificationFinding(
                    "manifest_file_mismatch",
                    $"Manifest file metadata does not match for '{file.Path}'."));
            }
        }
    }

    private static bool IsSha256(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length == 64 &&
        value.All(Uri.IsHexDigit);

    private static async Task<string> ComputeEntryHashAsync(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var algorithm = SHA256.Create();
        var hash = await algorithm.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    private sealed record SelectedStackExport(
        string Schema,
        int SchemaVersion,
        Guid SelectedSourceStackId,
        SelectedStackExportStack Stack,
        SelectedStackExportService[] Services);

    private sealed record SelectedStackExportStack(
        Guid Id,
        string Slug);

    private sealed record SelectedStackExportService(
        Guid Id,
        Guid StackId,
        string ServiceKey,
        string? ServerName);

}
