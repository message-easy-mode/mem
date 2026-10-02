using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationBaselineBackupPersistenceTests
{
    [Fact]
    public async Task Baseline_backup_handoff_is_one_to_one_with_acceptance_and_session()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>().UseSqlite(connection).Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(), IntakeId = "mig_baseline_persistence",
            DisplayName = "Baseline persistence", CreatedAtUtc = DateTime.UtcNow,
        };
        var acceptance = new MigrationAcceptanceEntity
        {
            Id = Guid.NewGuid(), AcceptanceId = "macc_baseline", MigrationIntakeEntityId = intake.Id,
            ExecutionId = "execution_baseline", CandidateArtifactId = "candidate_baseline", StagingRunId = "staging_baseline",
            PublicVerificationStatus = "passed", PublicVerificationEvidenceJson = "{\"checks\":[]}",
            PublicVerificationEvidenceSha256 = new string('1', 64), PublicCutoverAtUtc = DateTime.UtcNow,
            AcceptedAtUtc = DateTime.UtcNow, AcceptedBy = "owner", FreshPublicVerificationAcknowledged = true,
            TargetWriteDivergenceAcknowledged = true, RollbackBoundaryAcknowledged = true,
            LegacyRetentionAcknowledged = true, NoAutomaticLegacyDeletionAcknowledged = true,
        };
        var handoff = new MigrationBaselineBackupHandoffEntity
        {
            Id = Guid.NewGuid(), HandoffId = "mbh_test", MigrationIntakeEntityId = intake.Id,
            MigrationAcceptanceEntityId = acceptance.Id, Status = "failed", AttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            TargetStackSlug = "migrated-stack", CandidateId = "candidate-1", PrivateRuntimeId = "runtime-1",
            FailureCode = "baseline_backup_failed", FailureSummary = "Catalog registration failed.",
        };
        intake.Acceptance = acceptance;
        intake.BaselineBackupHandoff = handoff;
        acceptance.BaselineBackupHandoff = handoff;
        db.AddRange(intake, acceptance, handoff);
        await db.SaveChangesAsync();

        var loaded = await db.MigrationIntakes.AsNoTracking()
            .Include(x => x.Acceptance)!.ThenInclude(x => x!.BaselineBackupHandoff)
            .Include(x => x.BaselineBackupHandoff)
            .SingleAsync(x => x.IntakeId == intake.IntakeId);

        Assert.Equal("mbh_test", loaded.BaselineBackupHandoff!.HandoffId);
        Assert.Equal("failed", loaded.Acceptance!.BaselineBackupHandoff!.Status);
        Assert.Equal("baseline_backup_failed", loaded.BaselineBackupHandoff.FailureCode);
    }
}
