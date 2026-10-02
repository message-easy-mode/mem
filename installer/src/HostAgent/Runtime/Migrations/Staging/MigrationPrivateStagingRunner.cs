using System.IO.Compression;
using System.Text.Json;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Data.Entities.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Operator.Migrations;

namespace HostAgent.Runtime.Migrations.Staging;

public sealed class MigrationPrivateStagingRunner(
    PrivateStagingService stagingService,
    IConfiguration configuration,
    ILogger<MigrationPrivateStagingRunner> logger) : IMigrationPrivateStagingRunner
{
    public async Task<PrivateStagingRunResult> CreateAsync(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity packageRevision,
        MigrationCandidateArtifactEntity candidate,
        string stagingRunId,
        string? targetStackSlug,
        CancellationToken ct)
    {
        var dataRoot = ResolveDataRoot();
        var archivePath = MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
            dataRoot,
            intake.IntakeId,
            packageRevision.PackageRevisionId,
            allowLegacyPreviewFallback: packageRevision.Purpose == "preview");

        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException(
                "The authoritative validated migration archive is unavailable.",
                archivePath);
        }

        if (string.IsNullOrWhiteSpace(packageRevision.DecryptedArchiveSha256) ||
            !string.Equals(
                candidate.SourcePackageSha256,
                packageRevision.DecryptedArchiveSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The migration candidate does not belong to the authoritative package revision selected for staging.");
        }

        if (!File.Exists(candidate.ArtifactPath))
        {
            throw new FileNotFoundException(
                "Verified migration candidate database dump is unavailable.",
                candidate.ArtifactPath);
        }

        using var provenance = JsonDocument.Parse(candidate.ProvenanceJson);
        var root = provenance.RootElement;
        var stackIdValue = root.TryGetProperty("SourceStackId", out var sid) ? sid.GetString() : null;
        var serverName = root.TryGetProperty("MatrixServerName", out var msn) ? msn.GetString() : null;
        if (!Guid.TryParse(stackIdValue, out var stackId) ||
            stackId == Guid.Empty ||
            string.IsNullOrWhiteSpace(serverName))
        {
            throw new InvalidDataException(
                "Migration candidate provenance is missing source stack identity.");
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var archiveMaterial = MigrationArchiveMaterialResolver.Resolve(
            archive,
            stackId,
            serverName);

        var materialRoot = Path.Combine(
            dataRoot,
            "migration-staging-material",
            stagingRunId);
        try
        {
            if (Directory.Exists(materialRoot))
            {
                Directory.Delete(materialRoot, true);
            }

            Directory.CreateDirectory(materialRoot);
            var homeserver = Path.Combine(materialRoot, "homeserver.yaml");
            var signing = Path.Combine(materialRoot, "signing.key");
            var media = Path.Combine(materialRoot, "media_store");
            var element = Path.Combine(materialRoot, "element-config.json");
            Directory.CreateDirectory(media);

            ExtractRequired(archive, archiveMaterial.HomeserverConfigurationPath, homeserver);
            ExtractRequired(archive, archiveMaterial.SigningKeyPath, signing);
            if (archiveMaterial.MediaPath is not null)
            {
                ExtractPrefix(
                    archive,
                    archiveMaterial.MediaPath.TrimEnd('/') + "/",
                    media);
            }

            if (archiveMaterial.ElementConfigurationPath is not null)
            {
                ExtractRequired(
                    archive,
                    archiveMaterial.ElementConfigurationPath,
                    element);
            }

            var databaseDumpFormat = PrivateStagingDatabaseImportPolicy
                .ResolveMigrationCandidateDumpFormat(candidate.ArtifactKind);

            var material = new PrivateStagingSourceMaterial(
                PrivateStagingSourceKinds.MigrationCandidate,
                null,
                null,
                archiveMaterial.MatrixServerName,
                intake.DisplayName,
                candidate.ArtifactPath,
                databaseDumpFormat,
                homeserver,
                signing,
                Directory.EnumerateFileSystemEntries(media).Any() ? media : null,
                File.Exists(element) ? element : null);

            return await stagingService.CreatePrivateSynapseFromMaterialAsync(
                candidate.CandidateArtifactId,
                material,
                new PrivateStagingRunRequest(
                    KeepOnFailure: true,
                    PostgresImage: null,
                    SynapseImage: null,
                    TargetStackSlug: targetStackSlug,
                    RequireElementRuntime: true,
                    RequireApprovedRuntimeImages: true),
                ct);
        }
        finally
        {
            try
            {
                if (Directory.Exists(materialRoot))
                {
                    Directory.Delete(materialRoot, recursive: true);
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Could not remove temporary migration staging material. StagingRunId={StagingRunId}",
                    stagingRunId);
            }
        }
    }

    public Task<PrivateStagingRunResult> DestroyAsync(
        string privateStagingId,
        CancellationToken ct) =>
        stagingService.DestroyAsync(privateStagingId, ct);

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static void ExtractRequired(
        ZipArchive archive,
        string name,
        string destination)
    {
        var entry = archive.GetEntry(name)
            ?? throw new InvalidDataException(
                $"Migration archive is missing required entry '{name}'.");
        entry.ExtractToFile(destination, true);
    }

    private static void ExtractPrefix(
        ZipArchive archive,
        string prefix,
        string destinationRoot)
    {
        foreach (var entry in archive.Entries.Where(x =>
                     x.FullName.StartsWith(prefix, StringComparison.Ordinal) &&
                     !string.IsNullOrEmpty(x.Name)))
        {
            var relative = entry.FullName[prefix.Length..];
            if (relative.Split('/').Any(x => x is "" or "." or ".."))
            {
                throw new InvalidDataException(
                    "Migration media archive path is unsafe.");
            }

            var destination = Path.GetFullPath(
                Path.Combine(
                    destinationRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(destinationRoot)
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Migration media archive path escaped its staging root.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }
}
