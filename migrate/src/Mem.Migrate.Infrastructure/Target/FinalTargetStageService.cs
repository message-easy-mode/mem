using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Rehearsal;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;

namespace Mem.Migrate.Infrastructure.Target;

public sealed class FinalTargetStageService(
    IMigrationArchiveReader archiveReader,
    ISynapseConversionService conversionService,
    IRehearsalArtifactService artifactService,
    ITargetImportService importService,
    ITargetPrivateStageService stageService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public async Task<FinalTargetStageReport> RunAsync(FinalTargetStageOptions options, CancellationToken ct)
    {
        var normalized = options.Normalize();
        var outputDirectory = Path.Combine(normalized.OutputPath, normalized.AttemptId!);
        var reportPath = Path.Combine(outputDirectory, "final-target-stage-report.json");
        if (normalized.Resume && File.Exists(reportPath))
            return JsonSerializer.Deserialize<FinalTargetStageReport>(await File.ReadAllTextAsync(reportPath, ct), JsonOptions)
                ?? throw new InvalidDataException("Completed final target stage report could not be read.");
        if (Directory.Exists(outputDirectory) && !normalized.Resume)
            throw new IOException($"Final target stage output already exists: {outputDirectory}");

        var freeze = JsonSerializer.Deserialize<CutoverFreezeReport>(await File.ReadAllTextAsync(normalized.FreezeReportPath, ct), JsonOptions)
            ?? throw new InvalidDataException("Freeze report could not be parsed.");
        if (!string.Equals(freeze.Status, "Frozen", StringComparison.Ordinal) || !freeze.SourceFrozen)
            throw new InvalidDataException("MM-06B freeze evidence does not prove a frozen source.");
        if (freeze.PublicRoutingMutationOccurred)
            throw new InvalidDataException("Freeze evidence unexpectedly records public routing mutation.");
        if (freeze.Containers.Length == 0 || freeze.Containers.Any(x => !x.Stopped || !x.RestartPolicyDisabled))
            throw new InvalidDataException("Freeze evidence does not prove every source writer is stopped and restart-disabled.");

        var verification = await archiveReader.VerifyAsync(normalized.ArchivePath, normalized.ToSafetyLimits(), ct);
        if (!verification.Valid || verification.Manifest is null) throw new InvalidDataException("Final migration archive verification failed.");
        var manifest = verification.Manifest;
        if (manifest.Capture.RehearsalOnly || !manifest.Capture.SourceFrozen || manifest.Capture.SourceChangedDuringCapture ||
            !string.Equals(manifest.Capture.Kind, "final", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Archive is not a stable final frozen-source capture.");
        if (!string.Equals(manifest.Source.CompletionFingerprint, freeze.SourceFingerprint, StringComparison.Ordinal))
            throw new InvalidDataException("Final archive fingerprint does not match the frozen-source checkpoint.");
        if (manifest.Stacks.Length != 1) throw new NotSupportedException("MM-06D currently requires exactly one captured stack.");

        PrivateFilePermissions.EnsureDirectory(outputDirectory);
        var started = DateTimeOffset.UtcNow;
        var conversionId = $"{normalized.AttemptId}-convert";
        var artifactId = $"{normalized.AttemptId}-artifact";
        var importId = $"{normalized.AttemptId}-import";
        var stageId = $"{normalized.AttemptId}-stage";

        var conversion = await conversionService.ConvertAsync(new ConversionOptions
        {
            ArchivePath = normalized.ArchivePath, WorkspacePath = normalized.WorkspacePath, OutputPath = normalized.OutputPath,
            ConversionId = conversionId, DockerCommand = normalized.DockerCommand, SynapseImage = normalized.SynapseImage,
            PostgresImage = normalized.PostgresImage, CommandTimeoutSeconds = normalized.CommandTimeoutSeconds,
            ReadinessTimeoutSeconds = normalized.ReadinessTimeoutSeconds, Resume = normalized.Resume
        }, ct);
        if (conversion.Status != ConversionLifecycleStatus.Completed) throw new InvalidOperationException("Final conversion did not complete.");

        RehearsalArtifactReport artifact;
        var artifactDirectory = Path.Combine(normalized.OutputPath, artifactId);
        if (normalized.Resume && Directory.Exists(artifactDirectory))
        {
            var zip = Directory.EnumerateFiles(artifactDirectory, "*.memstack.zip").Single();
            var neutral = Path.Combine(artifactDirectory, "migration-intake-manifest.json");
            artifact = new RehearsalArtifactReport("mem-migrate-final-artifact-report", 1, artifactId, manifest.MigrationId,
                conversion.ConversionId, conversion.SourceStackId, conversion.MatrixServerName, zip,
                await Sha256File.ComputeAsync(zip, ct), new FileInfo(zip).Length, neutral,
                await Sha256File.ComputeAsync(neutral, ct), 0, 0, 0, [], []);
        }
        else
        {
            artifact = await artifactService.ExportAsync(new RehearsalArtifactOptions
            {
                ArchivePath = normalized.ArchivePath, ConversionReportPath = Path.Combine(normalized.OutputPath, conversionId, "conversion-report.json"),
                OutputPath = normalized.OutputPath, ArtifactId = artifactId, DockerCommand = normalized.DockerCommand,
                CommandTimeoutSeconds = normalized.CommandTimeoutSeconds
            }, ct);
        }

        var imported = await importService.ImportAsync(new TargetImportOptions
        {
            ManifestPath = artifact.NeutralManifestPath, StackExportPath = artifact.StackExportPath,
            ProfileName = normalized.ProfileName, WorkspacePath = normalized.WorkspacePath, AttemptId = importId,
            DisplayName = $"Final migration {manifest.Stacks[0].DisplayName}", HttpTimeoutSeconds = normalized.HttpTimeoutSeconds,
            AllowInsecureTls = normalized.AllowInsecureTls, Resume = normalized.Resume
        }, ct);
        var staged = await stageService.RunAsync(new TargetPrivateStageOptions
        {
            ImportAttemptId = importId, ProfileName = normalized.ProfileName, WorkspacePath = normalized.WorkspacePath,
            OutputPath = normalized.OutputPath, StageAttemptId = stageId, HttpTimeoutSeconds = normalized.HttpTimeoutSeconds,
            AllowInsecureTls = normalized.AllowInsecureTls, DestroyAfterVerification = false, Resume = normalized.Resume
        }, ct);
        if (!staged.PrivateOnly || !staged.DatabaseImportSucceeded || !staged.SynapseHealthPassed || !staged.PublishedRoutesAbsent || staged.DestroySucceeded)
            throw new InvalidOperationException("Final target candidate did not remain healthy, private, route-free, and retained.");

        var warnings = conversion.Warnings.Concat(artifact.Warnings).Concat(imported.Warnings).Concat(staged.Warnings).Distinct(StringComparer.Ordinal).ToArray();
        var report = new FinalTargetStageReport("mem-final-target-stage-report", 1, normalized.AttemptId!, "Completed", started, DateTimeOffset.UtcNow,
            freeze.FreezeAttemptId, freeze.SourceFingerprint, normalized.ArchivePath, verification.VerifiedZipSha256,
            conversionId, artifactId, importId, stageId, imported.CatalogEntryId, staged.RestoreSessionId, staged.StagingId,
            true, true, true, true, true, reportPath, warnings,
            ["Review final candidate evidence before public activation.", "Do not destroy the retained staging runtime before activation or rollback decision."]);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, CaptureJson.Options), ct);
        PrivateFilePermissions.EnsureFile(reportPath);
        return report;
    }
}
