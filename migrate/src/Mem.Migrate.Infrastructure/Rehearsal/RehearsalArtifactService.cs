using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Rehearsal;
using Mem.Migrate.Core.Processes;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Infrastructure.Archive;

namespace Mem.Migrate.Infrastructure.Rehearsal;

public sealed class RehearsalArtifactService(
    IMigrationArchiveReader archiveReader,
    IBinaryProcessRunner binaryProcessRunner) : IRehearsalArtifactService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const string Root = "mem-stack-export";

    public async Task<RehearsalArtifactReport> ExportAsync(RehearsalArtifactOptions options, CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var verification = await archiveReader.VerifyAsync(normalized.ArchivePath, normalized.ToSafetyLimits(), cancellationToken);
        if (!verification.Valid || verification.Manifest is null)
            throw new InvalidDataException(verification.Findings.FirstOrDefault()?.Message ?? "Migration archive verification failed.");

        var conversion = await ReadJsonAsync<ConversionReport>(normalized.ConversionReportPath, cancellationToken)
            ?? throw new InvalidDataException("Conversion report could not be parsed.");
        if (conversion.Status != ConversionLifecycleStatus.Completed)
            throw new InvalidDataException("Only a completed MM-04A conversion can be exported.");
        if (!string.Equals(conversion.ArchiveSha256, verification.VerifiedZipSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Conversion report archive receipt does not match the verified MM-03 archive.");
        if (!File.Exists(conversion.PostgreSqlDumpPath)) throw new FileNotFoundException("Converted PostgreSQL dump was not found.", conversion.PostgreSqlDumpPath);
        var dumpHash = await Sha256File.ComputeAsync(conversion.PostgreSqlDumpPath, cancellationToken);
        if (!string.Equals(dumpHash, conversion.PostgreSqlDumpSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Converted PostgreSQL dump SHA-256 does not match the MM-04A report.");

        var stack = verification.Manifest.Stacks.SingleOrDefault(x => x.SourceStackId == conversion.SourceStackId)
            ?? throw new InvalidDataException("Conversion stack is not present in the verified archive.");
        if (!string.Equals(stack.MatrixServerName, conversion.MatrixServerName, StringComparison.Ordinal))
            throw new InvalidDataException("Conversion Matrix server name does not match the verified archive.");

        PrivateFilePermissions.EnsureDirectory(normalized.OutputPath);
        var outputDir = Path.Combine(normalized.OutputPath, normalized.ArtifactId!);
        if (Directory.Exists(outputDir)) throw new IOException($"Artifact output already exists: {outputDir}");
        PrivateFilePermissions.EnsureDirectory(outputDir);
        var staging = Path.Combine(outputDir, ".staging");
        PrivateFilePermissions.EnsureDirectory(staging);

        try
        {
            var exportRoot = Path.Combine(staging, Root);
            var databaseDir = Path.Combine(exportRoot, "database");
            var matrixDir = Path.Combine(exportRoot, "matrix");
            var elementDir = Path.Combine(exportRoot, "element");
            var backupDir = Path.Combine(exportRoot, "backup");
            foreach (var directory in new[] { exportRoot, databaseDir, matrixDir, elementDir, backupDir }) PrivateFilePermissions.EnsureDirectory(directory);

            var dumpDestination = Path.Combine(databaseDir, "synapse.sql");
            var plainDump = await binaryProcessRunner.RunToFileAsync(
                new BinaryProcessRequest(
                    normalized.DockerCommand,
                    [
                        "run", "--rm", "--pull", "never", "--network", "none",
                        "--read-only",
                        "--tmpfs", "/tmp:rw,nosuid,nodev,size=64m",
                        "--entrypoint", "pg_restore",
                        "-v", $"{conversion.PostgreSqlDumpPath}:/input/source.dump:ro",
                        conversion.PostgresImage,
                        "--no-owner", "--no-privileges", "--file=-",
                        "/input/source.dump"
                    ],
                    dumpDestination,
                    TimeSpan.FromSeconds(normalized.CommandTimeoutSeconds)),
                cancellationToken);

            if (!plainDump.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not convert the verified MM-04A custom-format PostgreSQL dump to MEM's canonical plain SQL artifact: {plainDump.ErrorMessage ?? plainDump.StandardError}");
            }

            PrivateFilePermissions.EnsureFile(dumpDestination);

            var selected = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [stack.HomeserverConfigurationPath] = Path.Combine(matrixDir, "homeserver.yaml"),
                [stack.SigningKeyPath] = Path.Combine(matrixDir, "signing.key")
            };
            if (!string.IsNullOrWhiteSpace(stack.ElementConfigurationPath)) selected[stack.ElementConfigurationPath] = Path.Combine(elementDir, "config.json");

            var mediaFiles = verification.Manifest.IncludedFiles
                .Where(x => !string.IsNullOrWhiteSpace(stack.MediaPath) && x.Path.StartsWith(stack.MediaPath.TrimEnd('/') + "/", StringComparison.Ordinal))
                .OrderBy(x => x.Path, StringComparer.Ordinal)
                .ToArray();
            foreach (var media in mediaFiles)
            {
                var relative = media.Path[(stack.MediaPath!.TrimEnd('/').Length + 1)..];
                selected[media.Path] = Path.Combine(matrixDir, "media_store", relative.Replace('/', Path.DirectorySeparatorChar));
            }

            var extractor = new VerifiedArchiveExtractor(archiveReader, normalized.ToSafetyLimits());
            await extractor.ExtractFilesAsync(normalized.ArchivePath, selected, cancellationToken);

            var included = EnumerateRelativeFiles(exportRoot)
                .Where(x => x != "backup/checksums.sha256" && x != "mem-export-manifest.json")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
            var mediaBytes = mediaFiles.Sum(x => x.SizeBytes);
            var warnings = new List<string>();
            if (verification.Manifest.Capture.RehearsalOnly) warnings.Add("Source archive is rehearsal-only; this stack export is for private rehearsal and must not be activated publicly.");
            if (mediaFiles.Length == 0) warnings.Add("The source fixture contains no media files; non-zero media retrieval remains a release-qualification requirement.");
            warnings.AddRange(conversion.Warnings.Where(x => !x.Contains("rehearsal-only", StringComparison.OrdinalIgnoreCase)));

            var manifest = new
            {
                manifestVersion = 2,
                exportKind = "mem-stack-export",
                createdAtUtc = DateTimeOffset.UtcNow,
                createdBy = "mem-migrate",
                memVersion = "migration-v010-to-v011",
                stack = new
                {
                    stackId = stack.SourceStackId.ToString(), stack.Slug, stack.DisplayName, stack.MatrixServerName,
                    stack.MatrixPublicUrl, stack.ElementPublicUrl
                },
                database = new { engine = "postgresql", dumpFile = "database/synapse.sql", databaseName = "synapse", username = "synapse", present = true },
                matrix = new { homeserverConfig = "matrix/homeserver.yaml", signingKey = "matrix/signing.key", mediaStore = "matrix/media_store", mediaBytes, mediaFiles = (long)mediaFiles.Length, present = true },
                element = new { config = "element/config.json", present = !string.IsNullOrWhiteSpace(stack.ElementConfigurationPath) },
                routes = new { matrixHost = HostFromUrl(stack.MatrixPublicUrl) ?? stack.MatrixServerName, elementHost = HostFromUrl(stack.ElementPublicUrl), requiresDns = true },
                coturn = new { configured = false, publicHost = (string?)null, realm = (string?)null, turnUris = Array.Empty<string>(), sharedSecretPresent = false, userLifetime = (string?)null, allowGuests = (bool?)null },
                restorePolicy = new { canRestoreToFreshMemServer = true, requiresPostgres = true, requiresDomainMapping = true, requiresSigningKey = true, requiresOldServerStoppedForSameServerName = true },
                includedFiles = included,
                warnings
            };
            var manifestPath = Path.Combine(exportRoot, "mem-export-manifest.json");
            await WriteJsonAsync(manifestPath, manifest, cancellationToken);

            var checksumTargets = EnumerateRelativeFiles(exportRoot)
                .Where(x => x != "backup/checksums.sha256")
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            var checksumBuilder = new StringBuilder();
            foreach (var relative in checksumTargets)
            {
                var hash = await Sha256File.ComputeAsync(Path.Combine(exportRoot, relative.Replace('/', Path.DirectorySeparatorChar)), cancellationToken);
                checksumBuilder.Append(hash).Append("  ").Append(Root).Append('/').Append(relative).Append('\n');
            }
            var checksumPath = Path.Combine(backupDir, "checksums.sha256");
            await File.WriteAllTextAsync(checksumPath, checksumBuilder.ToString(), new UTF8Encoding(false), cancellationToken);
            PrivateFilePermissions.EnsureFile(checksumPath);

            var zipPath = Path.Combine(outputDir, $"{stack.Slug}-{normalized.ArtifactId}.memstack.zip");
            CreateDeterministicZip(staging, zipPath);
            PrivateFilePermissions.EnsureFile(zipPath);
            var zipHash = await Sha256File.ComputeAsync(zipPath, cancellationToken);
            var zipBytes = new FileInfo(zipPath).Length;

            var neutral = new
            {
                contractVersion = "mem-migration-import/v1",
                sources = new[] { new { sourceId = verification.Manifest.MigrationId, kind = "mem-v010-capture", product = verification.Manifest.Source.Product, productVersion = verification.Manifest.Source.Version, sourceFingerprint = verification.Manifest.Source.CompletionFingerprint, capturedAtUtc = (DateTimeOffset?)verification.Manifest.Capture.CompletedAtUtc } },
                artifacts = new[] { new { artifactId = normalized.ArtifactId, kind = "mem-stack-export", logicalName = stack.DisplayName, targetKind = "backup-catalog-import", targetKey = stack.Slug, sha256 = zipHash, sizeBytes = zipBytes, required = true } }
            };
            var neutralPath = Path.Combine(outputDir, "migration-intake-manifest.json");
            await WriteJsonAsync(neutralPath, neutral, cancellationToken);
            var neutralHash = await Sha256File.ComputeAsync(neutralPath, cancellationToken);

            return new RehearsalArtifactReport(
                "mem-migrate-rehearsal-artifact-report", 1, normalized.ArtifactId!, verification.Manifest.MigrationId,
                conversion.ConversionId, stack.SourceStackId, stack.MatrixServerName, zipPath, zipHash, zipBytes,
                neutralPath, neutralHash, checksumTargets.Length + 1, mediaFiles.LongLength, mediaBytes,
                warnings.Distinct(StringComparer.Ordinal).ToArray(),
                ["Upload the memstack ZIP through MEM validated imports.", "Preview and commit migration-intake-manifest.json through the private migration intake API.", "Materialise the validated import into Backup Catalog, then create a private restore workspace."]);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static string[] EnumerateRelativeFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Select(x => Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/')).ToArray();

    private static string? HostFromUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true }, ct);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, ct);
        await stream.FlushAsync(ct);
        PrivateFilePermissions.EnsureFile(path);
    }

    private static void CreateDeterministicZip(string stagingRoot, string zipPath)
    {
        using var stream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories).OrderBy(x => Path.GetRelativePath(stagingRoot, x), StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(stagingRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }
}
