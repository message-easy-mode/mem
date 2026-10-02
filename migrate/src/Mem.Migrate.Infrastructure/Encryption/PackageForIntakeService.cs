using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Infrastructure.Encryption;

public sealed class PackageForIntakeService(
    IMigrationArchiveReader archiveReader,
    IAgeEnvelope ageEnvelope,
    TimeProvider? timeProvider = null) : IPackageForIntakeService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<PackageForIntakeReport> PackageAsync(
        PackageForIntakeOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        if (string.IsNullOrWhiteSpace(normalized.ArchivePath))
        {
            throw new ArgumentException(
                "Archive path was not resolved. Use automatic source-capture discovery or provide --archive as an advanced override.",
                nameof(options));
        }

        var actualFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(
            normalized.AgeRecipient);

        if (!string.Equals(
                actualFingerprint,
                normalized.RecipientFingerprint,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Recipient fingerprint mismatch. Expected {normalized.RecipientFingerprint}; " +
                $"calculated {actualFingerprint}.");
        }

        var verification = await archiveReader.VerifyAsync(
            normalized.ArchivePath,
            normalized.ToSafetyLimits(),
            cancellationToken);

        if (!verification.Valid || verification.Manifest is null)
        {
            var findings = verification.Findings.Length == 0
                ? "archive verification failed without findings"
                : string.Join(
                    "; ",
                    verification.Findings.Select(finding =>
                        $"{finding.Code}: {finding.Message}"));
            throw new InvalidDataException(
                $"The source migration archive is invalid: {findings}");
        }

        var manifest = verification.Manifest;
        if (manifest.Stacks.Length != 1)
        {
            throw new InvalidDataException(
                "The selected source capture is not a single-stack migration archive. Create a fresh capture for the selected stack.");
        }

        var selectedStack = manifest.Stacks[0];
        if (normalized.SourceStackId is not null &&
            selectedStack.SourceStackId != normalized.SourceStackId)
        {
            throw new InvalidDataException(
                "The selected source stack does not match the stack bound to the source capture.");
        }

        if (normalized.RequireFinalFrozen &&
            (manifest.Capture.Kind != "final" ||
             !manifest.Capture.SourceFrozen ||
             manifest.Capture.RehearsalOnly ||
             manifest.Capture.SourceChangedDuringCapture))
        {
            throw new InvalidDataException(
                "The selected source archive is not an eligible final frozen capture. " +
                "Required: kind=final, sourceFrozen=true, rehearsalOnly=false, sourceChangedDuringCapture=false.");
        }

        PrivateFilePermissions.EnsureDirectory(normalized.OutputDirectory);
        var completedAtUtc = _timeProvider.GetUtcNow();
        var outputStem = normalized.RequireFinalFrozen
            ? $"mem-migration-final-{completedAtUtc:yyyyMMdd-HHmmss'Z'}"
            : $"mem-migration-{completedAtUtc:yyyyMMdd-HHmmss'Z'}";
        var outputPath = Path.Combine(
            normalized.OutputDirectory,
            $"{outputStem}.memmigration.zip.age");
        var jsonPath = Path.Combine(
            normalized.OutputDirectory,
            $"{outputStem}.package-report.json");
        var markdownPath = Path.Combine(
            normalized.OutputDirectory,
            $"{outputStem}.package-report.md");

        EnsureOutputsDoNotExist(outputPath, jsonPath, markdownPath);

        var encrypted = await ageEnvelope.EncryptAsync(
            normalized.ArchivePath,
            outputPath,
            normalized.AgeRecipient,
            normalized.AgeCommand,
            cancellationToken);

        try
        {
            var report = new PackageForIntakeReport(
                Schema: "mem-package-for-intake-report",
                SchemaVersion: 2,
                IntakeId: normalized.IntakeId,
                PackageRevisionId: string.IsNullOrWhiteSpace(normalized.PackageRevisionId) ? null : normalized.PackageRevisionId,
                CompletedAtUtc: completedAtUtc,
                RecipientFingerprint: actualFingerprint,
                RequestedCaptureKind: normalized.RequireFinalFrozen ? "final" : "any-eligible",
                MigrationId: manifest.MigrationId,
                CaptureKind: manifest.Capture.Kind,
                SourceFrozen: manifest.Capture.SourceFrozen,
                RehearsalOnly: manifest.Capture.RehearsalOnly,
                SourceStackId: selectedStack.SourceStackId,
                SourceStackSlug: selectedStack.Slug,
                MatrixServerName: selectedStack.MatrixServerName,
                StackCount: 1,
                SourceArchivePath: normalized.ArchivePath,
                SourceArchiveSha256: verification.InputSha256,
                SourceArchiveBytes: verification.InputBytes,
                VerifiedFileCount: verification.VerifiedFileCount,
                VerifiedExpandedBytes: verification.VerifiedExpandedBytes,
                EncryptedPackagePath: encrypted.Path,
                EncryptedPackageSha256: encrypted.Sha256,
                EncryptedPackageBytes: encrypted.SizeBytes,
                JsonReportPath: jsonPath,
                MarkdownReportPath: markdownPath);

            await WritePrivateAsync(
                jsonPath,
                JsonSerializer.Serialize(report, CaptureJson.Options),
                cancellationToken);
            await WritePrivateAsync(
                markdownPath,
                RenderMarkdown(report),
                cancellationToken);
            return report;
        }
        catch
        {
            TryDelete(encrypted.Path);
            TryDelete(jsonPath);
            TryDelete(markdownPath);
            throw;
        }
    }

    private static void EnsureOutputsDoNotExist(params string[] paths)
    {
        var existing = paths.FirstOrDefault(File.Exists);
        if (existing is not null)
        {
            throw new IOException($"Package-for-intake output already exists: {existing}");
        }
    }

    private static async Task WritePrivateAsync(
        string path,
        string contents,
        CancellationToken cancellationToken)
    {
        var partial = path + ".partial";
        TryDelete(partial);
        try
        {
            await File.WriteAllTextAsync(
                partial,
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            PrivateFilePermissions.EnsureFile(partial);
            File.Move(partial, path);
            PrivateFilePermissions.EnsureFile(path);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static string RenderMarkdown(PackageForIntakeReport report) =>
        $"""
        # MEM Migration Package for Secure Intake

        - Intake ID: `{report.IntakeId}`
        - Package revision ID: `{report.PackageRevisionId ?? "not supplied"}`
        - Requested capture kind: `{report.RequestedCaptureKind}`
        - Completed UTC: `{report.CompletedAtUtc:O}`
        - Recipient fingerprint: `{report.RecipientFingerprint}`
        - Migration ID: `{report.MigrationId}`
        - Capture kind: `{report.CaptureKind}`
        - Source frozen: `{report.SourceFrozen}`
        - Rehearsal only: `{report.RehearsalOnly}`
        - Source stack ID: `{report.SourceStackId:D}`
        - Source stack: `{report.SourceStackSlug}`
        - Matrix server: `{report.MatrixServerName}`
        - Stacks: `{report.StackCount}`
        - Source archive SHA-256: `{report.SourceArchiveSha256}`
        - Verified files: `{report.VerifiedFileCount}`
        - Verified expanded bytes: `{report.VerifiedExpandedBytes}`
        - Encrypted package: `{report.EncryptedPackagePath}`
        - Encrypted package SHA-256: `{report.EncryptedPackageSha256}`
        - Encrypted package bytes: `{report.EncryptedPackageBytes}`

        The target private age identity is not present in this report.
        """;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup. Preserve the original failure.
        }
    }
}
