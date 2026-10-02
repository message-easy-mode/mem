using System.Text.Json;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationProductionAdoptionPersistenceTests
{
    [Fact]
    public async Task Adoption_plan_is_one_to_one_and_creates_no_normal_runtime_records()
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
            IntakeId = "mig_adoption_persistence",
            DisplayName = "Production adoption persistence",
            CreatedAtUtc = now,
        };
        var revision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_final",
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 2,
            Purpose = "final",
            Status = "package-validated",
            RetentionState = "active",
            CreatedAtUtc = now,
            DecryptedArchiveSha256 = new string('1', 64),
        };
        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "conv_final",
            MigrationIntakeEntityId = intake.Id,
            MigrationPackageRevisionEntityId = revision.Id,
            SourcePackageSha256 = revision.DecryptedArchiveSha256,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "1",
            ConverterId = "synapse-port-db",
            ConverterVersion = "1",
            Status = "completed",
            CurrentStep = "completed",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = "mca_final",
            MigrationConversionAttemptEntityId = conversion.Id,
            ArtifactKind = "synapse-postgresql-conversion",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = revision.DecryptedArchiveSha256,
            ArtifactSha256 = new string('2', 64),
            ManifestSha256 = new string('3', 64),
            ChecksumsSha256 = new string('4', 64),
            ProvenanceJson = "{}",
            VerificationStatus = "verified",
            RetentionState = "active",
            StorageKind = "server-owned",
            ArtifactPath = "/server-owned/candidate.dump",
            CreatedAtUtc = now,
        };
        var staging = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = "mstg_final",
            MigrationIntakeEntityId = intake.Id,
            MigrationCandidateArtifactEntityId = candidate.Id,
            Status = "verified",
            CurrentStep = "private-verification-complete",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PrivateOnly = true,
            DatabaseImportSucceeded = true,
            SynapseHealthPassed = true,
            ElementConfigPresent = true,
            ElementContainerStarted = true,
            ElementHealthPassed = true,
            ElementSynapseConnectivityPassed = true,
            ElementNetworkAttached = true,
        };
        var plan = new MigrationProductionAdoptionEntity
        {
            Id = Guid.NewGuid(),
            AdoptionPlanId = "madp_test",
            MigrationIntakeEntityId = intake.Id,
            MigrationPackageRevisionEntityId = revision.Id,
            MigrationCandidateArtifactEntityId = candidate.Id,
            MigrationStagingRunEntityId = staging.Id,
            Status = "prepared",
            RevisionNumber = 1,
            PlanSha256 = new string('5', 64),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PreparedAtUtc = now,
            RuntimeStackId = Guid.NewGuid(),
            TargetStackSlug = "davids-stack",
            TargetDisplayName = "Davids stack",
            MatrixInstanceId = Guid.NewGuid(),
            ElementInstanceId = Guid.NewGuid(),
            MatrixServerName = "matrix-davids.deltabox.dev",
            MatrixPublicHost = "matrix-davids.deltabox.dev",
            MatrixPublicBaseUrl = "https://matrix-davids.deltabox.dev",
            ElementPublicHost = "chat-davids.deltabox.dev",
            ElementPublicBaseUrl = "https://chat-davids.deltabox.dev",
            RuntimeNetworkName = "mem-gateway",
            RuntimeDataRoot = "/server-owned/mem-data",
            ManifestPath = "/server-owned/mem-data/control-plane/runtime-stacks/stack.json",
            MatrixContainerName = "mem-matrix-davids-stack",
            MatrixDataPath = "/server-owned/matrix",
            ElementContainerName = "mem-element-davids-stack",
            ElementDataPath = "/server-owned/element",
            MatrixImageReference = "sha256:synapse-approved",
            MatrixImageId = "sha256:synapse-local",
            ElementImageReference = "sha256:element-approved",
            ElementImageId = "sha256:element-local",
            DatabaseEngine = "postgres",
            DatabaseHost = "mem-postgres",
            DatabasePort = 5432,
            DatabaseName = "mem_davids_stack",
            DatabaseUsername = "mem_davids_stack",
            DatabasePasswordSecretKind = "matrix_postgres_password",
            RoutePlanJson = JsonSerializer.Serialize(Array.Empty<MigrationProductionAdoptionRoutePlan>()),
            ProvenanceJson = "{}",
            CollisionEvidenceJson = "[]",
            MaterializationId = "mpm_test",
            MaterializationStatus = "materializing",
            MaterializationStartedAtUtc = now,
            ProductionDatabaseImported = true,
            MaterializationEvidenceJson = "{}",
            CutoverPreviewId = "mpcv_test",
            CutoverPreviewStatus = "ready",
            CutoverPreviewCreatedAtUtc = now,
            CutoverPreviewExpiresAtUtc = now.AddMinutes(15),
            CutoverPreviewSha256 = new string('6', 64),
            CutoverPreviewJson = "{}",
            CutoverExecutionId = "mpce_test",
            CutoverStatus = "public-awaiting-verification",
            CutoverStartedAtUtc = now,
            CutoverCompletedAtUtc = now.AddMinutes(1),
            TargetPublicAtUtc = now.AddMinutes(1),
            MatrixNpmRouteId = "11",
            ElementNpmRouteId = "12",
            RuntimePromotionCompleted = true,
            PublicRoutesCreated = true,
            CutoverEvidenceJson = "{}",
            CutoverRollbackCheckpointJson = "{}",
            ProductionVerificationId = "mpvf_test",
            ProductionVerificationStatus = "passed",
            ProductionVerificationStartedAtUtc = now.AddMinutes(1),
            ProductionVerificationCompletedAtUtc = now.AddMinutes(2),
            ProductionVerificationValidUntilUtc = now.AddMinutes(32),
            ProductionVerificationCheckCount = 17,
            ProductionVerificationFailedCheckCount = 0,
            ProductionVerificationEvidenceSha256 = new string('a', 64),
            ProductionVerificationEvidenceJson = "{\"checks\":[]}",
            ProductionVerificationReadinessReportId = Guid.NewGuid(),
            RollbackPreviewId = "mprv_test",
            RollbackPreviewStatus = "ready",
            RollbackPreviewCreatedAtUtc = now,
            RollbackPreviewExpiresAtUtc = now.AddMinutes(15),
            RollbackPreviewSha256 = new string('7', 64),
            RollbackPreviewJson = "{}",
            RollbackExecutionId = "mpre_test",
            RollbackStatus = "target-rolled-back-awaiting-source",
            RollbackStartedAtUtc = now.AddMinutes(2),
            RollbackCompletedAtUtc = now.AddMinutes(3),
            RollbackRoutesRestored = true,
            RollbackRuntimeRoutesRemoved = true,
            RollbackTargetContainersStopped = true,
            RollbackSourceHandoffId = "mpsh_test",
            RollbackSourceHandoffSha256 = new string('8', 64),
            RollbackSourceHandoffJson = "{}",
            RollbackEvidenceJson = "{}",
            RollbackCompletionStatus = "coordinated-rollback-complete",
            RollbackSourceCompletionAttemptId = "mm01cb-test",
            RollbackSourceCompletionSha256 = new string('9', 64),
            RollbackSourceCompletionJson = "{}",
            RollbackSourceCompletionImportedAtUtc = now.AddMinutes(4),
            CoordinatedRollbackCompletedAtUtc = now.AddMinutes(4),
            RollbackSourceRestored = true,
            RollbackRestartPoliciesRestored = true,
            RollbackOriginalRunningStatesRestored = true,
            RollbackMatrixVerified = true,
            RollbackElementVerified = true,
            RollbackTargetAuthorityVerified = true,
            RollbackTargetIntegrityVerified = true,
        };
        intake.PackageRevisions.Add(revision);
        intake.ConversionAttempts.Add(conversion);
        intake.StagingRuns.Add(staging);
        intake.ProductionAdoption = plan;
        conversion.CandidateArtifact = candidate;
        db.AddRange(intake, revision, conversion, candidate, staging, plan);
        await db.SaveChangesAsync();

        var loaded = await db.MigrationIntakes.AsNoTracking()
            .Include(x => x.ProductionAdoption)
            .SingleAsync(x => x.IntakeId == intake.IntakeId);

        Assert.Equal("madp_test", loaded.ProductionAdoption!.AdoptionPlanId);
        Assert.Equal("davids-stack", loaded.ProductionAdoption.TargetStackSlug);
        Assert.Equal("mpm_test", loaded.ProductionAdoption.MaterializationId);
        Assert.Equal("materializing", loaded.ProductionAdoption.MaterializationStatus);
        Assert.True(loaded.ProductionAdoption.ProductionDatabaseImported);
        Assert.Equal("mpcv_test", loaded.ProductionAdoption.CutoverPreviewId);
        Assert.Equal("mpce_test", loaded.ProductionAdoption.CutoverExecutionId);
        Assert.True(loaded.ProductionAdoption.RuntimePromotionCompleted);
        Assert.True(loaded.ProductionAdoption.PublicRoutesCreated);
        Assert.Equal("mpvf_test", loaded.ProductionAdoption.ProductionVerificationId);
        Assert.Equal("passed", loaded.ProductionAdoption.ProductionVerificationStatus);
        Assert.Equal(17, loaded.ProductionAdoption.ProductionVerificationCheckCount);
        Assert.Equal(new string('a', 64), loaded.ProductionAdoption.ProductionVerificationEvidenceSha256);
        Assert.NotNull(loaded.ProductionAdoption.ProductionVerificationReadinessReportId);
        Assert.Equal("mprv_test", loaded.ProductionAdoption.RollbackPreviewId);
        Assert.Equal("mpre_test", loaded.ProductionAdoption.RollbackExecutionId);
        Assert.Equal("mpsh_test", loaded.ProductionAdoption.RollbackSourceHandoffId);
        Assert.True(loaded.ProductionAdoption.RollbackRoutesRestored);
        Assert.True(loaded.ProductionAdoption.RollbackRuntimeRoutesRemoved);
        Assert.True(loaded.ProductionAdoption.RollbackTargetContainersStopped);
        Assert.Equal("coordinated-rollback-complete", loaded.ProductionAdoption.RollbackCompletionStatus);
        Assert.Equal("mm01cb-test", loaded.ProductionAdoption.RollbackSourceCompletionAttemptId);
        Assert.True(loaded.ProductionAdoption.RollbackSourceRestored);
        Assert.True(loaded.ProductionAdoption.RollbackTargetIntegrityVerified);
        Assert.Empty(await db.RuntimeStacks.AsNoTracking().ToListAsync());
        Assert.Empty(await db.RuntimeServiceInstances.AsNoTracking().ToListAsync());
        Assert.Empty(await db.RuntimeStackDatabases.AsNoTracking().ToListAsync());
        Assert.Empty(await db.RuntimeRoutes.AsNoTracking().ToListAsync());
    }
}
