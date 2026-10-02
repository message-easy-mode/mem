using System.Security.Cryptography;
using System.Text;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Modules.Operator.Migrations;

public sealed class SecureMigrationIntakeService(
    MemDbContext db,
    IAgeKeyPairGenerator ageKeyPairGenerator,
    IAgePackageDecryptor agePackageDecryptor,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration)
{
    internal const int LifetimeHours = 24;
    internal const string ProtectorPurpose = "MEM.MigrationIntake.AgeIdentity.v1";
    private const string InitialPackagePurpose = "preview";
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(ProtectorPurpose);

    public async Task<SecureMigrationIntakeDto> CreateAsync(
        SecureMigrationIntakeCreateRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var keyPair = await ageKeyPairGenerator.GenerateAsync(cancellationToken);
        var intakeId = $"mig_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..40];
        var packageRevisionId = $"mpr_{now:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}"[..40];
        var fingerprint = CreateFingerprint(keyPair.Recipient);
        var protectedIdentity = _protector.Protect(keyPair.Identity);

        var entity = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = intakeId,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName)
                ? "Secure MEM migration"
                : request.DisplayName.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LifecycleStatus = MigrationSessionLifecycleStatuses.Active,
            StateVersion = 1,
        };

        entity.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = packageRevisionId,
            MigrationIntakeEntityId = entity.Id,
            MigrationIntake = entity,
            RevisionNumber = 1,
            Purpose = InitialPackagePurpose,
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = CreateActivePurposeKey(intakeId, InitialPackagePurpose),
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(LifetimeHours),
            AgeRecipient = keyPair.Recipient,
            ProtectedAgeIdentity = protectedIdentity,
            RecipientFingerprint = fingerprint,
        });

        db.MigrationIntakes.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(entity);
    }

    public async Task<SecureMigrationIntakeDto?> GetAsync(
        string intakeId,
        CancellationToken cancellationToken)
    {
        var entity = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .SingleOrDefaultAsync(x => x.IntakeId == intakeId, cancellationToken);

        if (entity is null || !MigrationPackageRevisionAuthority.IsSecureSession(entity))
        {
            return null;
        }

        await ExpireIfRequiredAsync(entity, cancellationToken);
        return ToDto(entity);
    }

    public async Task<SecureMigrationIntakeDto> UploadAndValidateAsync(
        string intakeId,
        IFormFile package,
        CancellationToken cancellationToken)
    {
        const long maximumBytes = 5L * 1024 * 1024 * 1024;
        var entity = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.IntakeId == intakeId, cancellationToken)
            ?? throw new SecureMigrationIntakeException(
                "secure_migration_intake_not_found",
                "Secure migration intake was not found.");

        await ExpireIfRequiredAsync(entity, cancellationToken);
        var revision = ResolveAwaitingRevision(entity);
        if (revision is null ||
            MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
                revision,
                DateTime.UtcNow) != MigrationPackageRevisionAuthority.AwaitingPackageStatus ||
            string.IsNullOrWhiteSpace(revision.ProtectedAgeIdentity))
        {
            var effectiveStatus = revision is null
                ? "package-unavailable"
                : MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
                    revision,
                    DateTime.UtcNow);
            throw new SecureMigrationIntakeException(
                "secure_migration_package_upload_not_allowed",
                $"A package cannot be uploaded while the package revision is '{effectiveStatus}'.");
        }

        if (package.Length <= 0 || package.Length > maximumBytes)
        {
            throw new SecureMigrationIntakeException(
                "secure_migration_package_size_invalid",
                "The encrypted migration package is empty or exceeds the 5 GiB intake limit.");
        }

        if (!package.FileName.EndsWith(".memmigration.zip.age", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecureMigrationIntakeException(
                "secure_migration_package_name_invalid",
                "Choose a .memmigration.zip.age package produced by mem-migrate.");
        }

        var root = ResolveDataRoot();
        var revisionRoot = MigrationPackageRevisionStorage.ResolveRevisionRoot(
            root,
            intakeId,
            revision.PackageRevisionId);
        Directory.CreateDirectory(revisionRoot);
        var uploadOperationId = Guid.NewGuid().ToString("N");
        var encryptedPath = Path.Combine(
            revisionRoot,
            $"{MigrationPackageRevisionStorage.EncryptedArchiveFileName}.{uploadOperationId}.partial");
        var decryptedPath = Path.Combine(
            revisionRoot,
            $"{MigrationPackageRevisionStorage.DecryptedArchiveFileName}.{uploadOperationId}.partial");
        var finalEncryptedPath = MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
            root,
            intakeId,
            revision.PackageRevisionId);
        var finalDecryptedPath = MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
            root,
            intakeId,
            revision.PackageRevisionId);

        try
        {
            await using (var input = package.OpenReadStream())
            await using (var output = new FileStream(
                             encryptedPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             1024 * 1024,
                             true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            var encryptedSha = await HashFileAsync(encryptedPath, cancellationToken);
            var identity = _protector.Unprotect(revision.ProtectedAgeIdentity);
            await agePackageDecryptor.DecryptAsync(
                identity,
                encryptedPath,
                decryptedPath,
                cancellationToken);
            var validation = await MigrationPackageArchiveValidator.ValidateAsync(
                decryptedPath,
                cancellationToken);
            var decryptedSha = await HashFileAsync(decryptedPath, cancellationToken);
            var source = ResolveOrCreateDurableSourceIdentity(entity, validation);
            if (db.Entry(source).State == EntityState.Detached)
            {
                db.MigrationSources.Add(source);
            }

            File.Move(encryptedPath, finalEncryptedPath, true);
            File.Move(decryptedPath, finalDecryptedPath, true);

            var now = DateTime.UtcNow;
            var packagePurpose = validation.CaptureKind;

            revision.Purpose = packagePurpose;
            revision.Status = "package-validated";
            revision.RetentionState = "active";
            revision.ActivePurposeKey = CreateActivePurposeKey(intakeId, packagePurpose);
            revision.PackageFileName = Path.GetFileName(package.FileName);
            revision.PackageSizeBytes = package.Length;
            revision.EncryptedPackageSha256 = encryptedSha;
            revision.DecryptedArchiveSha256 = decryptedSha;
            revision.UploadedAtUtc = now;
            revision.ValidatedAtUtc = now;
            revision.ArchiveMigrationId = validation.MigrationId;
            revision.ArchiveSourceProduct = validation.SourceProduct;
            revision.ArchiveSourceVersion = validation.SourceVersion;
            revision.ArchiveStackCount = validation.StackCount;
            revision.CaptureKind = validation.CaptureKind;
            revision.SourceFrozen = validation.SourceFrozen;
            revision.RehearsalOnly = validation.RehearsalOnly;
            revision.VerifiedFileCount = validation.VerifiedFileCount;
            revision.VerifiedExpandedBytes = validation.VerifiedExpandedBytes;
            revision.ValidationCode = "validated";
            revision.ValidationSummary = "Package archive and checksum evidence validated.";
            revision.ProtectedAgeIdentity = null;

            await db.SaveChangesAsync(cancellationToken);
            return ToDto(entity);
        }
        catch
        {
            TryDelete(encryptedPath);
            TryDelete(decryptedPath);
            throw;
        }
    }

    /// <summary>
    /// Internal target-only boundary for encrypted package handling. The identity is never
    /// projected through an endpoint or browser DTO.
    /// </summary>
    internal async Task<string?> UnprotectIdentityAsync(
        string intakeId,
        CancellationToken cancellationToken)
    {
        var entity = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .SingleOrDefaultAsync(x => x.IntakeId == intakeId, cancellationToken);

        if (entity is null)
        {
            return null;
        }

        await ExpireIfRequiredAsync(entity, cancellationToken);
        var revision = ResolveAwaitingRevision(entity);
        return string.IsNullOrWhiteSpace(revision?.ProtectedAgeIdentity)
            ? null
            : _protector.Unprotect(revision.ProtectedAgeIdentity);
    }

    private async Task ExpireIfRequiredAsync(
        MigrationIntakeEntity entity,
        CancellationToken cancellationToken)
    {
        var revision = ResolveAwaitingRevision(entity);
        if (revision?.ExpiresAtUtc is not { } expiresAtUtc ||
            expiresAtUtc > DateTime.UtcNow)
        {
            return;
        }

        var now = DateTime.UtcNow;
        revision.Status = "expired";
        revision.RetentionState = "retired";
        revision.ActivePurposeKey = null;
        revision.RetiredAtUtc = now;
        revision.ProtectedAgeIdentity = null;
        entity.LifecycleStatus = MigrationSessionLifecycleStatuses.Closed;
        entity.ClosedAtUtc ??= revision.ExpiresAtUtc ?? now;
        entity.ClosureKind ??= "expired";

        await db.SaveChangesAsync(cancellationToken);
    }


    private static MigrationSourceEntity ResolveOrCreateDurableSourceIdentity(
        MigrationIntakeEntity intake,
        MigrationPackageArchiveSummary validation)
    {
        var sources = intake.Sources.ToArray();
        if (sources.Length > 1)
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_source_identity_ambiguous",
                "The Migration Session contains more than one durable source identity.");
        }

        var capturedAtUtc = validation.CaptureCompletedAtUtc.UtcDateTime;
        var fingerprint = validation.CompletionSourceFingerprint.ToLowerInvariant();

        if (sources.Length == 0)
        {
            var source = new MigrationSourceEntity
            {
                Id = Guid.NewGuid(),
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                SourceId = validation.MigrationId,
                SourceKind = "mem-v010-capture",
                Product = validation.SourceProduct,
                ProductVersion = validation.SourceVersion,
                SourceFingerprint = fingerprint,
                CapturedAtUtc = capturedAtUtc,
            };
            intake.Sources.Add(source);
            return source;
        }

        var existing = sources[0];
        if (!string.Equals(existing.SourceId, validation.MigrationId, StringComparison.Ordinal) ||
            !string.Equals(existing.Product, validation.SourceProduct, StringComparison.Ordinal) ||
            !string.Equals(existing.ProductVersion, validation.SourceVersion, StringComparison.Ordinal) ||
            !string.Equals(existing.SourceFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase) ||
            existing.CapturedAtUtc is not { } existingCapturedAtUtc ||
            NormalizeUtc(existingCapturedAtUtc) != capturedAtUtc)
        {
            throw new SecureMigrationIntakeException(
                "migration_archive_source_identity_conflict",
                "The validated migration archive does not match the durable source identity already recorded for this Migration Session.");
        }

        return existing;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static MigrationPackageRevisionEntity? ResolveAwaitingRevision(
        MigrationIntakeEntity entity) =>
        MigrationPackageRevisionAuthority.ResolveAwaitingUpload(entity.PackageRevisions);

    internal static string CreateActivePurposeKey(string intakeId, string purpose) =>
        $"{intakeId}:{purpose}";

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
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

    internal static string CreateFingerprint(string recipient)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(recipient)));
        return string.Join(
            '-',
            Enumerable.Range(0, 4).Select(index => hash.Substring(index * 4, 4)));
    }

    private static SecureMigrationIntakeDto ToDto(MigrationIntakeEntity entity)
    {
        var revision = MigrationPackageRevisionAuthority.ResolveCurrentPackage(
            entity.PackageRevisions)
            ?? throw new InvalidOperationException(
                "The secure Migration Session has no package revision.");

        return new SecureMigrationIntakeDto(
            entity.IntakeId,
            entity.DisplayName,
            ResolveStatus(entity, revision),
            revision.AgeRecipient
                ?? throw new InvalidOperationException(
                    "The active package revision has no age recipient."),
            revision.RecipientFingerprint
                ?? throw new InvalidOperationException(
                    "The active package revision has no recipient fingerprint."),
            entity.CreatedAtUtc,
            revision.ExpiresAtUtc ?? revision.CreatedAtUtc,
            string.IsNullOrWhiteSpace(revision.PackageFileName)
                ? null
                : Path.GetFileName(revision.PackageFileName),
            revision.PackageSizeBytes,
            revision.EncryptedPackageSha256,
            revision.DecryptedArchiveSha256,
            revision.UploadedAtUtc,
            revision.ValidatedAtUtc,
            revision.ArchiveMigrationId,
            revision.ArchiveSourceProduct,
            revision.ArchiveSourceVersion,
            revision.ArchiveStackCount);
    }

    private static string ResolveStatus(
        MigrationIntakeEntity entity,
        MigrationPackageRevisionEntity revision) =>
        string.Equals(
            entity.LifecycleStatus,
            MigrationSessionLifecycleStatuses.Cancelled,
            StringComparison.Ordinal)
            ? "cancelled"
            : MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
                revision,
                DateTime.UtcNow);
}

public sealed class SecureMigrationIntakeException : Exception
{
    public SecureMigrationIntakeException(string code, string message, Exception? inner = null)
        : base(message, inner) => Code = code;

    public string Code { get; }
}
