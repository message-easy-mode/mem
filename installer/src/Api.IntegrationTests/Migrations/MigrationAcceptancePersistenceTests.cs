using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationAcceptancePersistenceTests
{
    [Fact]
    public async Task Acceptance_and_retention_are_one_to_one_with_migration_session()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(), IntakeId = "mig_acceptance_persistence",
            DisplayName = "Acceptance persistence", CreatedAtUtc = DateTime.UtcNow,
        };
        var acceptance = new MigrationAcceptanceEntity
        {
            Id = Guid.NewGuid(), AcceptanceId = "macc_test", MigrationIntakeEntityId = intake.Id,
            ExecutionId = "execution_test", CandidateArtifactId = "candidate_test", StagingRunId = "staging_test",
            PublicVerificationStatus = "passed", PublicVerificationEvidenceJson = "{\"checks\":[]}",
            PublicVerificationEvidenceSha256 = new string('1', 64), PublicCutoverAtUtc = DateTime.UtcNow,
            AcceptedAtUtc = DateTime.UtcNow, AcceptedBy = "owner", FreshPublicVerificationAcknowledged = true,
            TargetWriteDivergenceAcknowledged = true, RollbackBoundaryAcknowledged = true,
            LegacyRetentionAcknowledged = true, NoAutomaticLegacyDeletionAcknowledged = true,
        };
        var retention = new LegacyRetentionRecordEntity
        {
            Id = Guid.NewGuid(), RetentionRecordId = "mlr_test", MigrationIntakeEntityId = intake.Id,
            MigrationAcceptanceEntityId = acceptance.Id, Status = "active", CreatedAtUtc = DateTime.UtcNow,
            RetainUntilUtc = DateTime.UtcNow.AddDays(14), CleanupEligibleAtUtc = DateTime.UtcNow.AddDays(14),
            SourceMigrationId = "source_test", SourceProduct = "Message Easy Mode", SourceVersion = "0.1.0",
            SourcePackageRetained = true, CandidateArtifactRetained = true, PrivateStagingEvidenceRetained = true,
            LegacySourceResourcesRetained = true, AutomaticDeletionAllowed = false, Summary = "Retained",
        };
        acceptance.LegacyRetentionRecord = retention;
        intake.Acceptance = acceptance;
        intake.LegacyRetentionRecord = retention;
        db.AddRange(intake, acceptance, retention);
        await db.SaveChangesAsync();

        var loaded = await db.MigrationIntakes.AsNoTracking()
            .Include(x => x.Acceptance)!.ThenInclude(x => x!.LegacyRetentionRecord)
            .SingleAsync(x => x.IntakeId == intake.IntakeId);
        Assert.Equal("macc_test", loaded.Acceptance!.AcceptanceId);
        Assert.Equal("mlr_test", loaded.Acceptance.LegacyRetentionRecord.RetentionRecordId);
        Assert.False(loaded.Acceptance.LegacyRetentionRecord.AutomaticDeletionAllowed);
    }
}
