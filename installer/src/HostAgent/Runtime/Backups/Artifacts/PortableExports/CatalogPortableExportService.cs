using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Backups.Catalog;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Backups.Artifacts.PortableExports;

/// <summary>
/// Generates a fresh portable MEM export directly from a managed Backup Catalog
/// payload. This intentionally never reads a legacy uploaded ZIP by validation
/// id; the catalog payload is the recovery source of record.
/// </summary>
public sealed class CatalogPortableExportService
{
    private const string ExportRootEntry = "mem-stack-export";
    private const string ExportKind = "mem-stack-export";
    private const int ManifestVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConfiguration _configuration;
    private readonly BackupCatalogPayloadResolver _payloadResolver;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;

    public CatalogPortableExportService(
        IConfiguration configuration,
        BackupCatalogPayloadResolver payloadResolver,
        MemControlPlaneRuntimeContext runtimeContext)
    {
        _configuration = configuration;
        _payloadResolver = payloadResolver;
        _runtimeContext = runtimeContext;
    }

    public async Task<CatalogPortableExportResult> ExportAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        var material = await _payloadResolver
            .ResolveStandardRecreateExecutionMaterialAsync(
                catalogEntryId,
                ct);

        var sourceStackSlug = NormalizeFileName(
            material.SourceStackSlug,
            fallback: "catalog");
        var exportId = $"catalog-{NormalizeFileName(material.CatalogEntryId, "backup")}";
        var downloadName = $"mem-stack-{exportId}.zip";
        var exportDirectory = Path.Combine(
            GetStackExportsRoot(),
            sourceStackSlug);
        var exportPath = Path.Combine(exportDirectory, downloadName);
        var temporaryExportPath = $"{exportPath}.{Guid.NewGuid():N}.tmp";
        var payloadRootPath = ResolvePayloadRoot(material.DatabaseDumpPath);
        var warnings = BuildWarnings(material);

        Directory.CreateDirectory(exportDirectory);

        try
        {
            var checksums = new List<(string EntryPath, string Sha256)>();
            var includedFiles = new List<string>();

            await using (var fileStream = new FileStream(
                             temporaryExportPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None))
            using (var archive = new ZipArchive(
                       fileStream,
                       ZipArchiveMode.Create,
                       leaveOpen: false))
            {
                await AddCatalogReportAsync(
                    archive,
                    material,
                    sourceStackSlug,
                    checksums,
                    includedFiles,
                    ct);

                await AddReadmeAsync(
                    archive,
                    sourceStackSlug,
                    material.CatalogEntryId,
                    checksums,
                    includedFiles,
                    ct);

                var originalBackupManifest = FirstExistingFile(
                    Path.Combine(payloadRootPath, "backup-manifest.json"),
                    Path.Combine(payloadRootPath, "backup", "backup-manifest.json"));

                await AddFileIfPresentAsync(
                    archive,
                    originalBackupManifest,
                    $"{ExportRootEntry}/backup/backup-manifest.json",
                    checksums,
                    includedFiles,
                    ct);

                await AddFileIfPresentAsync(
                    archive,
                    material.DatabaseDumpPath,
                    $"{ExportRootEntry}/database/synapse.sql",
                    checksums,
                    includedFiles,
                    ct);

                await AddDatabaseMetadataAsync(
                    archive,
                    material,
                    checksums,
                    includedFiles,
                    ct);

                await AddFileIfPresentAsync(
                    archive,
                    material.HomeserverPath,
                    $"{ExportRootEntry}/matrix/homeserver.yaml",
                    checksums,
                    includedFiles,
                    ct);

                await AddFileIfPresentAsync(
                    archive,
                    material.SigningKeyPath,
                    $"{ExportRootEntry}/matrix/signing.key",
                    checksums,
                    includedFiles,
                    ct);

                await AddDirectoryIfPresentAsync(
                    archive,
                    material.MediaStorePath,
                    $"{ExportRootEntry}/matrix/media_store",
                    checksums,
                    includedFiles,
                    ct);

                await AddFileIfPresentAsync(
                    archive,
                    material.ElementConfigPath,
                    $"{ExportRootEntry}/element/config.json",
                    checksums,
                    includedFiles,
                    ct);

                await AddFileIfPresentAsync(
                    archive,
                    material.StackLogo?.Path,
                    $"{ExportRootEntry}/identity/logo.png",
                    checksums,
                    includedFiles,
                    ct);

                await AddRoutesSummaryAsync(
                    archive,
                    material,
                    checksums,
                    includedFiles,
                    ct);

                await AddCoturnSummaryAsync(
                    archive,
                    material,
                    checksums,
                    includedFiles,
                    ct);

                var manifestIncludedFiles = includedFiles
                    .Concat([
                        "mem-export-manifest.json",
                        "backup/checksums.sha256"
                    ])
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();

                var exportManifest = BuildExportManifest(
                    material,
                    sourceStackSlug,
                    warnings,
                    manifestIncludedFiles);

                await AddJsonEntryAsync(
                    archive,
                    $"{ExportRootEntry}/mem-export-manifest.json",
                    exportManifest,
                    checksums,
                    includedFiles,
                    ct);

                await AddChecksumsAsync(
                    archive,
                    checksums,
                    includedFiles,
                    ct);
            }

            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            File.Move(temporaryExportPath, exportPath);

            var sizeBytes = new FileInfo(exportPath).Length;

            return new CatalogPortableExportResult(
                Source: "control-plane",
                Status: "created",
                CatalogEntryId: material.CatalogEntryId,
                OriginKind: material.OriginKind,
                SourceStackSlug: sourceStackSlug,
                ExportId: exportId,
                DownloadName: downloadName,
                DownloadPath: $"/internal/host-agent/backups/artifacts/portable-exports/{Uri.EscapeDataString(exportId)}/download",
                SizeBytes: sizeBytes,
                Warnings: warnings,
                Detail: "A fresh portable MEM export was generated from the managed Backup Catalog payload.");
        }
        finally
        {
            if (File.Exists(temporaryExportPath))
            {
                File.Delete(temporaryExportPath);
            }
        }
    }

    private static IReadOnlyList<string> BuildWarnings(
        BackupCatalogStandardRecreateExecutionMaterial material)
    {
        var warnings = new List<string>();

        if (material.WarningCount > 0)
        {
            warnings.Add(
                $"The source Backup Catalog entry carries {material.WarningCount} recorded integrity warning(s). Review the catalog entry before relying on this export.");
        }

        warnings.Add("This export contains Matrix signing identity material. Store it securely.");
        warnings.Add("Do not run two public homeservers with the same Matrix server name at the same time.");
        warnings.Add("This ZIP was regenerated from the managed Backup Catalog payload. It does not require the original uploaded ZIP to remain retained.");

        return warnings;
    }

    private MemStackExportManifest BuildExportManifest(
        BackupCatalogStandardRecreateExecutionMaterial material,
        string sourceStackSlug,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> includedFiles)
    {
        var elementHost = NormalizeHost(material.SourceElementHost);
        var coturn = material.BackupCoturn;

        return new MemStackExportManifest(
            ManifestVersion: ManifestVersion,
            ExportKind: ExportKind,
            CreatedAtUtc: DateTime.UtcNow,
            CreatedBy: "Matrix Easy Mode Backup Catalog",
            MemVersion: _runtimeContext.Version,
            Stack: new MemStackExportStackManifest(
                StackId: null,
                Slug: sourceStackSlug,
                DisplayName: sourceStackSlug,
                MatrixServerName: material.MatrixServerName,
                MatrixPublicUrl: ToHttpsUrl(material.MatrixServerName),
                ElementPublicUrl: ToHttpsUrl(elementHost)),
            Database: new MemStackExportDatabaseManifest(
                Engine: "postgres",
                DumpFile: "database/synapse.sql",
                DatabaseName: null,
                Username: null,
                Present: true),
            Matrix: new MemStackExportMatrixManifest(
                HomeserverConfig: "matrix/homeserver.yaml",
                SigningKey: "matrix/signing.key",
                MediaStore: "matrix/media_store",
                MediaBytes: CountDirectoryBytes(material.MediaStorePath),
                MediaFiles: CountDirectoryFiles(material.MediaStorePath),
                Present: true),
            Element: new MemStackExportElementManifest(
                Config: "element/config.json",
                Present: !string.IsNullOrWhiteSpace(material.ElementConfigPath) && File.Exists(material.ElementConfigPath)),
            Routes: new MemStackExportRoutesManifest(
                MatrixHost: material.MatrixServerName,
                ElementHost: elementHost,
                RequiresDns: true),
            Coturn: new MemStackExportCoturnManifest(
                Configured: material.DeclaresCoturnConfigured,
                PublicHost: coturn?.PublicHost,
                Realm: coturn?.Realm,
                TurnUris: coturn?.TurnUris ?? Array.Empty<string>(),
                SharedSecretPresent: coturn?.SharedSecretPresent ?? false,
                UserLifetime: coturn?.UserLifetime,
                AllowGuests: coturn?.AllowGuests,
                State: coturn?.State ?? "unknown",
                Management: coturn?.Management ?? "unknown",
                ConfigurationSource: coturn?.ConfigurationSource,
                ConfigurationSha256: coturn?.ConfigurationSha256),
            RestorePolicy: new MemStackExportRestorePolicyManifest(
                CanRestoreToFreshMemServer: true,
                RequiresPostgres: true,
                RequiresDomainMapping: true,
                RequiresSigningKey: true,
                RequiresOldServerStoppedForSameServerName: true),
            Integrity: new MemStackExportIntegrityManifest(
                ChecksumsFile: "backup/checksums.sha256"),
            IncludedFiles: includedFiles,
            Warnings: warnings)
        {
            Logo = material.StackLogo is null
                ? null
                : new MemStackExportLogoManifest(
                    File: "identity/logo.png",
                    Sha256: material.StackLogo.Sha256,
                    Bytes: material.StackLogo.Bytes,
                    Width: material.StackLogo.Width,
                    Height: material.StackLogo.Height,
                    Present: true)
        };
    }

    private static async Task AddCatalogReportAsync(
        ZipArchive archive,
        BackupCatalogStandardRecreateExecutionMaterial material,
        string sourceStackSlug,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var report = new
        {
            SourceKind = "backup-catalog",
            material.CatalogEntryId,
            material.OriginKind,
            material.IntegrityStatus,
            material.WarningCount,
            SourceStackSlug = sourceStackSlug,
            material.MatrixServerName,
            ElementHost = NormalizeHost(material.SourceElementHost),
            GeneratedAtUtc = DateTime.UtcNow,
            Notes = new[]
            {
                "This portable export was generated directly from the managed Backup Catalog payload.",
                "The original uploaded ZIP, if any, was not used as a storage dependency."
            }
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/backup/backup-report.json",
            report,
            checksums,
            includedFiles,
            ct);
    }

    private static async Task AddDatabaseMetadataAsync(
        ZipArchive archive,
        BackupCatalogStandardRecreateExecutionMaterial material,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var metadata = new
        {
            Engine = "postgres",
            DumpFile = "database/synapse.sql",
            DumpPresent = true,
            Source = "backup-catalog",
            material.CatalogEntryId
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/database/database-metadata.json",
            metadata,
            checksums,
            includedFiles,
            ct);
    }

    private static async Task AddRoutesSummaryAsync(
        ZipArchive archive,
        BackupCatalogStandardRecreateExecutionMaterial material,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var elementHost = NormalizeHost(material.SourceElementHost);
        var summary = new
        {
            MatrixHost = material.MatrixServerName,
            MatrixPublicUrl = ToHttpsUrl(material.MatrixServerName),
            ElementHost = elementHost,
            ElementPublicUrl = ToHttpsUrl(elementHost),
            RequiresDns = true,
            CapturedAtBackupCreation = true,
            Note = "Route metadata was reconstructed from the managed Backup Catalog payload. Hosts may be overridden deliberately during restore."
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/domains/routes.json",
            summary,
            checksums,
            includedFiles,
            ct);
    }

    private static async Task AddCoturnSummaryAsync(
        ZipArchive archive,
        BackupCatalogStandardRecreateExecutionMaterial material,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var coturn = material.BackupCoturn;
        var summary = new
        {
            Configured = material.DeclaresCoturnConfigured,
            PublicHost = coturn?.PublicHost,
            Realm = coturn?.Realm,
            TurnUris = coturn?.TurnUris ?? Array.Empty<string>(),
            SharedSecretPresent = coturn?.SharedSecretPresent ?? false,
            UserLifetime = coturn?.UserLifetime,
            AllowGuests = coturn?.AllowGuests,
            State = coturn?.State ?? "unknown",
            Management = coturn?.Management ?? "unknown",
            ConfigurationSource = coturn?.ConfigurationSource,
            ConfigurationSha256 = coturn?.ConfigurationSha256,
            Note = "TURN metadata was reconstructed from the managed Backup Catalog payload. The shared secret itself remains only inside homeserver.yaml."
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/coturn/turn-summary.json",
            summary,
            checksums,
            includedFiles,
            ct);
    }

    private static async Task AddReadmeAsync(
        ZipArchive archive,
        string stackSlug,
        string catalogEntryId,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var readme = $"""
        Matrix Easy Mode Backup Catalog portable export

        Source stack: {stackSlug}
        Backup Catalog entry: {catalogEntryId}

        This ZIP was generated from a managed Backup Catalog payload.
        It may contain a Synapse database dump, homeserver.yaml, Matrix signing key,
        Matrix media_store, Element config, a custom stack logo when configured,
        metadata, a portable export manifest, and checksums.

        Important safety notes:
        - The Matrix signing key is identity material. Store this archive securely.
        - Do not run two public homeservers with the same Matrix server name at the same time.
        - Stop the old homeserver before bringing up a restored server using the same Matrix identity.
        - Test restoration in an isolated environment before relying on disaster recovery.
        """;

        await AddStringEntryAsync(
            archive,
            $"{ExportRootEntry}/backup/README-RESTORE.md",
            readme,
            checksums,
            includedFiles,
            ct);
    }

    private static async Task AddChecksumsAsync(
        ZipArchive archive,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var content = new StringBuilder();

        foreach (var checksum in checksums.OrderBy(
                     x => x.EntryPath,
                     StringComparer.Ordinal))
        {
            content.Append(checksum.Sha256);
            content.Append("  ");
            content.Append(checksum.EntryPath);
            content.AppendLine();
        }

        await AddStringEntryAsync(
            archive,
            $"{ExportRootEntry}/backup/checksums.sha256",
            content.ToString(),
            checksums: null,
            includedFiles,
            ct);
    }

    private static async Task AddJsonEntryAsync(
        ZipArchive archive,
        string entryPath,
        object value,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await AddStringEntryAsync(
            archive,
            entryPath,
            json,
            checksums,
            includedFiles,
            ct);
    }

    private static async Task AddStringEntryAsync(
        ZipArchive archive,
        string entryPath,
        string content,
        List<(string EntryPath, string Sha256)>? checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);

        await using (var entryStream = entry.Open())
        {
            await entryStream.WriteAsync(bytes, ct);
        }

        includedFiles.Add(ToExportRelativePath(entryPath));

        if (checksums is not null)
        {
            checksums.Add((entryPath, Sha256Hex(bytes)));
        }
    }

    private static async Task AddFileIfPresentAsync(
        ZipArchive archive,
        string? sourcePath,
        string entryPath,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);

        await using (var sourceStream = File.OpenRead(sourcePath))
        await using (var entryStream = entry.Open())
        {
            await sourceStream.CopyToAsync(entryStream, ct);
        }

        checksums.Add((entryPath, await Sha256FileHexAsync(sourcePath, ct)));
        includedFiles.Add(ToExportRelativePath(entryPath));
    }

    private static async Task AddDirectoryIfPresentAsync(
        ZipArchive archive,
        string? sourceDirectory,
        string entryDirectory,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(
                     sourceDirectory,
                     "*",
                     SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(file);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceDirectory, file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            await AddFileIfPresentAsync(
                archive,
                file,
                $"{entryDirectory}/{relative}",
                checksums,
                includedFiles,
                ct);
        }
    }

    /// <summary>
    /// Removes every generated portable export artifact owned by one Backup
    /// Catalog entry. Exports are located by their stable catalog-derived file
    /// name rather than a source-stack directory because an imported portable
    /// manifest can legitimately use a different stack slug from the catalog
    /// provenance row.
    /// </summary>
    public Task<int> DeleteGeneratedExportsAsync(
        string catalogEntryId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedCatalogEntryId = NormalizeFileName(
            catalogEntryId,
            fallback: "backup");
        var exportFileName = $"mem-stack-catalog-{normalizedCatalogEntryId}.zip";
        var exportsRoot = GetStackExportsRoot();

        if (!Directory.Exists(exportsRoot))
        {
            return Task.FromResult(0);
        }

        var deleted = 0;
        foreach (var exportPath in Directory.EnumerateFiles(
                     exportsRoot,
                     exportFileName,
                     SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            File.Delete(exportPath);
            deleted++;
            TryRemoveEmptyDirectory(Path.GetDirectoryName(exportPath));
        }

        return Task.FromResult(deleted);
    }

    private static void TryRemoveEmptyDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            !Directory.Exists(directoryPath))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            Directory.Delete(directoryPath);
        }
    }

    private string GetStackExportsRoot() => Path.Combine(GetDataRoot(), "exports", "stacks");

    private string GetDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string ResolvePayloadRoot(string databaseDumpPath)
    {
        var databaseDirectory = Directory.GetParent(databaseDumpPath)?.FullName;
        var payloadRoot = databaseDirectory is null
            ? null
            : Directory.GetParent(databaseDirectory)?.FullName;

        if (string.IsNullOrWhiteSpace(payloadRoot))
        {
            throw new InvalidOperationException("Catalog database dump path does not have a payload root.");
        }

        return payloadRoot;
    }

    private static string? FirstExistingFile(params string[] candidates) =>
        candidates.FirstOrDefault(File.Exists);

    private static string NormalizeFileName(string? value, string fallback)
    {
        var builder = new StringBuilder();
        foreach (var character in value?.Trim() ?? string.Empty)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private static string ToExportRelativePath(string entryPath) =>
        entryPath.StartsWith($"{ExportRootEntry}/", StringComparison.Ordinal)
            ? entryPath[(ExportRootEntry.Length + 1)..]
            : entryPath;

    private static string? NormalizeHost(string? hostOrUrl)
    {
        if (string.IsNullOrWhiteSpace(hostOrUrl))
        {
            return null;
        }

        if (Uri.TryCreate(hostOrUrl, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }

        return hostOrUrl.Trim();
    }

    private static string? ToHttpsUrl(string? host)
    {
        return string.IsNullOrWhiteSpace(host)
            ? null
            : $"https://{host}";
    }

    private static long CountDirectoryFiles(string? path) =>
        string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)
            ? 0
            : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).LongCount();

    private static long CountDirectoryBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0;
        }

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file).Length)
            .Sum();
    }

    private static async Task<string> Sha256FileHexAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

/// <summary>
/// Browser-safe result for a portable ZIP freshly generated from one Backup
/// Catalog payload. It intentionally omits all host filesystem paths.
/// </summary>
public sealed record CatalogPortableExportResult(
    string Source,
    string Status,
    string CatalogEntryId,
    string OriginKind,
    string SourceStackSlug,
    string ExportId,
    string DownloadName,
    string DownloadPath,
    long SizeBytes,
    IReadOnlyList<string> Warnings,
    string? Detail);
