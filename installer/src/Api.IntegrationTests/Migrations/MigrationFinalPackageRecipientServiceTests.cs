using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationFinalPackageRecipientServiceTests
{
    [Fact]
    public async Task Creates_and_idempotently_resumes_a_target_bound_final_recipient()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateRehearsalQualifiedIntake(fixture.Now);
        fixture.Db.MigrationIntakes.Add(intake);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var first = await fixture.Service.CreateOrResumeAsync(
            intake.IntakeId,
            CancellationToken.None);
        var second = await fixture.Service.CreateOrResumeAsync(
            intake.IntakeId,
            CancellationToken.None);

        Assert.False(first.ResumedExisting);
        Assert.True(second.ResumedExisting);
        Assert.Equal(first.PackageRevisionId, second.PackageRevisionId);
        Assert.Equal(2, first.RevisionNumber);
        Assert.Equal("final", first.Purpose);
        Assert.Equal("awaiting-package", first.Status);
        Assert.StartsWith("age1", first.AgeRecipient, StringComparison.Ordinal);
        Assert.Matches("^[0-9A-F]{4}(?:-[0-9A-F]{4}){3}$", first.RecipientFingerprint);
        Assert.Equal(TimeSpan.FromHours(24), first.ExpiresAtUtc - first.CreatedAtUtc);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.MigrationIntakes
            .Include(x => x.PackageRevisions)
            .SingleAsync();
        Assert.Equal(2, persisted.PackageRevisions.Count);

        var preview = Assert.Single(persisted.PackageRevisions, x => x.Purpose == "preview");
        Assert.Equal("package-validated", preview.Status);
        Assert.Equal($"{intake.IntakeId}:preview", preview.ActivePurposeKey);

        var final = Assert.Single(persisted.PackageRevisions, x => x.Purpose == "final");
        Assert.Equal("awaiting-package", final.Status);
        Assert.Equal("active", final.RetentionState);
        Assert.Equal($"{intake.IntakeId}:final", final.ActivePurposeKey);
        Assert.NotNull(final.ProtectedAgeIdentity);
        Assert.DoesNotContain("AGE-SECRET-KEY-", final.ProtectedAgeIdentity, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(first, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("AGE-SECRET-KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedAgeIdentity", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retires_an_expired_final_recipient_before_creating_the_next_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateRehearsalQualifiedIntake(fixture.Now);
        intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_expired_final",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 2,
            Purpose = "final",
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:final",
            CreatedAtUtc = fixture.Now.AddHours(-25),
            ExpiresAtUtc = fixture.Now.AddMinutes(-1),
            AgeRecipient = StubAgeKeyPairGenerator.Recipient,
            ProtectedAgeIdentity = "protected-expired",
            RecipientFingerprint = "AAAA-BBBB-CCCC-DDDD",
        });
        fixture.Db.MigrationIntakes.Add(intake);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var created = await fixture.Service.CreateOrResumeAsync(
            intake.IntakeId,
            CancellationToken.None);

        Assert.Equal(3, created.RevisionNumber);
        fixture.Db.ChangeTracker.Clear();
        var revisions = await fixture.Db.MigrationPackageRevisions
            .OrderBy(x => x.RevisionNumber)
            .ToListAsync();
        var expired = Assert.Single(revisions, x => x.PackageRevisionId == "mpr_expired_final");
        Assert.Equal("expired", expired.Status);
        Assert.Equal("retired", expired.RetentionState);
        Assert.Null(expired.ActivePurposeKey);
        Assert.Null(expired.ProtectedAgeIdentity);
        Assert.NotNull(expired.RetiredAtUtc);

        var activeFinal = Assert.Single(revisions, x => x.ActivePurposeKey == $"{intake.IntakeId}:final");
        Assert.Equal(created.PackageRevisionId, activeFinal.PackageRevisionId);
    }

    [Theory]
    [InlineData(false, true, "final_package_preview_evidence_required")]
    [InlineData(true, false, "final_package_rehearsal_required")]
    public async Task Fails_closed_without_preview_and_private_rehearsal_evidence(
        bool includePreview,
        bool includeVerifiedStaging,
        string expectedCode)
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateRehearsalQualifiedIntake(fixture.Now);
        if (!includePreview)
        {
            var conversion = Assert.Single(intake.ConversionAttempts);
            conversion.PackageRevision = null;
            conversion.MigrationPackageRevisionEntityId = null;
            intake.PackageRevisions.Clear();
        }
        if (!includeVerifiedStaging)
        {
            intake.StagingRuns.Clear();
        }
        fixture.Db.MigrationIntakes.Add(intake);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var exception = await Assert.ThrowsAsync<MigrationFinalPackageRecipientException>(
            () => fixture.Service.CreateOrResumeAsync(
                intake.IntakeId,
                CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries<MigrationPackageRevisionEntity>(),
            entry => entry.Entity.Purpose == "final");
    }

    private static MigrationIntakeEntity CreateRehearsalQualifiedIntake(DateTime now)
    {
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_final_recipient",
            DisplayName = "Final recipient",
            CreatedAtUtc = now.AddDays(-2),
        };

        var preview = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_preview",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:preview",
            CreatedAtUtc = now.AddDays(-2),
            UploadedAtUtc = now.AddDays(-2).AddMinutes(5),
            ValidatedAtUtc = now.AddDays(-2).AddMinutes(6),
            AgeRecipient = StubAgeKeyPairGenerator.Recipient,
            RecipientFingerprint = "1111-2222-3333-4444",
            PackageFileName = "preview.memmigration.zip.age",
            PackageSizeBytes = 1024,
            EncryptedPackageSha256 = new string('b', 64),
            DecryptedArchiveSha256 = new string('a', 64),
            ArchiveMigrationId = "source-preview",
            ArchiveSourceProduct = "MatrixEasyMode",
            ArchiveSourceVersion = "0.1.0",
            ArchiveStackCount = 1,
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
            ValidationCode = "validated",
            ValidationSummary = "Preview validated.",
        };
        intake.PackageRevisions.Add(preview);

        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "conv_final_recipient",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            MigrationPackageRevisionEntityId = preview.Id,
            PackageRevision = preview,
            SourcePackageSha256 = preview.DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "0.1.0",
            ConverterId = "mem-migrate-worker",
            ConverterVersion = "v2",
            Status = "completed",
            CurrentStep = "completed",
            CreatedAtUtc = now.AddDays(-1),
            UpdatedAtUtc = now.AddDays(-1),
            CompletedAtUtc = now.AddDays(-1),
        };
        intake.ConversionAttempts.Add(conversion);

        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = "mca_final_recipient",
            MigrationConversionAttemptEntityId = conversion.Id,
            ConversionAttempt = conversion,
            ArtifactKind = "synapse-postgresql-conversion",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = preview.DecryptedArchiveSha256!,
            ArtifactSha256 = new string('c', 64),
            ManifestSha256 = new string('d', 64),
            ChecksumsSha256 = new string('e', 64),
            ProvenanceJson = "{}",
            VerificationStatus = "verified",
            RetentionState = "active",
            StorageKind = "migration-owned",
            ArtifactPath = "/trusted/candidate",
            CreatedAtUtc = now.AddHours(-12),
            VerifiedAtUtc = now.AddHours(-11),
        };
        conversion.CandidateArtifact = candidate;

        intake.StagingRuns.Add(new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = "mstg_final_recipient",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            CandidateArtifact = candidate,
            MigrationCandidateArtifactEntityId = candidate.Id,
            ActiveMigrationKey = intake.IntakeId,
            Status = "verified",
            CurrentStep = "private-verification-complete",
            CreatedAtUtc = now.AddHours(-10),
            UpdatedAtUtc = now.AddHours(-9),
            CompletedAtUtc = now.AddHours(-9),
            PrivateOnly = true,
            PublicRoutesCreated = false,
            DatabaseImportSucceeded = true,
            SynapseHealthPassed = true,
            ElementConfigPresent = true,
        });

        return intake;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationFinalPackageRecipientService service,
            DateTime now)
        {
            _connection = connection;
            Db = db;
            Service = service;
            Now = now;
        }

        public MemDbContext Db { get; }
        public MigrationFinalPackageRecipientService Service { get; }
        public DateTime Now { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var now = new DateTime(2026, 7, 18, 2, 0, 0, DateTimeKind.Utc);
            var service = new MigrationFinalPackageRecipientService(
                db,
                new StubAgeKeyPairGenerator(),
                new EphemeralDataProtectionProvider(),
                new FixedTimeProvider(now));
            return new Fixture(connection, db, service, now);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }

    private sealed class StubAgeKeyPairGenerator : IAgeKeyPairGenerator
    {
        internal const string Recipient =
            "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";

        public Task<AgeKeyPair> GenerateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new AgeKeyPair(
                Recipient,
                "AGE-SECRET-KEY-FINAL-TEST"));
    }
}
