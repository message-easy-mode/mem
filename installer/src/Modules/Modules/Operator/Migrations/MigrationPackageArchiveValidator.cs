using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Modules.Operator.Migrations;

internal static class MigrationPackageArchiveValidator
{
    private const string Root = "mem-migration/";
    private const string ManifestPath = Root + "migration-manifest.json";
    private const string ChecksumPath = Root + "checksums/sha256.json";
    private const string EvidencePath = Root + "evidence/capture-report.json";
    private const string CanonicalExportPath = Root + "legacy-mem/canonical-export.json";
    private const long MaximumEntryBytes = 10L * 1024 * 1024 * 1024;
    private const long MaximumExpandedBytes = 25L * 1024 * 1024 * 1024;
    private const int MaximumEntries = 250_000;
    private const long MaximumControlJsonBytes = 16L * 1024 * 1024;
    private const long MaximumCompressionRatio = 100;

    public static async Task<IReadOnlyList<MigrationPackageArchiveStackIdentity>> InspectStackIdentitiesAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var entries = ValidateEnvelope(archive);
        var manifest = await ReadJsonAsync<MigrationManifest>(
            GetRequired(entries, ManifestPath),
            cancellationToken);
        ValidateManifest(manifest, entries);
        var selectedExport = await ReadJsonAsync<SelectedStackExport>(
            GetRequired(entries, CanonicalExportPath),
            cancellationToken);
        ValidateSelectedStackExport(selectedExport, manifest.Stacks[0]);

        return ProjectStackIdentities(manifest);
    }

    public static async Task<MigrationPackageArchiveSummary> ValidateAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

        var entries = ValidateEnvelope(archive);
        var manifestEntry = GetRequired(entries, ManifestPath);
        var checksumEntry = GetRequired(entries, ChecksumPath);
        _ = GetRequired(entries, EvidencePath);

        var manifest = await ReadJsonAsync<MigrationManifest>(
            manifestEntry,
            cancellationToken);
        ValidateManifest(manifest, entries);
        var selectedExport = await ReadJsonAsync<SelectedStackExport>(
            GetRequired(entries, CanonicalExportPath),
            cancellationToken);
        ValidateSelectedStackExport(selectedExport, manifest.Stacks[0]);

        var checksumIndex = await ReadJsonAsync<ChecksumIndex>(
            checksumEntry,
            cancellationToken);
        ValidateChecksumIndex(checksumIndex, entries);

        var verifiedFiles = 0;
        long verifiedBytes = 0;
        foreach (var expected in checksumIndex.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[expected.Path];
            var actual = await ComputeSha256Async(entry, cancellationToken);
            if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_checksum_mismatch",
                    $"The migration archive checksum did not match for '{expected.Path}'.");
            }

            verifiedFiles++;
            verifiedBytes = checked(verifiedBytes + entry.Length);
        }

        ValidateManifestInventory(manifest, checksumIndex);

        return new MigrationPackageArchiveSummary(
            manifest.MigrationId,
            manifest.Source.Product,
            manifest.Source.Version,
            manifest.Source.LegacyMigration,
            manifest.Source.StartFingerprint.ToLowerInvariant(),
            manifest.Source.CompletionFingerprint.ToLowerInvariant(),
            ProjectStackIdentities(manifest),
            manifest.Capture.Kind,
            manifest.Capture.SourceFrozen,
            manifest.Capture.RehearsalOnly,
            manifest.Capture.SourceChangedDuringCapture,
            manifest.Capture.StartedAtUtc,
            manifest.Capture.CompletedAtUtc,
            verifiedFiles,
            verifiedBytes);
    }

    private static IReadOnlyList<MigrationPackageArchiveStackIdentity> ProjectStackIdentities(
        MigrationManifest manifest) =>
        manifest.Stacks
            .Select(stack => new MigrationPackageArchiveStackIdentity(
                stack.SourceStackId,
                stack.Slug,
                stack.MatrixServerName))
            .OrderBy(stack => stack.Slug, StringComparer.OrdinalIgnoreCase)
            .ThenBy(stack => stack.SourceStackId)
            .ToArray();

    private static Dictionary<string, ZipArchiveEntry> ValidateEnvelope(ZipArchive archive)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntries)
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_entry_count_invalid",
                "The decrypted migration archive has an invalid entry count.");
        }

        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var caseFolded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;

        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName;
            if (string.IsNullOrWhiteSpace(path) ||
                path.Contains('\\') ||
                path.StartsWith('/') ||
                path.Contains('\0') ||
                path.Split('/').Any(segment => segment is "." or "..") ||
                !path.StartsWith(Root, StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_path_invalid",
                    "The decrypted migration archive contains an unsafe or unsupported path.");
            }

            if (!entries.TryAdd(path, entry) || !caseFolded.Add(path))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_path_invalid",
                    "The decrypted migration archive contains a duplicate or case-colliding path.");
            }

            if (IsLinkOrSpecial(entry))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_entry_type_invalid",
                    "The decrypted migration archive contains a link or special-file entry.");
            }

            expanded = checked(expanded + entry.Length);
            if (entry.Length > MaximumEntryBytes || expanded > MaximumExpandedBytes)
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_size_invalid",
                    "The decrypted migration archive exceeds the supported expanded-size limits.");
            }

            if (entry.CompressedLength > 0 &&
                entry.Length / entry.CompressedLength > MaximumCompressionRatio)
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_compression_ratio_invalid",
                    "The decrypted migration archive contains an entry with an unsafe compression ratio.");
            }
        }

        return entries;
    }

    private static bool IsLinkOrSpecial(ZipArchiveEntry entry)
    {
        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
        return mode is 0xA000 or 0x6000 or 0x2000 or 0x1000 or 0xC000;
    }

    private static ZipArchiveEntry GetRequired(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string path) =>
        entries.TryGetValue(path, out var entry)
            ? entry
            : throw new SecureMigrationIntakeException(
                "migration_archive_control_file_missing",
                $"The migration archive control file '{path}' is missing.");

    private static async Task<T> ReadJsonAsync<T>(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Length <= 0 || entry.Length > MaximumControlJsonBytes)
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_control_file_invalid",
                $"The migration archive control file '{entry.FullName}' has an invalid size.");
        }

        try
        {
            await using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<T>(
                       stream,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                       cancellationToken)
                   ?? throw new JsonException("The JSON document was empty.");
        }
        catch (JsonException exception)
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_control_file_invalid",
                $"The migration archive control file '{entry.FullName}' is invalid.",
                exception);
        }
    }

    private static void ValidateManifest(
        MigrationManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (manifest.Schema != "mem-v010-migration" ||
            manifest.SchemaVersion != 2 ||
            string.IsNullOrWhiteSpace(manifest.MigrationId) ||
            manifest.Source is null ||
            manifest.Capture is null ||
            manifest.Stacks is null ||
            manifest.Stacks.Length != 1 ||
            manifest.IncludedFiles is null)
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_manifest_invalid",
                "The migration archive manifest is incomplete or unsupported.");
        }

        if (manifest.Source.Product != "MatrixEasyMode" ||
            manifest.Source.Version != "0.1.0" ||
            string.IsNullOrWhiteSpace(manifest.Source.LegacyMigration) ||
            !IsSha256(manifest.Source.StartFingerprint) ||
            !IsSha256(manifest.Source.CompletionFingerprint))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_source_unsupported",
                "The migration archive source identity is incomplete or unsupported.");
        }

        var preview = manifest.Capture.Kind == "preview" &&
                      !manifest.Capture.SourceFrozen &&
                      manifest.Capture.RehearsalOnly;
        var final = manifest.Capture.Kind == "final" &&
                    manifest.Capture.SourceFrozen &&
                    !manifest.Capture.RehearsalOnly;

        if ((!preview && !final) ||
            manifest.Capture.SourceChangedDuringCapture ||
            manifest.Capture.StartedAtUtc == default ||
            manifest.Capture.CompletedAtUtc == default ||
            manifest.Capture.CompletedAtUtc < manifest.Capture.StartedAtUtc ||
            !string.Equals(
                manifest.Source.StartFingerprint,
                manifest.Source.CompletionFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_capture_invalid",
                "The migration archive capture semantics are contradictory or report source drift.");
        }

        var stackIds = new HashSet<Guid>();
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var serverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var stack in manifest.Stacks)
        {
            if (stack.SourceStackId == Guid.Empty ||
                string.IsNullOrWhiteSpace(stack.Slug) ||
                string.IsNullOrWhiteSpace(stack.MatrixServerName) ||
                !stackIds.Add(stack.SourceStackId) ||
                !slugs.Add(stack.Slug) ||
                !serverNames.Add(stack.MatrixServerName))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_stack_identity_invalid",
                    "The migration archive contains incomplete or duplicate stack identity.");
            }
        }

        foreach (var required in new[]
        {
            CanonicalExportPath,
            EvidencePath,
            ChecksumPath,
        })
        {
            _ = GetRequired(entries, required);
        }

        var selectedRoot = $"{Root}stacks/{manifest.Stacks[0].SourceStackId:D}/";
        var allowedControlFiles = new HashSet<string>(StringComparer.Ordinal)
        {
            ManifestPath,
            ChecksumPath,
            EvidencePath,
            CanonicalExportPath
        };
        if (entries.Keys.Any(path =>
                !allowedControlFiles.Contains(path) &&
                !path.StartsWith(selectedRoot, StringComparison.Ordinal)))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_scope_invalid",
                "The migration archive contains material outside the selected source stack.");
        }
    }

    private static void ValidateSelectedStackExport(
        SelectedStackExport selectedExport,
        ManifestStack manifestStack)
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
            throw new SecureMigrationIntakeException(
                "migration_archive_selected_stack_export_invalid",
                "The selected-stack provenance in the migration archive is incomplete or unsupported.");
        }

        if (selectedExport.SelectedSourceStackId != manifestStack.SourceStackId ||
            selectedExport.Stack.Id != manifestStack.SourceStackId ||
            !string.Equals(selectedExport.Stack.Slug, manifestStack.Slug, StringComparison.Ordinal))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_selected_stack_export_invalid",
                "The selected-stack provenance does not match the migration manifest.");
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
            throw new SecureMigrationIntakeException(
                "migration_archive_selected_stack_export_invalid",
                "The selected-stack provenance contains invalid service identity.");
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
            throw new SecureMigrationIntakeException(
                "migration_archive_selected_stack_export_invalid",
                "The selected-stack provenance does not match the Matrix server identity in the migration manifest.");
        }
    }

    private static void ValidateChecksumIndex(
        ChecksumIndex index,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (index.Schema != "mem-migration-sha256" ||
            index.SchemaVersion != 1 ||
            index.Files is null ||
            index.Files.Any(file => file is null))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_checksum_index_invalid",
                "The migration archive checksum index is unsupported or incomplete.");
        }

        var indexed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var expected in index.Files)
        {
            if (string.IsNullOrWhiteSpace(expected.Path) ||
                !indexed.Add(expected.Path) ||
                !entries.TryGetValue(expected.Path, out var entry) ||
                entry.Length != expected.SizeBytes ||
                !IsSha256(expected.Sha256))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_checksum_index_invalid",
                    "The migration archive checksum index does not match the archive payload.");
            }
        }

        var expectedPaths = entries.Keys
            .Where(path => path != ChecksumPath)
            .OrderBy(path => path, StringComparer.Ordinal);
        var indexedPaths = indexed.OrderBy(path => path, StringComparer.Ordinal);
        if (!expectedPaths.SequenceEqual(indexedPaths, StringComparer.Ordinal))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_checksum_index_invalid",
                "The checksum index does not account for every archive file exactly once.");
        }
    }

    private static void ValidateManifestInventory(
        MigrationManifest manifest,
        ChecksumIndex checksumIndex)
    {
        var checksums = checksumIndex.Files.ToDictionary(file => file.Path, StringComparer.Ordinal);
        var expectedPayload = checksumIndex.Files
            .Select(file => file.Path)
            .Where(path => path != ManifestPath && path != EvidencePath)
            .OrderBy(path => path, StringComparer.Ordinal);
        var manifestPayload = manifest.IncludedFiles
            .Select(file => file.Path)
            .OrderBy(path => path, StringComparer.Ordinal);

        if (!expectedPayload.SequenceEqual(manifestPayload, StringComparer.Ordinal))
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_manifest_inventory_mismatch",
                "The migration manifest does not account for every payload file exactly once.");
        }

        foreach (var file in manifest.IncludedFiles)
        {
            if (!checksums.TryGetValue(file.Path, out var checksum) ||
                checksum.SizeBytes != file.SizeBytes ||
                !string.Equals(checksum.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecureMigrationIntakeException(
                    "migration_archive_manifest_inventory_mismatch",
                    $"The migration manifest evidence does not match '{file.Path}'.");
            }
        }
    }

    private static async Task<string> ComputeSha256Async(
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private sealed record MigrationManifest(
        string Schema,
        int SchemaVersion,
        string MigrationId,
        ManifestSource Source,
        ManifestCapture Capture,
        ManifestStack[] Stacks,
        ManifestFile[] IncludedFiles);

    private sealed record ManifestSource(
        string Product,
        string Version,
        string LegacyMigration,
        string StartFingerprint,
        string CompletionFingerprint);

    private sealed record ManifestCapture(
        string Kind,
        bool SourceFrozen,
        bool RehearsalOnly,
        bool SourceChangedDuringCapture,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset CompletedAtUtc);

    private sealed record ManifestStack(
        Guid SourceStackId,
        string Slug,
        string MatrixServerName);

    private sealed record ManifestFile(string Path, long SizeBytes, string Sha256);

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

    private sealed record ChecksumIndex(
        string Schema,
        int SchemaVersion,
        ChecksumFile[] Files);
    private sealed record ChecksumFile(string Path, long SizeBytes, string Sha256);
}

internal sealed record MigrationPackageArchiveStackIdentity(
    Guid SourceStackId,
    string Slug,
    string MatrixServerName);

internal sealed record MigrationPackageArchiveSummary(
    string MigrationId,
    string SourceProduct,
    string SourceVersion,
    string LegacyMigration,
    string StartSourceFingerprint,
    string CompletionSourceFingerprint,
    IReadOnlyList<MigrationPackageArchiveStackIdentity> Stacks,
    string CaptureKind,
    bool SourceFrozen,
    bool RehearsalOnly,
    bool SourceChangedDuringCapture,
    DateTimeOffset CaptureStartedAtUtc,
    DateTimeOffset CaptureCompletedAtUtc,
    int VerifiedFileCount,
    long VerifiedExpandedBytes)
{
    public int StackCount => Stacks.Count;
}
