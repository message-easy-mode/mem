using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class SecureMigrationIntakeServiceTests
{
    [Fact]
    public async Task Creates_target_bound_recipient_without_projecting_private_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var protectionProvider = new EphemeralDataProtectionProvider();
        var service = new SecureMigrationIntakeService(
            db,
            new StubAgeKeyPairGenerator(),
            new StubAgePackageDecryptor(),
            protectionProvider,
            CreateConfiguration());

        var result = await service.CreateAsync(
            new SecureMigrationIntakeCreateRequest("Davids server"),
            CancellationToken.None);

        Assert.Equal("awaiting-package", result.Status);
        Assert.StartsWith("age1", result.AgeRecipient, StringComparison.Ordinal);
        Assert.Matches("^[0-9A-F]{4}(?:-[0-9A-F]{4}){3}$", result.RecipientFingerprint);
        Assert.Equal(TimeSpan.FromHours(24), result.ExpiresAtUtc - result.CreatedAtUtc);

        var entity = await db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .SingleAsync();
        var protector = protectionProvider.CreateProtector("MEM.MigrationIntake.AgeIdentity.v1");

        var revision = Assert.Single(entity.PackageRevisions);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal("preview", revision.Purpose);
        Assert.Equal("awaiting-package", revision.Status);
        Assert.Equal("active", revision.RetentionState);
        Assert.Equal($"{entity.IntakeId}:preview", revision.ActivePurposeKey);
        Assert.Equal(result.AgeRecipient, revision.AgeRecipient);
        Assert.Equal(result.RecipientFingerprint, revision.RecipientFingerprint);
        Assert.NotNull(revision.ProtectedAgeIdentity);
        Assert.Equal("AGE-SECRET-KEY-TEST", protector.Unprotect(revision.ProtectedAgeIdentity!));
    }

    [Fact]
    public async Task Validated_secure_package_persists_one_canonical_source_identity()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-secure-intake-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataRoot);
        var archivePath = await MigrationPackageArchiveValidatorTests.CreateArchiveAsync(
            captureKind: "preview",
            sourceFrozen: false,
            rehearsalOnly: true,
            sourceChangedDuringCapture: false,
            sourceStackId: Guid.NewGuid(),
            matrixServerName: "matrix-secure.example.test",
            tamperPayload: false,
            migrationId: "capture-preview-secure");

        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            await using var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Mem:DataRoot"] = dataRoot,
                })
                .Build();
            var service = new SecureMigrationIntakeService(
                db,
                new StubAgeKeyPairGenerator(),
                new CopyingAgePackageDecryptor(archivePath),
                new EphemeralDataProtectionProvider(),
                configuration);
            var created = await service.CreateAsync(
                new SecureMigrationIntakeCreateRequest("Secure source persistence"),
                CancellationToken.None);

            await using var packageStream = new MemoryStream("encrypted"u8.ToArray());
            var package = new FormFile(
                packageStream,
                0,
                packageStream.Length,
                "package",
                "source.memmigration.zip.age");
            var result = await service.UploadAndValidateAsync(
                created.IntakeId,
                package,
                CancellationToken.None);

            Assert.Equal("package-validated", result.Status);
            var source = await db.MigrationSources.AsNoTracking().SingleAsync();
            Assert.Equal("capture-preview-secure", source.SourceId);
            Assert.Equal("mem-v010-capture", source.SourceKind);
            Assert.Equal("MatrixEasyMode", source.Product);
            Assert.Equal("0.1.0", source.ProductVersion);
            Assert.Equal(
                "76c31508f9a852b6a2bb85fabed3c6354d06c9ff7085a75dd72f29588287b1f8",
                source.SourceFingerprint);
            Assert.Equal(
                new DateTime(2026, 7, 20, 23, 57, 7, DateTimeKind.Utc),
                source.CapturedAtUtc);

            var persisted = await db.MigrationIntakes
                .AsNoTracking()
                .Include(x => x.PackageRevisions)
                .SingleAsync();
            var revision = Assert.Single(persisted.PackageRevisions);
            Assert.Equal(result.DecryptedArchiveSha256, revision.DecryptedArchiveSha256);
            Assert.NotNull(revision.ValidatedAtUtc);
        }
        finally
        {
            try
            {
                Directory.Delete(dataRoot, recursive: true);
                File.Delete(archivePath);
                Directory.Delete(archivePath[..^4], recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();

    private sealed class StubAgeKeyPairGenerator : IAgeKeyPairGenerator
    {
        public Task<AgeKeyPair> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AgeKeyPair(
                "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq",
                "AGE-SECRET-KEY-TEST"));
    }

    private sealed class CopyingAgePackageDecryptor(string archivePath) : IAgePackageDecryptor
    {
        public Task DecryptAsync(
            string identity,
            string encryptedPath,
            string outputPath,
            CancellationToken cancellationToken)
        {
            File.Copy(archivePath, outputPath, overwrite: false);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAgePackageDecryptor : IAgePackageDecryptor
    {
        public Task DecryptAsync(
            string identity,
            string encryptedPath,
            string outputPath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Package decryption is not exercised by these secure-intake lifecycle tests.");
    }
}
