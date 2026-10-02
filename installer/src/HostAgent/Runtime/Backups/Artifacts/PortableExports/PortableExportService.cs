using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Backups.Artifacts.PortableExports;

public sealed class PortableExportService
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
    private readonly LocalBackupCatalogService _historyService;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;

    public PortableExportService(
        IConfiguration configuration,
        LocalBackupCatalogService historyService,
        MemControlPlaneRuntimeContext runtimeContext)
    {
        _configuration = configuration;
        _historyService = historyService;
        _runtimeContext = runtimeContext;
    }

    public async Task<PortableExportResult> ExportAsync(
        string stackSlug,
        string backupId,
        CancellationToken ct)
    {
        var backup = await _historyService.InspectAsync(
            stackSlug,
            backupId,
            ct);

        var warnings = backup.Warnings.ToList();

        warnings.Add("This export contains Matrix signing identity material. Store it securely.");
        warnings.Add("Do not run two public homeservers with the same Matrix server name at the same time.");
        warnings.Add("This export includes route and TURN snapshots captured when the local backup was created. Review or override hosts when restoring to a different environment.");

        var exportId = BuildExportId(
            backup.StackSlug,
            backup.BackupId);

        var downloadName = $"mem-stack-{exportId}.zip";

        var exportDirectory = Path.Combine(
            GetStackExportsRoot(),
            backup.StackSlug);

        Directory.CreateDirectory(exportDirectory);

        var exportPath = Path.Combine(
            exportDirectory,
            downloadName);

        var temporaryExportPath = $"{exportPath}.tmp";

        if (File.Exists(temporaryExportPath))
        {
            File.Delete(temporaryExportPath);
        }

        var checksums = new List<(string EntryPath, string Sha256)>();
        var includedFiles = new List<string>();
        var backupLogoPath = ResolveBackupLogoPath(backup);
        if (backup.Manifest?.Logo is { Present: true } && backupLogoPath is null)
        {
            warnings.Add(
                "The backup declares a custom stack logo, but the logo payload is missing and was omitted from this portable export.");
        }

        await using (var fileStream = new FileStream(
                         temporaryExportPath,
                         FileMode.CreateNew,
                         FileAccess.ReadWrite,
                         FileShare.None))
        {
            using var archive = new ZipArchive(
                fileStream,
                ZipArchiveMode.Create,
                leaveOpen: false);

            await AddBackupReportAsync(
                archive,
                backup,
                checksums,
                includedFiles,
                ct);

            await AddReadmeAsync(
                archive,
                backup.StackSlug,
                backup.BackupId,
                checksums,
                includedFiles,
                ct);

            await AddBackupManifestAsync(
                archive,
                backup,
                checksums,
                includedFiles,
                ct);

            await AddFileIfPresentAsync(
                archive,
                backup.Components.DatabaseDump.AbsolutePath,
                $"{ExportRootEntry}/database/synapse.sql",
                checksums,
                includedFiles,
                ct);

            await AddDatabaseMetadataAsync(
                archive,
                backup,
                checksums,
                includedFiles,
                ct);

            await AddFileIfPresentAsync(
                archive,
                backup.Components.MatrixConfig.AbsolutePath,
                $"{ExportRootEntry}/matrix/homeserver.yaml",
                checksums,
                includedFiles,
                ct);

            await AddFileIfPresentAsync(
                archive,
                backup.Components.MatrixSigningKey.AbsolutePath,
                $"{ExportRootEntry}/matrix/signing.key",
                checksums,
                includedFiles,
                ct);

            await AddDirectoryIfPresentAsync(
                archive,
                backup.Components.MatrixMediaStore.AbsolutePath,
                $"{ExportRootEntry}/matrix/media_store",
                checksums,
                includedFiles,
                ct);

            await AddFileIfPresentAsync(
                archive,
                backup.Components.ElementConfig.AbsolutePath,
                $"{ExportRootEntry}/element/config.json",
                checksums,
                includedFiles,
                ct);

            await AddFileIfPresentAsync(
                archive,
                backupLogoPath,
                $"{ExportRootEntry}/identity/logo.png",
                checksums,
                includedFiles,
                ct);

            await AddRoutesSummaryAsync(
                archive,
                backup,
                checksums,
                includedFiles,
                ct);

            await AddCoturnSummaryAsync(
                archive,
                backup,
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
                backup,
                warnings,
                manifestIncludedFiles,
                logoIncluded: backupLogoPath is not null);

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

        File.Move(
            temporaryExportPath,
            exportPath);

        var sizeBytes = new FileInfo(exportPath).Length;

        return new PortableExportResult(
            Source: "control-plane",
            Status: "created",
            ExportId: exportId,
            StackSlug: backup.StackSlug,
            BackupId: backup.BackupId,
            ExportPath: exportPath,
            DownloadName: downloadName,
            DownloadPath: $"/internal/host-agent/backups/exports/{Uri.EscapeDataString(exportId)}/download",
            SizeBytes: sizeBytes,
            Warnings: warnings,
            Detail: null);
    }

    public Task<PortableExportDownload> ResolveDownloadAsync(
        string exportId,
        CancellationToken ct)
    {
        var normalizedExportId = NormalizePathSegment(
            exportId,
            "Export id is required.");

        var expectedFileName = $"mem-stack-{normalizedExportId}.zip";
        var exportsRoot = GetStackExportsRoot();

        if (!Directory.Exists(exportsRoot))
        {
            throw new DirectoryNotFoundException(
                $"Export root does not exist yet: {exportsRoot}");
        }

        var matches = Directory
            .EnumerateFiles(
                exportsRoot,
                expectedFileName,
                SearchOption.AllDirectories)
            .Where(path => string.Equals(
                Path.GetFileName(path),
                expectedFileName,
                StringComparison.Ordinal))
            .ToArray();

        ct.ThrowIfCancellationRequested();

        if (matches.Length == 0)
        {
            throw new FileNotFoundException(
                $"Export '{normalizedExportId}' was not found.");
        }

        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Export id '{normalizedExportId}' matched more than one file.");
        }

        var exportPath = matches[0];
        var fileInfo = new FileInfo(exportPath);

        return Task.FromResult(new PortableExportDownload(
            ExportId: normalizedExportId,
            ExportPath: exportPath,
            DownloadName: fileInfo.Name,
            SizeBytes: fileInfo.Length));
    }

    private MemStackExportManifest BuildExportManifest(
        LocalBackupDetailResponse backup,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> includedFiles,
        bool logoIncluded)
    {
        var stackId = backup.Manifest?.RuntimeStackId;
        var routes = backup.Manifest?.Routes;
        var coturn = backup.Manifest?.Coturn;
        var matrixServerName = FirstNonEmpty(
            routes?.MatrixHost,
            TryGetMatrixServerName(backup));
        var matrixPublicUrl = FirstNonEmpty(
            routes?.MatrixPublicUrl,
            ToHttpsUrl(matrixServerName));
        var elementPublicUrl = TryGetElementPublicUrl(backup);
        var elementHost = FirstNonEmpty(
            routes?.ElementHost,
            TryGetHostFromUrl(elementPublicUrl));

        return new MemStackExportManifest(
            ManifestVersion: ManifestVersion,
            ExportKind: ExportKind,
            CreatedAtUtc: DateTime.UtcNow,
            CreatedBy: "Matrix Easy Mode",
            MemVersion: _runtimeContext.Version,
            Stack: new MemStackExportStackManifest(
                StackId: stackId,
                Slug: backup.StackSlug,
                DisplayName: backup.StackSlug,
                MatrixServerName: matrixServerName,
                MatrixPublicUrl: matrixPublicUrl,
                ElementPublicUrl: elementPublicUrl),
            Database: new MemStackExportDatabaseManifest(
                Engine: backup.Manifest?.Database.Engine ?? "postgres",
                DumpFile: "database/synapse.sql",
                DatabaseName: backup.Manifest?.Database.DatabaseName,
                Username: backup.Manifest?.Database.DatabaseUsername,
                Present: backup.Components.DatabaseDump.Present),
            Matrix: new MemStackExportMatrixManifest(
                HomeserverConfig: "matrix/homeserver.yaml",
                SigningKey: "matrix/signing.key",
                MediaStore: "matrix/media_store",
                MediaBytes: backup.Components.MatrixMediaStore.Bytes,
                MediaFiles: backup.Components.MatrixMediaStore.Files,
                Present:
                    backup.Components.MatrixConfig.Present &&
                    backup.Components.MatrixSigningKey.Present),
            Element: new MemStackExportElementManifest(
                Config: "element/config.json",
                Present: backup.Components.ElementConfig.Present),
            Routes: new MemStackExportRoutesManifest(
                MatrixHost: matrixServerName,
                ElementHost: elementHost,
                RequiresDns: routes?.RequiresDns ??
                    (!string.IsNullOrWhiteSpace(matrixServerName) ||
                     !string.IsNullOrWhiteSpace(elementHost))),
            Coturn: new MemStackExportCoturnManifest(
                Configured: coturn?.Configured ?? false,
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
            Logo = logoIncluded && backup.Manifest?.Logo is { Present: true } logo
                ? new MemStackExportLogoManifest(
                    File: "identity/logo.png",
                    Sha256: logo.Sha256,
                    Bytes: logo.Bytes,
                    Width: logo.Width,
                    Height: logo.Height,
                    Present: true)
                : null
        };
    }

    private static string? ResolveBackupLogoPath(LocalBackupDetailResponse backup)
    {
        if (backup.Manifest?.Logo is not { Present: true })
        {
            return null;
        }

        var path = Path.Combine(backup.BackupRootPath, "identity", "logo.png");
        return File.Exists(path) ? path : null;
    }

    private async Task AddBackupManifestAsync(
        ZipArchive archive,
        LocalBackupDetailResponse backup,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(backup.ManifestPath) ||
            !File.Exists(backup.ManifestPath))
        {
            return;
        }

        await AddFileIfPresentAsync(
            archive,
            backup.ManifestPath,
            $"{ExportRootEntry}/backup/backup-manifest.json",
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddBackupReportAsync(
        ZipArchive archive,
        LocalBackupDetailResponse backup,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var report = new
        {
            backup.Source,
            backup.Status,
            backup.StackSlug,
            backup.BackupId,
            backup.BackupRootPath,
            backup.ManifestPath,
            backup.ManifestPresent,
            backup.CreatedAtUtc,
            backup.Components,
            backup.TotalBytes,
            backup.TotalFiles,
            backup.Warnings
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/backup/backup-report.json",
            report,
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddDatabaseMetadataAsync(
        ZipArchive archive,
        LocalBackupDetailResponse backup,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var metadata = new
        {
            Engine = backup.Manifest?.Database.Engine ?? "postgres",
            Host = backup.Manifest?.Database.Host,
            Port = backup.Manifest?.Database.Port,
            DatabaseName = backup.Manifest?.Database.DatabaseName,
            DatabaseUsername = backup.Manifest?.Database.DatabaseUsername,
            PasswordSecretKind = backup.Manifest?.Database.PasswordSecretKind,
            DumpFile = "database/synapse.sql",
            DumpPresent = backup.Components.DatabaseDump.Present,
            DumpBytes = backup.Components.DatabaseDump.Bytes
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/database/database-metadata.json",
            metadata,
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddRoutesSummaryAsync(
        ZipArchive archive,
        LocalBackupDetailResponse backup,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var routes = backup.Manifest?.Routes;
        var matrixHost = FirstNonEmpty(
            routes?.MatrixHost,
            TryGetMatrixServerName(backup));
        var matrixPublicUrl = FirstNonEmpty(
            routes?.MatrixPublicUrl,
            ToHttpsUrl(matrixHost));
        var elementPublicUrl = TryGetElementPublicUrl(backup);
        var elementHost = FirstNonEmpty(
            routes?.ElementHost,
            TryGetHostFromUrl(elementPublicUrl));

        var summary = new
        {
            MatrixHost = matrixHost,
            MatrixPublicUrl = matrixPublicUrl,
            ElementPublicUrl = elementPublicUrl,
            ElementHost = elementHost,
            RequiresDns = routes?.RequiresDns ??
                (!string.IsNullOrWhiteSpace(matrixHost) ||
                 !string.IsNullOrWhiteSpace(elementHost)),
            CapturedAtBackupCreation = routes is not null,
            Note = routes is null
                ? "Legacy local backup: Element route metadata was not captured. Choose an Element host during recreate."
                : "Route metadata was captured when this local backup was created. Hosts may be overridden for restored-copy testing or migration."
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/domains/routes.json",
            summary,
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddCoturnSummaryAsync(
        ZipArchive archive,
        LocalBackupDetailResponse backup,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var coturn = backup.Manifest?.Coturn;

        var summary = new
        {
            Configured = coturn?.Configured ?? false,
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
            CapturedAtBackupCreation = coturn is not null,
            Note = coturn is null
                ? "Legacy local backup: TURN metadata was not captured. homeserver.yaml may still contain TURN configuration."
                : "TURN metadata was derived from the backed-up homeserver.yaml. The shared secret itself remains only inside homeserver.yaml and is never duplicated into metadata."
        };

        await AddJsonEntryAsync(
            archive,
            $"{ExportRootEntry}/coturn/turn-summary.json",
            summary,
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddReadmeAsync(
        ZipArchive archive,
        string stackSlug,
        string backupId,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var readme = $"""
        Matrix Easy Mode stack export

        Stack: {stackSlug}
        Backup: {backupId}

        This zip is a portable MEM stack export.

        It may contain:
        - Synapse Postgres database dump
        - Matrix homeserver.yaml
        - Matrix signing key
        - Matrix media_store
        - Element config.json
        - Custom stack logo (when configured)
        - Backup manifest
        - Export manifest
        - Checksums

        Important safety notes:
        - The Matrix signing key is identity material. Store this archive securely.
        - Do not run two public homeservers with the same Matrix server name at the same time.
        - Route and TURN metadata are snapshots of the local backup creation time; override hosts only when intentionally restoring to a different environment.
        - Before relying on this as disaster recovery, test restore procedures on an isolated environment.

        Suggested future restore flow:
        1. Stop the old homeserver before bringing up a restored server with the same Matrix server name.
        2. Restore the database dump.
        3. Restore homeserver.yaml and the signing key.
        4. Restore media_store.
        5. Restore Element config.
        6. Recreate the stack through MEM Production Recreate, preserving backed-up TURN settings where present.
        7. Verify routes, TURN reachability, federation, and client access before exposing production traffic.
        """;

        await AddStringEntryAsync(
            archive,
            $"{ExportRootEntry}/backup/README-RESTORE.md",
            readme,
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddChecksumsAsync(
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

    private async Task AddJsonEntryAsync(
        ZipArchive archive,
        string entryPath,
        object value,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(
            value,
            JsonOptions);

        await AddStringEntryAsync(
            archive,
            entryPath,
            json,
            checksums,
            includedFiles,
            ct);
    }

    private async Task AddStringEntryAsync(
        ZipArchive archive,
        string entryPath,
        string content,
        List<(string EntryPath, string Sha256)>? checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        var entry = archive.CreateEntry(
            entryPath,
            CompressionLevel.Optimal);

        await using (var entryStream = entry.Open())
        {
            await entryStream.WriteAsync(
                bytes,
                ct);
        }

        includedFiles.Add(ToExportRelativePath(entryPath));

        if (checksums is not null)
        {
            checksums.Add((
                EntryPath: entryPath,
                Sha256: Sha256Hex(bytes)));
        }
    }

    private async Task AddFileIfPresentAsync(
        ZipArchive archive,
        string? sourcePath,
        string entryPath,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !File.Exists(sourcePath))
        {
            return;
        }

        var entry = archive.CreateEntry(
            entryPath,
            CompressionLevel.Optimal);

        await using (var sourceStream = File.OpenRead(sourcePath))
        await using (var entryStream = entry.Open())
        {
            await sourceStream.CopyToAsync(
                entryStream,
                ct);
        }

        checksums.Add((
            EntryPath: entryPath,
            Sha256: await Sha256FileHexAsync(sourcePath, ct)));

        includedFiles.Add(ToExportRelativePath(entryPath));
    }

    private async Task AddDirectoryIfPresentAsync(
        ZipArchive archive,
        string? sourceDirectory,
        string entryDirectory,
        List<(string EntryPath, string Sha256)> checksums,
        List<string> includedFiles,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) ||
            !Directory.Exists(sourceDirectory))
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

            var relative = Path.GetRelativePath(
                    sourceDirectory,
                    file)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            var entryPath = $"{entryDirectory}/{relative}";

            await AddFileIfPresentAsync(
                archive,
                file,
                entryPath,
                checksums,
                includedFiles,
                ct);
        }
    }

    private string GetStackExportsRoot()
    {
        return Path.Combine(
            GetDataRoot(),
            "exports",
            "stacks");
    }

    private string GetDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string BuildExportId(
        string stackSlug,
        string backupId)
    {
        return $"{NormalizeFileName(stackSlug)}-{NormalizeFileName(backupId)}";
    }

    private static string NormalizeFileName(
        string value)
    {
        var builder = new StringBuilder();

        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    private static string NormalizePathSegment(
        string value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        var normalized = Uri.UnescapeDataString(value.Trim());

        if (normalized.Contains(Path.DirectorySeparatorChar) ||
            normalized.Contains(Path.AltDirectorySeparatorChar) ||
            normalized == "." ||
            normalized == "..")
        {
            throw new InvalidOperationException("Path traversal is not allowed.");
        }

        return normalized;
    }

    private static string ToExportRelativePath(
        string entryPath)
    {
        return entryPath.StartsWith(
            $"{ExportRootEntry}/",
            StringComparison.Ordinal)
            ? entryPath[(ExportRootEntry.Length + 1)..]
            : entryPath;
    }

    private static string? TryGetMatrixServerName(
        LocalBackupDetailResponse backup)
    {
        var signingKey = backup.Components.MatrixSigningKey.RelativePath;

        if (signingKey.EndsWith(
                ".signing.key",
                StringComparison.OrdinalIgnoreCase))
        {
            return Path
                .GetFileName(signingKey)
                .Replace(
                    ".signing.key",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static string? TryGetElementPublicUrl(
        LocalBackupDetailResponse backup) =>
        FirstNonEmpty(
            backup.Manifest?.Routes?.ElementPublicUrl,
            ToHttpsUrl(backup.Manifest?.Routes?.ElementHost));

    private static string? FirstNonEmpty(
        params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? ToHttpsUrl(
        string? host) =>
        string.IsNullOrWhiteSpace(host)
            ? null
            : $"https://{host}";

    private static string? TryGetHostFromUrl(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(
            value,
            UriKind.Absolute,
            out var uri)
            ? uri.Host
            : null;
    }

    private static async Task<string> Sha256FileHexAsync(
        string path,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);

        var hash = await SHA256.HashDataAsync(
            stream,
            ct);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha256Hex(
        byte[] bytes)
    {
        return Convert
            .ToHexString(SHA256.HashData(bytes))
            .ToLowerInvariant();
    }
}