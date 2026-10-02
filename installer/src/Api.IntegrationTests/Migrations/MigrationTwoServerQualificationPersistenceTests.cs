using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationTwoServerQualificationPersistenceTests
{
    [Fact]
    public async Task Qualification_is_one_to_one_with_migration_intake_and_retains_hash_bound_evidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_two_server_qualification",
            DisplayName = "Two-server qualification",
            CreatedAtUtc = now,
        };
        var qualification = new MigrationTwoServerQualificationEntity
        {
            Id = Guid.NewGuid(),
            QualificationId = "mtq_test",
            MigrationIntakeEntityId = intake.Id,
            Status = "qualified",
            ImportedAtUtc = now,
            QualifiedAtUtc = now,
            SourceEvidenceAttemptId = "mm01e-source-test",
            SourceEvidenceSha256 = new string('1', 64),
            SourceEvidenceJson = "{}",
            QualificationEvidenceSha256 = new string('2', 64),
            QualificationEvidenceJson = "{}",
            SourceMigrationId = "source-migration",
            PackageRevisionId = "mpr_final",
            EncryptedPackageSha256 = new string('3', 64),
            SourceFingerprint = new string('4', 64),
            SourceStackSlug = "legacy-stack",
            MatrixServerName = "matrix.example.test",
            FreezeAttemptId = "freeze-test",
            FreezePlanId = "plan-test",
            FreezePlanSha256 = new string('5', 64),
            AdoptionPlanId = "madp_test",
            RuntimeStackId = Guid.NewGuid(),
            ProductionVerificationId = "mpvf_test",
            AcceptanceId = "macc_test",
            BaselineBackupHandoffId = "mbh_test",
            BaselineCatalogEntryId = "bkp_test",
            SourceMachineIdSha256 = new string('6', 64),
            SourceDockerEngineIdSha256 = new string('7', 64),
            TargetMachineIdSha256 = new string('8', 64),
            TargetDockerEngineIdSha256 = new string('9', 64),
            DistinctMachineIdentity = true,
            DistinctDockerEngineIdentity = true,
            DevelopmentExternalControlPlane = false,
            ClosureId = "mtqc_test",
            ClosureStatus = "closed",
            ClosedAtUtc = now,
            ClosureEvidenceSha256 = new string('a', 64),
            ClosureEvidenceJson = "{}",
            ClosureNote = "Qualification complete.",
        };
        intake.TwoServerQualification = qualification;
        db.AddRange(intake, qualification);
        await db.SaveChangesAsync();

        var loaded = await db.MigrationIntakes.AsNoTracking()
            .Include(x => x.TwoServerQualification)
            .SingleAsync(x => x.IntakeId == intake.IntakeId);

        Assert.Equal("mtq_test", loaded.TwoServerQualification!.QualificationId);
        Assert.Equal("qualified", loaded.TwoServerQualification.Status);
        Assert.True(loaded.TwoServerQualification.DistinctMachineIdentity);
        Assert.True(loaded.TwoServerQualification.DistinctDockerEngineIdentity);
        Assert.Equal(new string('1', 64), loaded.TwoServerQualification.SourceEvidenceSha256);
        Assert.Equal(new string('2', 64), loaded.TwoServerQualification.QualificationEvidenceSha256);
        Assert.Equal("mtqc_test", loaded.TwoServerQualification.ClosureId);
        Assert.Equal("closed", loaded.TwoServerQualification.ClosureStatus);
        Assert.Equal(new string('a', 64), loaded.TwoServerQualification.ClosureEvidenceSha256);
    }
}
