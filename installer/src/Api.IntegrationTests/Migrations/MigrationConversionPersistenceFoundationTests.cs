using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationConversionPersistenceFoundationTests
{
    [Fact]
    public async Task Persists_conversion_attempt_and_candidate_without_catalog_or_restore_relationships()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateIntake();
        var attempt = CreateAttempt(intake, "conv_test_001", intake.IntakeId);
        var candidate = CreateCandidate(attempt, "mca_test_001");

        fixture.Db.AddRange(intake, attempt, candidate);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var persisted = await fixture.Db.MigrationConversionAttempts
            .Include(x => x.MigrationIntake)
            .Include(x => x.CandidateArtifact)
            .SingleAsync(x => x.ConversionAttemptId == "conv_test_001");

        Assert.Equal(intake.IntakeId, persisted.MigrationIntake.IntakeId);
        Assert.NotNull(persisted.CandidateArtifact);
        Assert.Equal("mca_test_001", persisted.CandidateArtifact.CandidateArtifactId);
        Assert.Equal("mem-migration-candidate", persisted.CandidateArtifact.ArtifactKind);

        var attemptEntityType = fixture.Db.Model.FindEntityType(typeof(MigrationConversionAttemptEntity));
        var candidateEntityType = fixture.Db.Model.FindEntityType(typeof(MigrationCandidateArtifactEntity));

        Assert.NotNull(attemptEntityType);
        Assert.NotNull(candidateEntityType);

        var forbiddenPrincipalTypes = new[]
        {
            typeof(BackupCatalogEntryEntity),
            typeof(RestoreAttemptEntity),
        };

        Assert.DoesNotContain(
            attemptEntityType.GetForeignKeys(),
            foreignKey => forbiddenPrincipalTypes.Contains(foreignKey.PrincipalEntityType.ClrType));

        Assert.DoesNotContain(
            candidateEntityType.GetForeignKeys(),
            foreignKey => forbiddenPrincipalTypes.Contains(foreignKey.PrincipalEntityType.ClrType));
    }

    [Fact]
    public async Task Enforces_one_active_conversion_attempt_per_migration()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateIntake();

        fixture.Db.Add(intake);
        fixture.Db.Add(CreateAttempt(intake, "conv_test_active_001", intake.IntakeId));
        await fixture.Db.SaveChangesAsync();

        fixture.Db.Add(CreateAttempt(intake, "conv_test_active_002", intake.IntakeId));

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Retains_terminal_attempt_history_and_enforces_one_candidate_per_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = CreateIntake();
        var first = CreateAttempt(intake, "conv_test_history_001", activeMigrationKey: null);
        first.Status = "failed";
        first.CurrentStep = "failed";
        first.CompletedAtUtc = DateTime.UtcNow;
        first.FailureCode = "conversion_failed";
        first.FailureSummary = "A retained test failure.";

        var retry = CreateAttempt(intake, "conv_test_history_002", intake.IntakeId);
        retry.RetryOfConversionAttempt = first;

        fixture.Db.AddRange(intake, first, retry);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.Add(CreateCandidate(retry, "mca_test_history_001"));
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var duplicateCandidate = CreateCandidate(retry, "mca_test_history_002");
        duplicateCandidate.ConversionAttempt = null!;
        fixture.Db.Add(duplicateCandidate);
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());

        fixture.Db.ChangeTracker.Clear();

        var attempts = await fixture.Db.MigrationConversionAttempts
            .Where(x => x.MigrationIntakeEntityId == intake.Id)
            .ToListAsync();

        Assert.Equal(2, attempts.Count);
        var persistedFirst = Assert.Single(attempts, x => x.Id == first.Id);
        var persistedRetry = Assert.Single(attempts, x => x.Id == retry.Id);
        Assert.Equal(first.Id, persistedRetry.RetryOfConversionAttemptEntityId);
        Assert.Equal("conversion_failed", persistedFirst.FailureCode);
    }

    private static MigrationIntakeEntity CreateIntake()
    {
        var now = DateTime.UtcNow;
        return new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = $"mig_test_{Guid.NewGuid():N}",
            DisplayName = "Conversion foundation test",
            CreatedAtUtc = now,
        };
    }

    private static MigrationConversionAttemptEntity CreateAttempt(
        MigrationIntakeEntity intake,
        string attemptId,
        string? activeMigrationKey)
    {
        var now = DateTime.UtcNow;
        return new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = attemptId,
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            ActiveMigrationKey = activeMigrationKey,
            SourcePackageSha256 = new string('a', 64),
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "0.2.0",
            ConverterId = "mem-migrate",
            ConverterVersion = "0.2.0-alpha.1",
            Status = "pending",
            CurrentStep = "queued",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static MigrationCandidateArtifactEntity CreateCandidate(
        MigrationConversionAttemptEntity attempt,
        string candidateId)
    {
        return new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = candidateId,
            ConversionAttempt = attempt,
            MigrationConversionAttemptEntityId = attempt.Id,
            ArtifactKind = "mem-migration-candidate",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = attempt.SourcePackageSha256,
            ArtifactSha256 = new string('c', 64),
            ManifestSha256 = new string('d', 64),
            ChecksumsSha256 = new string('e', 64),
            ProvenanceJson = "{\"sourceAdapter\":\"mem-v010\"}",
            VerificationStatus = "verified",
            RetentionState = "active",
            StorageKind = "private-migration-artifact",
            ArtifactPath = "/private/migrations/candidates/test.memcandidate.zip",
            VerificationReportPath = "/private/migrations/candidates/verification.json",
            CreatedAtUtc = DateTime.UtcNow,
            VerifiedAtUtc = DateTime.UtcNow,
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
