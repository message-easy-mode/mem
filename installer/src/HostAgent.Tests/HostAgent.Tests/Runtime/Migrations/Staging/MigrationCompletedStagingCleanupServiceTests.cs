using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Migrations.Staging;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Runtime.Migrations.Staging;

public sealed class MigrationCompletedStagingCleanupServiceTests
{
    [Fact]
    public async Task Completed_cleanup_removes_runtime_but_preserves_verified_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runner = new FakeRunner();
        var service = fixture.CreateService(runner);

        var result = await service.CleanupAsync(
            Fixture.MigrationId,
            fixture.Run.StagingRunId,
            CancellationToken.None);

        Assert.Equal("removed", result.Status);
        Assert.Equal(1, runner.DestroyCount);
        Assert.Equal("verified", fixture.Run.Status);
        Assert.Equal("private-verification-complete-runtime-removed", fixture.Run.CurrentStep);
        Assert.NotNull(fixture.Run.DestroyedAtUtc);
        Assert.Null(fixture.Run.ActiveMigrationKey);
        Assert.Null(fixture.Run.FailureCode);
        Assert.True(fixture.Run.DatabaseImportSucceeded);
        Assert.True(fixture.Run.SynapseHealthPassed);
        Assert.True(fixture.Run.ElementHealthPassed);
    }

    [Fact]
    public async Task Cleanup_failure_keeps_runtime_owned_and_records_retryable_safe_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runner = new FakeRunner
        {
            DestroyBehavior = (_, _) => throw new InvalidOperationException(
                "token=secret-value https://operator:password@example.test/")
        };
        var service = fixture.CreateService(runner);

        var result = await service.CleanupAsync(
            Fixture.MigrationId,
            fixture.Run.StagingRunId,
            CancellationToken.None);

        Assert.Equal("cleanup-required", result.Status);
        Assert.Equal("verified", fixture.Run.Status);
        Assert.Equal("private-verification-complete-cleanup-required", fixture.Run.CurrentStep);
        Assert.Null(fixture.Run.DestroyedAtUtc);
        Assert.Equal(Fixture.MigrationId, fixture.Run.ActiveMigrationKey);
        Assert.Equal("staging_cleanup_failed", fixture.Run.FailureCode);
        Assert.DoesNotContain("secret-value", fixture.Run.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operator:password", fixture.Run.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", fixture.Run.FailureSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cleanup_is_idempotent_after_runtime_was_already_removed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Run.DestroyedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await fixture.Db.SaveChangesAsync();
        var runner = new FakeRunner();
        var service = fixture.CreateService(runner);

        var result = await service.CleanupAsync(
            Fixture.MigrationId,
            fixture.Run.StagingRunId,
            CancellationToken.None);

        Assert.Equal("already-removed", result.Status);
        Assert.Equal(0, runner.DestroyCount);
        Assert.Null(fixture.Run.ActiveMigrationKey);
    }

    [Fact]
    public async Task Cleanup_refuses_non_verified_runtime()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Run.Status = "failed";
        await fixture.Db.SaveChangesAsync();
        var runner = new FakeRunner();
        var service = fixture.CreateService(runner);

        var result = await service.CleanupAsync(
            Fixture.MigrationId,
            fixture.Run.StagingRunId,
            CancellationToken.None);

        Assert.Equal("not-eligible", result.Status);
        Assert.Equal(0, runner.DestroyCount);
        Assert.Null(fixture.Run.DestroyedAtUtc);
        Assert.Equal(Fixture.MigrationId, fixture.Run.ActiveMigrationKey);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string MigrationId = "mig_completed_cleanup";

        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationStagingRunEntity run)
        {
            _connection = connection;
            Db = db;
            Run = run;
        }

        public MemDbContext Db { get; }
        public MigrationStagingRunEntity Run { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = DateTime.UtcNow;
            var intake = new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = MigrationId,
                DisplayName = "Completed cleanup",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LifecycleStatus = "completed"
            };
            var revision = new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = "mpr_cleanup",
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                RevisionNumber = 1,
                Purpose = "preview",
                Status = "package-validated",
                RetentionState = "active",
                CreatedAtUtc = now,
                DecryptedArchiveSha256 = new string('a', 64)
            };
            var attempt = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(),
                ConversionAttemptId = "conv_cleanup",
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                MigrationPackageRevisionEntityId = revision.Id,
                PackageRevision = revision,
                SourcePackageSha256 = revision.DecryptedArchiveSha256,
                SourceAdapterId = "mem-v010",
                SourceAdapterVersion = "0.1.0",
                ConverterId = "worker",
                ConverterVersion = "v2",
                Status = "completed",
                CurrentStep = "candidate-created",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var candidate = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(),
                CandidateArtifactId = "mca_cleanup",
                MigrationConversionAttemptEntityId = attempt.Id,
                ConversionAttempt = attempt,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "v1",
                SourcePackageSha256 = revision.DecryptedArchiveSha256,
                ArtifactSha256 = new string('b', 64),
                ManifestSha256 = new string('c', 64),
                ChecksumsSha256 = new string('d', 64),
                ProvenanceJson = "{}",
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-filesystem",
                ArtifactPath = "/private/candidate.sql",
                CreatedAtUtc = now,
                VerifiedAtUtc = now
            };
            var run = new MigrationStagingRunEntity
            {
                Id = Guid.NewGuid(),
                StagingRunId = "mstg_completed_cleanup",
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                MigrationCandidateArtifactEntityId = candidate.Id,
                CandidateArtifact = candidate,
                ActiveMigrationKey = MigrationId,
                Status = "verified",
                CurrentStep = "private-verification-complete",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                StartedAtUtc = now,
                CompletedAtUtc = now,
                PrivateRuntimeStagingId = "private_cleanup",
                PrivateOnly = true,
                PublicRoutesCreated = false,
                DatabaseImportSucceeded = true,
                SynapseHealthPassed = true,
                ElementConfigPresent = true,
                ElementContainerStarted = true,
                ElementHealthPassed = true,
                ElementSynapseConnectivityPassed = true,
                ElementNetworkAttached = true
            };

            db.AddRange(intake, revision, attempt, candidate, run);
            await db.SaveChangesAsync();
            return new Fixture(connection, db, run);
        }

        public MigrationCompletedStagingCleanupService CreateService(
            IMigrationPrivateStagingRunner runner) =>
            new(
                Db,
                runner,
                TimeProvider.System,
                NullLogger<MigrationCompletedStagingCleanupService>.Instance);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeRunner : IMigrationPrivateStagingRunner
    {
        public Func<string, CancellationToken, Task<PrivateStagingRunResult>> DestroyBehavior { get; set; } =
            (_, _) => Task.FromResult(CreateDestroyedResult());

        public int DestroyCount { get; private set; }

        public Task<PrivateStagingRunResult> CreateAsync(
            MigrationIntakeEntity intake,
            MigrationPackageRevisionEntity packageRevision,
            MigrationCandidateArtifactEntity candidate,
            string stagingRunId,
            string? targetStackSlug,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<PrivateStagingRunResult> DestroyAsync(
            string privateStagingId,
            CancellationToken ct)
        {
            DestroyCount++;
            return DestroyBehavior(privateStagingId, ct);
        }

        private static PrivateStagingRunResult CreateDestroyedResult() => new(
            Source: "control-plane",
            Status: "destroyed",
            Mode: "private-synapse-staging",
            StagingId: "private_cleanup",
            ValidationId: null,
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            UploadedZipPath: null,
            WorkspacePath: "/private/work",
            RuntimePath: "/private/runtime",
            MatrixDataPath: "/private/matrix",
            ElementDataPath: null,
            DatabaseDumpPath: "/private/dump",
            NetworkName: "private-network",
            NetworkId: null,
            PostgresContainerName: "private-postgres",
            PostgresContainerId: null,
            SynapseContainerName: "private-synapse",
            SynapseContainerId: null,
            PostgresImage: "sha256:postgres",
            SynapseImage: "sha256:synapse",
            DatabaseName: "synapse",
            DatabaseUser: "synapse",
            MatrixServerName: "matrix.example.test",
            TargetStackSlug: "staging",
            Safety: new PrivateStagingSafetySummary(
                PrivateOnly: true,
                DockerNetworkInternal: true,
                PublicRoutesCreated: false,
                DnsChanged: false,
                CertificatesChanged: false,
                ProductionContainersTouched: false,
                ProductionDatabasesTouched: false,
                RequiresExplicitDestroy: false,
                Notes: []),
            Database: new PrivateStagingDatabaseSummary(
                ImportSucceeded: true,
                PublicTableCount: 10,
                SynapseKnownTableCount: 8,
                UsersCount: 2,
                EventsCount: 5,
                RoomsCount: 1,
                StateEventsCount: 3),
            Runtime: new PrivateStagingRuntimeSummary(
                HomeserverConfigExtracted: true,
                HomeserverConfigPatched: true,
                SigningKeyExtracted: true,
                MediaStoreExtracted: true,
                MediaFiles: 0,
                MediaBytes: 0,
                ElementConfigExtracted: true,
                PostgresContainerStarted: false,
                SynapseContainerStarted: false,
                SynapseHealthPassed: true,
                HealthResponse: "destroyed",
                SynapseLogsTail: null,
                ElementConfigPatched: true,
                ElementContainerStarted: false,
                ElementHealthPassed: true,
                ElementSynapseConnectivityPassed: true,
                ElementNetworkAttached: false,
                ElementHealthResponse: "destroyed",
                ElementLogsTail: null),
            Checks: [],
            Warnings: [],
            Errors: [],
            Destroy: new PrivateStagingDestroySummary(
                DateTimeOffset.UtcNow,
                SynapseContainerRemoved: true,
                PostgresContainerRemoved: true,
                NetworkRemoved: true,
                WorkspaceRemoved: true,
                Warnings: []),
            Detail: "destroyed",
            SourceKind: PrivateStagingSourceKinds.MigrationCandidate,
            CatalogEntryId: null,
            SynapseApprovedReference: "sha256:synapse-approved",
            ElementContainerName: "private-element",
            ElementContainerId: null,
            ElementImage: "sha256:element",
            ElementApprovedReference: "sha256:element-approved",
            ElementConfigSha256: new string('f', 64));
    }
}
