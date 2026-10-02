using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationPackageRevisionPersistenceTests
{
    [Fact]
    public async Task Persists_separate_active_preview_and_final_revisions_and_links_conversion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateIntake();
        var preview = CreateRevision(intake, 1, "preview", new string('1', 64));
        var final = CreateRevision(intake, 2, "final", new string('2', 64));
        var attempt = CreateAttempt(intake, preview);

        fixture.Db.AddRange(intake, preview, final, attempt);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var persisted = await fixture.Db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.PackageRevision)
            .SingleAsync(x => x.IntakeId == intake.IntakeId);

        Assert.Equal(2, persisted.PackageRevisions.Count);
        Assert.Contains(persisted.PackageRevisions, x =>
            x.Purpose == "preview" &&
            x.ActivePurposeKey == $"{intake.IntakeId}:preview" &&
            x.RehearsalOnly == true &&
            x.SourceFrozen == false);
        Assert.Contains(persisted.PackageRevisions, x =>
            x.Purpose == "final" &&
            x.ActivePurposeKey == $"{intake.IntakeId}:final" &&
            x.RehearsalOnly == false &&
            x.SourceFrozen == true);

        var persistedAttempt = Assert.Single(persisted.ConversionAttempts);
        Assert.NotNull(persistedAttempt.PackageRevision);
        Assert.Equal(preview.PackageRevisionId, persistedAttempt.PackageRevision!.PackageRevisionId);
        Assert.Equal(preview.DecryptedArchiveSha256, persistedAttempt.SourcePackageSha256);

        var revisionEntityType = fixture.Db.Model.FindEntityType(typeof(MigrationPackageRevisionEntity));
        Assert.NotNull(revisionEntityType);
        var forbiddenPrincipalTypes = new[]
        {
            typeof(BackupCatalogEntryEntity),
            typeof(RestoreAttemptEntity),
        };
        Assert.DoesNotContain(
            revisionEntityType.GetForeignKeys(),
            foreignKey => forbiddenPrincipalTypes.Contains(
                foreignKey.PrincipalEntityType.ClrType));
    }

    [Fact]
    public async Task Enforces_one_active_revision_per_purpose()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateIntake();
        fixture.Db.Add(intake);
        fixture.Db.Add(CreateRevision(intake, 1, "preview", new string('1', 64)));
        await fixture.Db.SaveChangesAsync();

        fixture.Db.Add(CreateRevision(intake, 2, "preview", new string('2', 64)));

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Enforces_monotonic_revision_number_per_migration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateIntake();
        var preview = CreateRevision(intake, 1, "preview", new string('1', 64));
        preview.ActivePurposeKey = null;
        preview.RetentionState = "superseded";
        preview.SupersededAtUtc = DateTime.UtcNow;

        var duplicateNumber = CreateRevision(intake, 1, "final", new string('2', 64));
        fixture.Db.AddRange(intake, preview, duplicateNumber);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    private static MigrationIntakeEntity CreateIntake()
    {
        var now = DateTime.UtcNow;
        return new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = $"mig_revision_{Guid.NewGuid():N}",
            DisplayName = "Package revision persistence",
            CreatedAtUtc = now,
        };
    }

    private static MigrationPackageRevisionEntity CreateRevision(
        MigrationIntakeEntity intake,
        int revisionNumber,
        string purpose,
        string packageSha256)
    {
        var isFinal = purpose == "final";
        var now = DateTime.UtcNow.AddMinutes(revisionNumber);
        return new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr_test_{revisionNumber}_{Guid.NewGuid():N}",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = revisionNumber,
            Purpose = purpose,
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:{purpose}",
            CreatedAtUtc = now,
            UploadedAtUtc = now,
            ValidatedAtUtc = now,
            AgeRecipient = $"age1{new string(purpose[0], 58)}",
            RecipientFingerprint = isFinal ? "AAAA-BBBB-CCCC-DDDD" : "1111-2222-3333-4444",
            PackageFileName = $"{purpose}.memmigration.zip.age",
            PackageSizeBytes = 4096 + revisionNumber,
            EncryptedPackageSha256 = new string(isFinal ? 'e' : 'd', 64),
            DecryptedArchiveSha256 = packageSha256,
            ArchiveMigrationId = isFinal ? "source-final-001" : "source-preview-001",
            ArchiveSourceProduct = "MatrixEasyMode",
            ArchiveSourceVersion = "0.1.0",
            ArchiveStackCount = 1,
            CaptureKind = purpose,
            SourceFrozen = isFinal,
            RehearsalOnly = !isFinal,
            VerifiedFileCount = 12,
            VerifiedExpandedBytes = 8192,
            ValidationCode = "validated",
            ValidationSummary = "Validated package evidence.",
        };
    }

    private static MigrationConversionAttemptEntity CreateAttempt(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity revision)
    {
        var now = DateTime.UtcNow;
        return new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = $"conv_revision_{Guid.NewGuid():N}",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            PackageRevision = revision,
            MigrationPackageRevisionEntityId = revision.Id,
            ActiveMigrationKey = null,
            SourcePackageSha256 = revision.DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "0.1.0",
            ConverterId = "mem-migrate-worker",
            ConverterVersion = "mem-conversion-worker-request/v2",
            Status = "completed",
            CurrentStep = "candidate-created",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = now,
        };
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, MemDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public MemDbContext Db { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;

            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new Fixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
