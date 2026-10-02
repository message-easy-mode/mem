using System.Text.Json;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationProductionReservationQueryTests
{
    [Fact]
    public async Task Completed_archived_migration_with_live_runtime_still_reserves_identity()
    {
        await using var fixture = await Fixture.CreateAsync(runtimeStatus: "ready");

        var reservations = await MigrationProductionReservationQuery
            .CurrentReservations(fixture.Db)
            .Select(x => x.AdoptionPlanId)
            .ToArrayAsync();

        Assert.Single(reservations);
        Assert.Equal(fixture.Plan.AdoptionPlanId, reservations[0]);
        Assert.Equal(MigrationSessionLifecycleStatuses.Completed, fixture.Intake.LifecycleStatus);
        Assert.NotNull(fixture.Intake.ArchivedAtUtc);
    }

    [Fact]
    public async Task Destroyed_runtime_releases_reservation_without_rewriting_completed_adoption_evidence()
    {
        await using var fixture = await Fixture.CreateAsync(runtimeStatus: "ready");
        var historicalStatus = fixture.Plan.Status;

        fixture.Runtime!.Status = "destroyed";
        fixture.Runtime.LastVerifiedStatus = "destroyed";
        fixture.Runtime.UpdatedAtUtc = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();

        var reservations = await MigrationProductionReservationQuery
            .CurrentReservations(fixture.Db)
            .Select(x => x.AdoptionPlanId)
            .ToArrayAsync();

        Assert.Empty(reservations);

        var retainedPlan = await fixture.Db.MigrationProductionAdoptions
            .AsNoTracking()
            .SingleAsync(x => x.Id == fixture.Plan.Id);
        Assert.Equal(historicalStatus, retainedPlan.Status);
    }

    [Fact]
    public async Task Missing_runtime_row_fails_closed_and_keeps_reservation()
    {
        await using var fixture = await Fixture.CreateAsync(runtimeStatus: null);

        var reservations = await MigrationProductionReservationQuery
            .CurrentReservations(fixture.Db)
            .Select(x => x.AdoptionPlanId)
            .ToArrayAsync();

        Assert.Single(reservations);
        Assert.Equal(fixture.Plan.AdoptionPlanId, reservations[0]);
    }

    [Fact]
    public async Task Superseded_plan_is_not_a_current_reservation_even_when_runtime_is_live()
    {
        await using var fixture = await Fixture.CreateAsync(
            runtimeStatus: "ready",
            adoptionStatus: "superseded");

        var reservations = await MigrationProductionReservationQuery
            .CurrentReservations(fixture.Db)
            .Select(x => x.AdoptionPlanId)
            .ToArrayAsync();

        Assert.Empty(reservations);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationIntakeEntity intake,
            MigrationProductionAdoptionEntity plan,
            RuntimeStackEntity? runtime)
        {
            _connection = connection;
            Db = db;
            Intake = intake;
            Plan = plan;
            Runtime = runtime;
        }

        public MemDbContext Db { get; }
        public MigrationIntakeEntity Intake { get; }
        public MigrationProductionAdoptionEntity Plan { get; }
        public RuntimeStackEntity? Runtime { get; }

        public static async Task<Fixture> CreateAsync(
            string? runtimeStatus,
            string adoptionStatus = "accepted-baseline-backup-created")
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = new DateTime(2026, 8, 1, 9, 30, 0, DateTimeKind.Utc);
            var runtimeStackId = Guid.NewGuid();
            var intake = new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = $"mig_reservation_{Guid.NewGuid():N}",
                DisplayName = "Completed archived migration",
                LifecycleStatus = MigrationSessionLifecycleStatuses.Completed,
                CreatedAtUtc = now.AddHours(-2),
                UpdatedAtUtc = now,
                ClosedAtUtc = now.AddMinutes(-10),
                ClosureKind = "accepted-baseline-created",
                ArchivedAtUtc = now.AddMinutes(-5),
                ArchivedBy = "operator",
            };
            var revision = new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = $"mpr_{Guid.NewGuid():N}",
                MigrationIntakeEntityId = intake.Id,
                RevisionNumber = 1,
                Purpose = "final",
                Status = "package-validated",
                RetentionState = "active",
                CreatedAtUtc = now.AddHours(-1),
                DecryptedArchiveSha256 = new string('1', 64),
            };
            var conversion = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(),
                ConversionAttemptId = $"conv_{Guid.NewGuid():N}",
                MigrationIntakeEntityId = intake.Id,
                MigrationPackageRevisionEntityId = revision.Id,
                SourcePackageSha256 = revision.DecryptedArchiveSha256,
                SourceAdapterId = "mem-v010",
                SourceAdapterVersion = "2",
                ConverterId = "synapse-port-db",
                ConverterVersion = "1",
                Status = "completed",
                CurrentStep = "completed",
                CreatedAtUtc = now.AddMinutes(-50),
                UpdatedAtUtc = now.AddMinutes(-40),
                CompletedAtUtc = now.AddMinutes(-40),
            };
            var candidate = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(),
                CandidateArtifactId = $"mca_{Guid.NewGuid():N}",
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
                CreatedAtUtc = now.AddMinutes(-40),
            };
            var staging = new MigrationStagingRunEntity
            {
                Id = Guid.NewGuid(),
                StagingRunId = $"mstg_{Guid.NewGuid():N}",
                MigrationIntakeEntityId = intake.Id,
                MigrationCandidateArtifactEntityId = candidate.Id,
                Status = "destroyed",
                CurrentStep = "destroyed",
                CreatedAtUtc = now.AddMinutes(-35),
                UpdatedAtUtc = now.AddMinutes(-20),
                DestroyedAtUtc = now.AddMinutes(-20),
                PrivateOnly = true,
                DatabaseImportSucceeded = true,
                SynapseHealthPassed = true,
            };
            var plan = new MigrationProductionAdoptionEntity
            {
                Id = Guid.NewGuid(),
                AdoptionPlanId = $"madp_{Guid.NewGuid():N}",
                MigrationIntakeEntityId = intake.Id,
                MigrationPackageRevisionEntityId = revision.Id,
                MigrationCandidateArtifactEntityId = candidate.Id,
                MigrationStagingRunEntityId = staging.Id,
                Status = adoptionStatus,
                RevisionNumber = 1,
                PlanSha256 = new string('5', 64),
                CreatedAtUtc = now.AddMinutes(-30),
                UpdatedAtUtc = now,
                PreparedAtUtc = now.AddMinutes(-30),
                RuntimeStackId = runtimeStackId,
                TargetStackSlug = "dewars",
                TargetDisplayName = "Dewars",
                MatrixInstanceId = Guid.NewGuid(),
                ElementInstanceId = Guid.NewGuid(),
                MatrixServerName = "matrix40.matrixeasyhost.com",
                MatrixPublicHost = "matrix40.matrixeasyhost.com",
                MatrixPublicBaseUrl = "https://matrix40.matrixeasyhost.com",
                ElementPublicHost = "chat40.matrixeasyhost.com",
                ElementPublicBaseUrl = "https://chat40.matrixeasyhost.com",
                RuntimeNetworkName = "mem-gateway",
                RuntimeDataRoot = "/workspace/mem-data",
                ManifestPath = "/workspace/mem-data/runtime-stack.json",
                MatrixContainerName = "mem-matrix-dewars",
                MatrixDataPath = "/workspace/mem-data/matrix",
                ElementContainerName = "mem-element-dewars",
                ElementDataPath = "/workspace/mem-data/element",
                MatrixImageReference = "sha256:synapse-approved",
                MatrixImageId = "sha256:synapse-local",
                ElementImageReference = "sha256:element-approved",
                ElementImageId = "sha256:element-local",
                DatabaseEngine = "postgres",
                DatabaseHost = "mem-postgres",
                DatabasePort = 5432,
                DatabaseName = "mem_dewars",
                DatabaseUsername = "mem_dewars",
                DatabasePasswordSecretKind = "matrix_postgres_password",
                RoutePlanJson = JsonSerializer.Serialize(Array.Empty<MigrationProductionAdoptionRoutePlan>()),
                ProvenanceJson = "{}",
                CollisionEvidenceJson = "[]",
            };

            RuntimeStackEntity? runtime = null;
            if (runtimeStatus is not null)
            {
                runtime = new RuntimeStackEntity
                {
                    Id = runtimeStackId,
                    Slug = runtimeStatus == "destroyed"
                        ? "dewars--destroyed-20260801093000"
                        : "dewars",
                    DisplayName = "Dewars",
                    Status = runtimeStatus,
                    LastVerifiedStatus = runtimeStatus,
                    CreatedAtUtc = now.AddMinutes(-25),
                    UpdatedAtUtc = now,
                    MatrixInstanceId = plan.MatrixInstanceId,
                    ElementInstanceId = plan.ElementInstanceId,
                    MatrixPublicBaseUrl = plan.MatrixPublicBaseUrl,
                    ElementPublicBaseUrl = plan.ElementPublicBaseUrl,
                };
            }

            intake.PackageRevisions.Add(revision);
            intake.ConversionAttempts.Add(conversion);
            intake.StagingRuns.Add(staging);
            intake.ProductionAdoption = plan;
            conversion.CandidateArtifact = candidate;

            db.AddRange(intake, revision, conversion, candidate, staging, plan);
            if (runtime is not null)
            {
                db.RuntimeStacks.Add(runtime);
            }

            await db.SaveChangesAsync();
            return new Fixture(connection, db, intake, plan, runtime);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
