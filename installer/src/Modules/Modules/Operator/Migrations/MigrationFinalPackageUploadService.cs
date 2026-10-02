using System.Security.Cryptography;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Modules.Operator.Migrations;

/// <summary>
/// Receives the target-bound final encrypted package for the server-resolved active final revision,
/// validates final/frozen semantics, compares immutable source and stack identity with the retained
/// preview revision, and selects the final revision as the current Migration package authority.
/// </summary>
public sealed class MigrationFinalPackageUploadService(
    MemDbContext db,
    IAgePackageDecryptor agePackageDecryptor,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private const long MaximumBytes = 5L * 1024 * 1024 * 1024;
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        SecureMigrationIntakeService.ProtectorPurpose);

    public async Task<MigrationFinalPackageUploadDto> UploadAndValidateAsync(
        string migrationId,
        IFormFile package,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw NotFound();
        }

        var intake = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .Include(x => x.Acceptance)
            .SingleOrDefaultAsync(
                x => x.IntakeId == migrationId,
                cancellationToken)
            ?? throw NotFound();

        if (intake.Acceptance is not null)
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_upload_after_acceptance_not_allowed",
                "A final package cannot be uploaded after Migration Acceptance.",
                StatusCodes.Status409Conflict);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var finalRevision = ResolveActiveRevision(intake, "final")
            ?? throw new MigrationFinalPackageUploadException(
                "final_package_revision_not_found",
                "Create the target-bound final package recipient before uploading the final package.",
                StatusCodes.Status409Conflict);

        if (finalRevision.Status == "awaiting-package" &&
            finalRevision.ExpiresAtUtc is { } expiresAtUtc &&
            expiresAtUtc <= now)
        {
            RetireExpired(finalRevision, now);
            await db.SaveChangesAsync(cancellationToken);
            throw new MigrationFinalPackageUploadException(
                "final_package_recipient_expired",
                "The final package recipient expired. Create a replacement recipient and package the final capture again.",
                StatusCodes.Status409Conflict);
        }

        if (finalRevision.Status != "awaiting-package" ||
            string.IsNullOrWhiteSpace(finalRevision.ProtectedAgeIdentity))
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_upload_not_allowed",
                $"A final package cannot be uploaded while the active final revision is '{finalRevision.Status}'.",
                StatusCodes.Status409Conflict);
        }

        var previewRevision = ResolveActiveRevision(intake, "preview");
        if (previewRevision is null ||
            previewRevision.Status != "package-validated" ||
            previewRevision.CaptureKind != "preview" ||
            previewRevision.SourceFrozen is not false ||
            previewRevision.RehearsalOnly is not true ||
            string.IsNullOrWhiteSpace(previewRevision.DecryptedArchiveSha256))
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_preview_evidence_required",
                "The retained validated preview revision is unavailable or incomplete.",
                StatusCodes.Status409Conflict);
        }

        ValidateUpload(package);

        var dataRoot = ResolveDataRoot();
        var revisionRoot = MigrationPackageRevisionStorage.ResolveRevisionRoot(
            dataRoot,
            migrationId,
            finalRevision.PackageRevisionId);
        Directory.CreateDirectory(revisionRoot);

        var uploadOperationId = Guid.NewGuid().ToString("N");
        var encryptedPartialPath = Path.Combine(
            revisionRoot,
            $"{MigrationPackageRevisionStorage.EncryptedArchiveFileName}.{uploadOperationId}.partial");
        var decryptedPartialPath = Path.Combine(
            revisionRoot,
            $"{MigrationPackageRevisionStorage.DecryptedArchiveFileName}.{uploadOperationId}.partial");
        var encryptedFinalPath = MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
            dataRoot,
            migrationId,
            finalRevision.PackageRevisionId);
        var decryptedFinalPath = MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
            dataRoot,
            migrationId,
            finalRevision.PackageRevisionId);

        if (File.Exists(encryptedFinalPath) || File.Exists(decryptedFinalPath))
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_storage_conflict",
                "Final package storage already contains retained files for this revision.",
                StatusCodes.Status409Conflict);
        }

        var encryptedPromoted = false;
        var decryptedPromoted = false;
        var databasePersisted = false;

        try
        {
            await using (var input = package.OpenReadStream())
            await using (var output = new FileStream(
                             encryptedPartialPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var encryptedSha256 = await HashFileAsync(
                encryptedPartialPath,
                cancellationToken);
            var identity = _protector.Unprotect(finalRevision.ProtectedAgeIdentity);
            await agePackageDecryptor.DecryptAsync(
                identity,
                encryptedPartialPath,
                decryptedPartialPath,
                cancellationToken);

            var finalSummary = await MigrationPackageArchiveValidator.ValidateAsync(
                decryptedPartialPath,
                cancellationToken);
            ValidateFinalSemantics(finalSummary);

            var previewArchivePath =
                MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
                    dataRoot,
                    migrationId,
                    previewRevision.PackageRevisionId,
                    allowLegacyPreviewFallback: true);
            if (!File.Exists(previewArchivePath))
            {
                throw new MigrationFinalPackageUploadException(
                    "final_package_preview_archive_unavailable",
                    "The retained preview archive is unavailable for source identity comparison.",
                    StatusCodes.Status409Conflict);
            }

            var previewHash = await HashFileAsync(
                previewArchivePath,
                cancellationToken);
            if (!string.Equals(
                    previewHash,
                    previewRevision.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new MigrationFinalPackageUploadException(
                    "final_package_preview_archive_mismatch",
                    "The retained preview archive no longer matches its immutable package evidence.",
                    StatusCodes.Status409Conflict);
            }

            var previewSummary = await MigrationPackageArchiveValidator.ValidateAsync(
                previewArchivePath,
                cancellationToken);
            ValidatePreviewSemantics(previewSummary);
            ValidateIdentityContinuity(previewSummary, finalSummary);

            var decryptedSha256 = await HashFileAsync(
                decryptedPartialPath,
                cancellationToken);

            try
            {
                File.Move(encryptedPartialPath, encryptedFinalPath);
                encryptedPromoted = true;
                File.Move(decryptedPartialPath, decryptedFinalPath);
                decryptedPromoted = true;
            }
            catch (IOException exception)
            {
                throw new MigrationFinalPackageUploadException(
                    "final_package_storage_conflict",
                    "Final package storage was claimed concurrently. Refresh the Migration Session and use the retained result.",
                    StatusCodes.Status409Conflict,
                    exception);
            }

            finalRevision.Status = "package-validated";
            finalRevision.PackageFileName = Path.GetFileName(package.FileName);
            finalRevision.PackageSizeBytes = package.Length;
            finalRevision.EncryptedPackageSha256 = encryptedSha256;
            finalRevision.DecryptedArchiveSha256 = decryptedSha256;
            finalRevision.UploadedAtUtc = now;
            finalRevision.ValidatedAtUtc = now;
            finalRevision.ArchiveMigrationId = finalSummary.MigrationId;
            finalRevision.ArchiveSourceProduct = finalSummary.SourceProduct;
            finalRevision.ArchiveSourceVersion = finalSummary.SourceVersion;
            finalRevision.ArchiveStackCount = finalSummary.StackCount;
            finalRevision.CaptureKind = finalSummary.CaptureKind;
            finalRevision.SourceFrozen = finalSummary.SourceFrozen;
            finalRevision.RehearsalOnly = finalSummary.RehearsalOnly;
            finalRevision.VerifiedFileCount = finalSummary.VerifiedFileCount;
            finalRevision.VerifiedExpandedBytes = finalSummary.VerifiedExpandedBytes;
            finalRevision.ValidationCode = "final-package-authority-selected";
            finalRevision.ValidationSummary =
                "Final frozen package validated, target-recipient binding proved, and source/stack identity matched the retained preview revision.";
            finalRevision.ProtectedAgeIdentity = null;

            // The package revision is the sole package authority. The Session root
            // contains lifecycle state only.
            await db.SaveChangesAsync(cancellationToken);
            databasePersisted = true;

            return new MigrationFinalPackageUploadDto(
                intake.IntakeId,
                finalRevision.PackageRevisionId,
                finalRevision.RevisionNumber,
                finalRevision.Status,
                finalRevision.PackageFileName!,
                finalRevision.PackageSizeBytes!.Value,
                finalRevision.EncryptedPackageSha256!,
                finalRevision.DecryptedArchiveSha256!,
                finalRevision.ArchiveMigrationId!,
                finalSummary.StartSourceFingerprint,
                finalSummary.CompletionSourceFingerprint,
                finalRevision.ArchiveStackCount!.Value,
                finalRevision.CaptureKind!,
                finalRevision.SourceFrozen!.Value,
                finalRevision.RehearsalOnly!.Value,
                finalRevision.ValidatedAtUtc!.Value,
                AuthoritySelected: true);
        }
        catch
        {
            TryDelete(encryptedPartialPath);
            TryDelete(decryptedPartialPath);

            if (!databasePersisted)
            {
                if (encryptedPromoted)
                {
                    TryDelete(encryptedFinalPath);
                }

                if (decryptedPromoted)
                {
                    TryDelete(decryptedFinalPath);
                }
            }

            throw;
        }
    }

    private static void ValidateUpload(IFormFile package)
    {
        if (package.Length <= 0 || package.Length > MaximumBytes)
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_size_invalid",
                "The encrypted final migration package is empty or exceeds the 5 GiB intake limit.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (!package.FileName.EndsWith(
                ".memmigration.zip.age",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_name_invalid",
                "Choose a .memmigration.zip.age package produced by mem-migrate.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static void ValidateFinalSemantics(MigrationPackageArchiveSummary summary)
    {
        if (summary.CaptureKind != "final" ||
            !summary.SourceFrozen ||
            summary.RehearsalOnly ||
            summary.SourceChangedDuringCapture ||
            !string.Equals(
                summary.StartSourceFingerprint,
                summary.CompletionSourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_capture_semantics_invalid",
                "The uploaded package is not a stable final frozen source capture.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    private static void ValidatePreviewSemantics(MigrationPackageArchiveSummary summary)
    {
        if (summary.CaptureKind != "preview" ||
            summary.SourceFrozen ||
            !summary.RehearsalOnly ||
            summary.SourceChangedDuringCapture)
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_preview_evidence_invalid",
                "The retained preview archive has invalid rehearsal semantics.",
                StatusCodes.Status409Conflict);
        }
    }

    private static void ValidateIdentityContinuity(
        MigrationPackageArchiveSummary preview,
        MigrationPackageArchiveSummary final)
    {
        if (!string.Equals(preview.SourceProduct, final.SourceProduct, StringComparison.Ordinal) ||
            !string.Equals(preview.SourceVersion, final.SourceVersion, StringComparison.Ordinal) ||
            !string.Equals(preview.LegacyMigration, final.LegacyMigration, StringComparison.Ordinal))
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_source_identity_drift",
                "The final package source product, version, or legacy schema identity differs from the rehearsal package.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var previewStacks = preview.Stacks
            .OrderBy(stack => stack.SourceStackId)
            .ToArray();
        var finalStacks = final.Stacks
            .OrderBy(stack => stack.SourceStackId)
            .ToArray();

        if (previewStacks.Length != finalStacks.Length ||
            previewStacks.Where((stack, index) =>
                stack.SourceStackId != finalStacks[index].SourceStackId ||
                !string.Equals(
                    stack.Slug,
                    finalStacks[index].Slug,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    stack.MatrixServerName,
                    finalStacks[index].MatrixServerName,
                    StringComparison.OrdinalIgnoreCase))
                .Any())
        {
            throw new MigrationFinalPackageUploadException(
                "final_package_stack_identity_drift",
                "The final package stack identity or Matrix server name differs from the rehearsal package.",
                StatusCodes.Status422UnprocessableEntity);
        }
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static MigrationPackageRevisionEntity? ResolveActiveRevision(
        MigrationIntakeEntity intake,
        string purpose) =>
        intake.PackageRevisions.SingleOrDefault(revision =>
            revision.ActivePurposeKey ==
            SecureMigrationIntakeService.CreateActivePurposeKey(
                intake.IntakeId,
                purpose));

    private static void RetireExpired(
        MigrationPackageRevisionEntity revision,
        DateTime now)
    {
        revision.Status = "expired";
        revision.RetentionState = "retired";
        revision.ActivePurposeKey = null;
        revision.RetiredAtUtc = now;
        revision.ProtectedAgeIdentity = null;
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

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

    private static MigrationFinalPackageUploadException NotFound() =>
        new(
            "migration_session_not_found",
            "Migration session was not found.",
            StatusCodes.Status404NotFound);
}

public sealed record MigrationFinalPackageUploadDto(
    string MigrationId,
    string PackageRevisionId,
    int RevisionNumber,
    string Status,
    string PackageFileName,
    long PackageSizeBytes,
    string EncryptedPackageSha256,
    string DecryptedArchiveSha256,
    string ArchiveMigrationId,
    string StartSourceFingerprint,
    string CompletionSourceFingerprint,
    int ArchiveStackCount,
    string CaptureKind,
    bool SourceFrozen,
    bool RehearsalOnly,
    DateTime ValidatedAtUtc,
    bool AuthoritySelected);

public sealed class MigrationFinalPackageUploadException : Exception
{
    public MigrationFinalPackageUploadException(
        string code,
        string message,
        int statusCode,
        Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
