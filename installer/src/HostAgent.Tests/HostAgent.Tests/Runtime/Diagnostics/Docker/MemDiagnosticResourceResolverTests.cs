using HostAgent.Runtime.Diagnostics.Docker;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics.Docker;

public sealed class MemDiagnosticResourceResolverTests
{
    [Fact]
    public async Task Resolves_a_stack_service_from_persisted_identity_not_container_name_shape()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var stackId = Guid.NewGuid();
        db.RuntimeStacks.Add(new RuntimeStackEntity
        {
            Id = stackId,
            Slug = "family-chat",
            DisplayName = "Family chat",
            Status = "ready",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            MatrixInstanceId = Guid.NewGuid(),
            ServiceInstances =
            {
                new RuntimeServiceInstanceEntity
                {
                    Id = Guid.NewGuid(),
                    RuntimeStackId = stackId,
                    InstanceId = Guid.NewGuid(),
                    ServiceKey = ServiceKeys.Matrix,
                    Status = "running",
                    ContainerId = "persisted-matrix-id",
                    ContainerName = "nonstandard-runtime-name",
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            }
        });
        await db.SaveChangesAsync();
        var resolver = new MemDiagnosticResourceResolver(db, new EmptyStagingReader());
        var incidentId = "inc_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var resource = new MemDiagnosticResource(
            "stack",
            stackId.ToString("D"),
            StackId: stackId.ToString("D"),
            StackSlug: "family-chat",
            Service: "synapse");

        var result = await resolver.ResolveAsync(
            incidentId,
            [Event(incidentId, resource)],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("persisted-matrix-id", result!.ContainerReference);
        Assert.Equal("Family chat Synapse", result.LogicalName);
        Assert.Equal(ServiceKeys.Matrix, result.Resource.Service);
    }

    [Fact]
    public async Task Resolves_only_the_staging_runtime_owned_by_the_exact_migration()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var intake = BuildMigrationGraph(
            "mig-owned",
            "owned-element-id",
            "looks-like-an-unrelated-element-container");
        db.MigrationIntakes.Add(intake);
        await db.SaveChangesAsync();
        var resolver = new MemDiagnosticResourceResolver(db, new EmptyStagingReader());
        var incidentId = "inc_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        var result = await resolver.ResolveAsync(
            incidentId,
            [Event(incidentId, new MemDiagnosticResource(
                "migration",
                "mig-owned",
                Service: "element"))],
            CancellationToken.None);
        var unrelated = await resolver.ResolveAsync(
            incidentId,
            [Event(incidentId, new MemDiagnosticResource(
                "migration",
                "mig-not-owned",
                Service: "element"))],
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("owned-element-id", result!.ContainerReference);
        Assert.Null(unrelated);
    }

    [Fact]
    public async Task Rejects_unknown_resource_kinds_even_when_the_id_looks_like_a_container()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = await CreateDatabaseAsync(connection);
        var resolver = new MemDiagnosticResourceResolver(db, new EmptyStagingReader());
        var incidentId = "inc_cccccccccccccccccccccccccccccccc";

        var result = await resolver.ResolveAsync(
            incidentId,
            [Event(incidentId, new MemDiagnosticResource(
                "unknown",
                "mem-restore-staging-synapse-similar-name",
                Service: "synapse"))],
            CancellationToken.None);

        Assert.Null(result);
    }

    private static async Task<MemDbContext> CreateDatabaseAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static MigrationIntakeEntity BuildMigrationGraph(
        string migrationId,
        string elementContainerId,
        string elementContainerName)
    {
        var now = DateTime.UtcNow;
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = migrationId,
            DisplayName = "Owned migration",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "convert-owned",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            SourcePackageSha256 = new string('a', 64),
            SourceAdapterId = "v010",
            SourceAdapterVersion = "1",
            ConverterId = "test",
            ConverterVersion = "1",
            Status = "completed",
            CurrentStep = "complete",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = now
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = "candidate-owned",
            MigrationConversionAttemptEntityId = conversion.Id,
            ConversionAttempt = conversion,
            ArtifactKind = "postgresql-custom",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = new string('a', 64),
            ArtifactSha256 = new string('b', 64),
            ManifestSha256 = new string('c', 64),
            ChecksumsSha256 = new string('d', 64),
            ProvenanceJson = "{}",
            VerificationStatus = "verified",
            RetentionState = "retained",
            StorageKind = "file",
            ArtifactPath = "/tmp/candidate",
            CreatedAtUtc = now,
            VerifiedAtUtc = now
        };
        conversion.CandidateArtifact = candidate;
        intake.ConversionAttempts.Add(conversion);
        intake.StagingRuns.Add(new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = "stage-owned",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            ActiveMigrationKey = migrationId,
            Status = "failed",
            CurrentStep = "element-readiness",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            PrivateRuntimeStagingId = "restore-stage-owned",
            PrivateOnly = true,
            PublicRoutesCreated = false,
            ElementContainerId = elementContainerId,
            ElementContainerName = elementContainerName,
            ElementContainerStarted = true
        });
        return intake;
    }

    private static MemDiagnosticEvent Event(
        string incidentId,
        MemDiagnosticResource resource) =>
        new(
            1,
            $"evt_{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            MemDiagnosticSeverities.Error,
            "migration.staging.failed",
            "host-agent",
            "migrations",
            "private-staging",
            "Staging failed.",
            incidentId,
            null,
            null,
            null,
            null,
            null,
            resource,
            null,
            null,
            null,
            null,
            null,
            false,
            true,
            false);

    private sealed class EmptyStagingReader : IMemPrivateStagingEvidenceReader
    {
        public Task<HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging.PrivateStagingRunResult?> ReadAsync(
            string stagingId,
            CancellationToken cancellationToken) =>
            Task.FromResult<HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging.PrivateStagingRunResult?>(null);
    }
}
