using System.Security.Cryptography;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationFinalPackageUploadServiceTests
{
    private static readonly Guid SourceStackId =
        Guid.Parse("9230081f-ba21-4e53-8fbe-b3cc7bd1b441");
    private static readonly DateTime Now =
        new(2026, 7, 18, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Validates_final_package_preserves_preview_and_selects_final_authority()
    {
        await using var fixture = await Fixture.CreateAsync(
            finalSourceStackId: SourceStackId,
            finalMatrixServerName: "matrix-davids.deltabox.dev",
            finalCaptureKind: "final",
            finalSourceFrozen: true,
            finalRehearsalOnly: false,
            finalSourceChanged: false);

        var result = await fixture.Service.UploadAndValidateAsync(
            fixture.IntakeId,
            CreateUpload(),
            CancellationToken.None);

        Assert.Equal(fixture.IntakeId, result.MigrationId);
        Assert.Equal(fixture.FinalRevisionId, result.PackageRevisionId);
        Assert.Equal("package-validated", result.Status);
        Assert.Equal("final", result.CaptureKind);
        Assert.True(result.SourceFrozen);
        Assert.False(result.RehearsalOnly);
        Assert.True(result.AuthoritySelected);

        fixture.Db.ChangeTracker.Clear();
        var intake = await fixture.Db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .SingleAsync();

        var preview = Assert.Single(
            intake.PackageRevisions,
            revision => revision.Purpose == "preview");
        Assert.Equal("package-validated", preview.Status);
        Assert.Equal(fixture.PreviewSha256, preview.DecryptedArchiveSha256);
        Assert.Equal($"{fixture.IntakeId}:preview", preview.ActivePurposeKey);

        var final = Assert.Single(
            intake.PackageRevisions,
            revision => revision.Purpose == "final");
        Assert.Equal("package-validated", final.Status);
        Assert.Equal("final-package-authority-selected", final.ValidationCode);
        Assert.Null(final.ProtectedAgeIdentity);
        Assert.Equal($"{fixture.IntakeId}:final", final.ActivePurposeKey);
        Assert.Equal(new string('1', 64), preview.EncryptedPackageSha256);
        Assert.Equal("capture-preview-20260716", preview.ArchiveMigrationId);
        Assert.NotEqual(final.DecryptedArchiveSha256, preview.DecryptedArchiveSha256);
        Assert.NotEqual(final.AgeRecipient, preview.AgeRecipient);

        Assert.True(File.Exists(fixture.PreviewArchivePath));
        Assert.True(File.Exists(
            MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                fixture.DataRoot,
                fixture.IntakeId,
                fixture.FinalRevisionId)));
        Assert.True(File.Exists(
            MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
                fixture.DataRoot,
                fixture.IntakeId,
                fixture.FinalRevisionId)));
    }

    [Fact]
    public async Task Rejects_preview_or_rehearsal_archive_for_final_revision()
    {
        await using var fixture = await Fixture.CreateAsync(
            finalSourceStackId: SourceStackId,
            finalMatrixServerName: "matrix-davids.deltabox.dev",
            finalCaptureKind: "preview",
            finalSourceFrozen: false,
            finalRehearsalOnly: true,
            finalSourceChanged: false);

        var exception = await Assert.ThrowsAsync<MigrationFinalPackageUploadException>(
            () => fixture.Service.UploadAndValidateAsync(
                fixture.IntakeId,
                CreateUpload(),
                CancellationToken.None));

        Assert.Equal("final_package_capture_semantics_invalid", exception.Code);
        await AssertFinalRevisionStillAwaitingAsync(fixture);
    }

    [Fact]
    public async Task Rejects_legacy_source_schema_identity_drift()
    {
        await using var fixture = await Fixture.CreateAsync(
            finalSourceStackId: SourceStackId,
            finalMatrixServerName: "matrix-davids.deltabox.dev",
            finalCaptureKind: "final",
            finalSourceFrozen: true,
            finalRehearsalOnly: false,
            finalSourceChanged: false,
            finalLegacyMigration: "20260718000000_UnexpectedSourceSchema");

        var exception = await Assert.ThrowsAsync<MigrationFinalPackageUploadException>(
            () => fixture.Service.UploadAndValidateAsync(
                fixture.IntakeId,
                CreateUpload(),
                CancellationToken.None));

        Assert.Equal("final_package_source_identity_drift", exception.Code);
        await AssertFinalRevisionStillAwaitingAsync(fixture);
    }

    [Fact]
    public async Task Rejects_stack_or_matrix_identity_drift()
    {
        await using var fixture = await Fixture.CreateAsync(
            finalSourceStackId: Guid.NewGuid(),
            finalMatrixServerName: "matrix-other.example",
            finalCaptureKind: "final",
            finalSourceFrozen: true,
            finalRehearsalOnly: false,
            finalSourceChanged: false);

        var exception = await Assert.ThrowsAsync<MigrationFinalPackageUploadException>(
            () => fixture.Service.UploadAndValidateAsync(
                fixture.IntakeId,
                CreateUpload(),
                CancellationToken.None));

        Assert.Equal("final_package_stack_identity_drift", exception.Code);
        await AssertFinalRevisionStillAwaitingAsync(fixture);
    }

    [Fact]
    public async Task Preserves_concurrently_retained_storage_and_returns_controlled_conflict()
    {
        await using var fixture = await Fixture.CreateAsync(
            finalSourceStackId: SourceStackId,
            finalMatrixServerName: "matrix-davids.deltabox.dev",
            finalCaptureKind: "final",
            finalSourceFrozen: true,
            finalRehearsalOnly: false,
            finalSourceChanged: false,
            createConcurrentStorageConflict: true);

        var exception = await Assert.ThrowsAsync<MigrationFinalPackageUploadException>(
            () => fixture.Service.UploadAndValidateAsync(
                fixture.IntakeId,
                CreateUpload(),
                CancellationToken.None));

        Assert.Equal("final_package_storage_conflict", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);

        var encryptedPath =
            MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
                fixture.DataRoot,
                fixture.IntakeId,
                fixture.FinalRevisionId);
        var decryptedPath =
            MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                fixture.DataRoot,
                fixture.IntakeId,
                fixture.FinalRevisionId);

        Assert.Equal("concurrent-encrypted", await File.ReadAllTextAsync(encryptedPath));
        Assert.Equal("concurrent-decrypted", await File.ReadAllTextAsync(decryptedPath));
        await AssertFinalRevisionStillAwaitingAsync(
            fixture,
            expectRetainedFinalStorage: true);
    }

    private static async Task AssertFinalRevisionStillAwaitingAsync(
        Fixture fixture,
        bool expectRetainedFinalStorage = false)
    {
        fixture.Db.ChangeTracker.Clear();
        var intake = await fixture.Db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .SingleAsync();
        var final = Assert.Single(
            intake.PackageRevisions,
            revision => revision.Purpose == "final");
        Assert.Equal("awaiting-package", final.Status);
        Assert.NotNull(final.ProtectedAgeIdentity);
        Assert.Null(final.DecryptedArchiveSha256);
        Assert.Equal(
            expectRetainedFinalStorage,
            File.Exists(
                MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                    fixture.DataRoot,
                    fixture.IntakeId,
                    fixture.FinalRevisionId)));
    }

    private static IFormFile CreateUpload()
    {
        var bytes = "encrypted-placeholder"u8.ToArray();
        return new FormFile(
            new MemoryStream(bytes),
            0,
            bytes.LongLength,
            "package",
            "mem-migration-final-20260718.memmigration.zip.age");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly string _previewArchiveSourcePath;
        private readonly string _finalArchivePath;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationFinalPackageUploadService service,
            string dataRoot,
            string intakeId,
            string finalRevisionId,
            string previewArchivePath,
            string previewSha256,
            string previewArchiveSourcePath,
            string finalArchivePath)
        {
            _connection = connection;
            Db = db;
            Service = service;
            DataRoot = dataRoot;
            IntakeId = intakeId;
            FinalRevisionId = finalRevisionId;
            PreviewArchivePath = previewArchivePath;
            PreviewSha256 = previewSha256;
            _previewArchiveSourcePath = previewArchiveSourcePath;
            _finalArchivePath = finalArchivePath;
        }

        public MemDbContext Db { get; }
        public MigrationFinalPackageUploadService Service { get; }
        public string DataRoot { get; }
        public string IntakeId { get; }
        public string FinalRevisionId { get; }
        public string PreviewArchivePath { get; }
        public string PreviewSha256 { get; }

        public static async Task<Fixture> CreateAsync(
            Guid finalSourceStackId,
            string finalMatrixServerName,
            string finalCaptureKind,
            bool finalSourceFrozen,
            bool finalRehearsalOnly,
            bool finalSourceChanged,
            string finalLegacyMigration = "20260507085953_InitialApplicationSchema",
            bool createConcurrentStorageConflict = false)
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-final-package-upload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataRoot);

            var previewArchive = await MigrationPackageArchiveValidatorTests.CreateArchiveAsync(
                captureKind: "preview",
                sourceFrozen: false,
                rehearsalOnly: true,
                sourceChangedDuringCapture: false,
                sourceStackId: SourceStackId,
                matrixServerName: "matrix-davids.deltabox.dev",
                tamperPayload: false,
                migrationId: "capture-preview-20260716");
            var finalArchive = await MigrationPackageArchiveValidatorTests.CreateArchiveAsync(
                captureKind: finalCaptureKind,
                sourceFrozen: finalSourceFrozen,
                rehearsalOnly: finalRehearsalOnly,
                sourceChangedDuringCapture: finalSourceChanged,
                sourceStackId: finalSourceStackId,
                matrixServerName: finalMatrixServerName,
                tamperPayload: false,
                migrationId: "capture-final-20260718",
                legacyMigration: finalLegacyMigration);

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var protectionProvider = new EphemeralDataProtectionProvider();
            var protector = protectionProvider.CreateProtector(
                SecureMigrationIntakeService.ProtectorPurpose);
            var intakeId = "mig_final_package_upload";
            var previewRevisionId = "mpr_preview_upload";
            var finalRevisionId = "mpr_final_upload";
            var previewSha256 = await HashFileAsync(previewArchive);

            var intake = new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = intakeId,
                DisplayName = "Final package upload",
                CreatedAtUtc = Now.AddDays(-2),
            };

            intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = previewRevisionId,
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                RevisionNumber = 1,
                Purpose = "preview",
                Status = "package-validated",
                RetentionState = "active",
                ActivePurposeKey = $"{intakeId}:preview",
                CreatedAtUtc = Now.AddDays(-2),
                AgeRecipient = "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
                RecipientFingerprint = "1111-2222-3333-4444",
                PackageFileName = "preview.memmigration.zip.age",
                PackageSizeBytes = new FileInfo(previewArchive).Length,
                EncryptedPackageSha256 = new string('1', 64),
                DecryptedArchiveSha256 = previewSha256,
                UploadedAtUtc = Now.AddDays(-2).AddMinutes(1),
                ValidatedAtUtc = Now.AddDays(-2).AddMinutes(2),
                ArchiveMigrationId = "capture-preview-20260716",
                ArchiveSourceProduct = "MatrixEasyMode",
                ArchiveSourceVersion = "0.1.0",
                ArchiveStackCount = 1,
                CaptureKind = "preview",
                SourceFrozen = false,
                RehearsalOnly = true,
                ValidationCode = "validated",
            });

            intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = finalRevisionId,
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                RevisionNumber = 2,
                Purpose = "final",
                Status = "awaiting-package",
                RetentionState = "active",
                ActivePurposeKey = $"{intakeId}:final",
                CreatedAtUtc = Now,
                ExpiresAtUtc = Now.AddHours(24),
                AgeRecipient =
                    "age1rrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrrr",
                RecipientFingerprint = "AAAA-BBBB-CCCC-DDDD",
                ProtectedAgeIdentity = protector.Protect("AGE-SECRET-KEY-FINAL"),
            });

            db.MigrationIntakes.Add(intake);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var previewTargetPath =
                MigrationPackageRevisionStorage.ResolveLegacyDecryptedArchivePath(
                    dataRoot,
                    intakeId);
            Directory.CreateDirectory(Path.GetDirectoryName(previewTargetPath)!);
            File.Copy(previewArchive, previewTargetPath);

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot,
                })
                .Build();
            var finalEncryptedPath =
                MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
                    dataRoot,
                    intakeId,
                    finalRevisionId);
            var finalDecryptedPath =
                MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                    dataRoot,
                    intakeId,
                    finalRevisionId);
            Action? afterDecrypt = createConcurrentStorageConflict
                ? () =>
                {
                    File.WriteAllText(finalEncryptedPath, "concurrent-encrypted");
                    File.WriteAllText(finalDecryptedPath, "concurrent-decrypted");
                }
                : null;

            var service = new MigrationFinalPackageUploadService(
                db,
                new CopyingAgePackageDecryptor(finalArchive, afterDecrypt),
                protectionProvider,
                configuration,
                new FixedTimeProvider(Now));

            return new Fixture(
                connection,
                db,
                service,
                dataRoot,
                intakeId,
                finalRevisionId,
                previewTargetPath,
                previewSha256,
                previewArchive,
                finalArchive);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            TryDelete(_previewArchiveSourcePath);
            TryDelete(_finalArchivePath);
            try
            {
                if (Directory.Exists(DataRoot))
                {
                    Directory.Delete(DataRoot, recursive: true);
                }
            }
            catch
            {
                // Best-effort test cleanup.
            }
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
                // Best-effort test cleanup.
            }
        }
    }

    private sealed class CopyingAgePackageDecryptor(
        string archivePath,
        Action? afterDecrypt = null) : IAgePackageDecryptor
    {
        public Task DecryptAsync(
            string identity,
            string encryptedPath,
            string outputPath,
            CancellationToken cancellationToken)
        {
            Assert.Equal("AGE-SECRET-KEY-FINAL", identity);
            File.Copy(archivePath, outputPath);
            afterDecrypt?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(value, TimeSpan.Zero);
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream))
            .ToLowerInvariant();
    }
}
