using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Migrations.Staging;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HostAgent.Tests.Runtime.Migrations.Staging;

public sealed class MigrationStagingOrchestratorTests
{
    [Fact]
    public async Task Verified_candidate_creates_one_retained_migration_owned_runtime_without_catalog_or_restore_records()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runner = new FakeRunner();
        var service = fixture.CreateService(runner);

        var result = await service.StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            CancellationToken.None);

        Assert.Equal("verified", result.Status);
        Assert.True(result.PrivateOnly);
        Assert.False(result.PublicRoutesCreated);
        Assert.True(result.RetirementReviewAvailable);
        Assert.Null(result.RetryOfStagingRunId);
        Assert.True(result.ElementContainerStarted);
        Assert.True(result.ElementHealthPassed);
        Assert.True(result.ElementSynapseConnectivityPassed);
        Assert.True(result.ElementNetworkAttached);
        Assert.Equal("mpr_preview", runner.LastPackageRevisionId);

        var persisted = Assert.Single(await fixture.Db.MigrationStagingRuns.ToListAsync());
        Assert.Equal(Fixture.MigrationId, persisted.ActiveMigrationKey);
        Assert.True(persisted.ElementContainerStarted);
        Assert.True(persisted.ElementHealthPassed);
        Assert.True(persisted.ElementSynapseConnectivityPassed);
        Assert.True(persisted.ElementNetworkAttached);
        Assert.Equal(new string('f', 64), persisted.ElementConfigSha256);
        Assert.Equal("sha256:element-approved", persisted.ElementImageReference);
        Assert.Equal("sha256:synapse-approved", persisted.SynapseImageReference);
        Assert.Empty(await fixture.Db.BackupCatalogEntries.ToListAsync());
        Assert.Empty(await fixture.Db.RestoreAttempts.ToListAsync());

        var exception = await Assert.ThrowsAsync<MigrationStagingException>(() =>
            service.StartAsync(
                Fixture.MigrationId,
                new StartMigrationStagingRequest(),
                CancellationToken.None));

        Assert.Equal("staging_already_active", exception.Code);
        Assert.Single(await fixture.Db.MigrationStagingRuns.ToListAsync());
    }

    [Fact]
    public async Task MIGRATION_STAGING_REQUEST_LIFETIME_CORR_01_request_abort_after_durable_acceptance_does_not_cancel_private_staging()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var request = new CancellationTokenSource();
        var runner = new FakeRunner
        {
            CreateBehavior = (_, _, _, _, _, operationToken) =>
            {
                request.Cancel();
                Assert.False(operationToken.IsCancellationRequested);
                return Task.FromResult(CreateResult());
            }
        };
        var service = fixture.CreateService(runner);

        var result = await service.StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            request.Token);

        Assert.True(request.IsCancellationRequested);
        Assert.Equal("verified", result.Status);
        Assert.True(result.PrivateOnly);
        Assert.False(result.PublicRoutesCreated);

        var persisted = Assert.Single(await fixture.Db.MigrationStagingRuns.ToListAsync());
        Assert.Equal("verified", persisted.Status);
        Assert.Equal(Fixture.MigrationId, persisted.ActiveMigrationKey);
        Assert.Null(persisted.FailureCode);
    }

    [Fact]
    public async Task A_recorded_retirement_allows_a_fresh_private_test()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runner = new FakeRunner();
        var service = fixture.CreateService(runner);

        var first = await service.StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            CancellationToken.None);

        // Retirement execution is covered by MigrationStagingRetirementTests.
        // This test proves only the creation boundary after a recorded retirement.
        var firstEntity = await fixture.Db.MigrationStagingRuns
            .SingleAsync(x => x.StagingRunId == first.StagingRunId);
        firstEntity.Status = "destroyed";
        firstEntity.ActiveMigrationKey = null;
        firstEntity.DestroyedAtUtc = DateTime.UtcNow;
        await fixture.Db.SaveChangesAsync();

        Assert.Null(firstEntity.ActiveMigrationKey);
        Assert.NotNull(firstEntity.DestroyedAtUtc);

        var second = await service.StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            CancellationToken.None);

        Assert.Equal("verified", second.Status);
        Assert.NotEqual(first.StagingRunId, second.StagingRunId);
        Assert.Equal(2, await fixture.Db.MigrationStagingRuns.CountAsync());
    }

    [Fact]
    public async Task Eligible_failed_run_can_be_retried_with_durable_ancestry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var retrySource = await fixture.AddHistoricalRunAsync(
            status: "failed",
            activeMigrationKey: null,
            privateRuntimeStagingId: null);
        var service = fixture.CreateService(new FakeRunner());

        var result = await service.StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(
                RetryOfStagingRunId: retrySource.StagingRunId),
            CancellationToken.None);

        Assert.Equal(retrySource.StagingRunId, result.RetryOfStagingRunId);

        var persisted = await fixture.Db.MigrationStagingRuns
            .Include(x => x.RetryOfStagingRun)
            .SingleAsync(x => x.StagingRunId == result.StagingRunId);
        Assert.Equal(retrySource.Id, persisted.RetryOfStagingRunEntityId);
        Assert.Equal(retrySource.StagingRunId, persisted.RetryOfStagingRun!.StagingRunId);
    }


    [Fact]
    public async Task Allows_preview_candidate_while_final_revision_is_awaiting_package()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddAwaitingFinalRevisionAsync();

        var runner = new FakeRunner();
        var result = await fixture.CreateService(runner).StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            CancellationToken.None);

        Assert.Equal("verified", result.Status);
        Assert.Equal("mpr_preview", runner.LastPackageRevisionId);
        Assert.True(result.PrivateOnly);
        Assert.False(result.PublicRoutesCreated);
    }

    [Fact]
    public async Task Rejects_preview_candidate_after_final_package_becomes_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.PromoteFinalRevisionAsync();

        var exception = await Assert.ThrowsAsync<MigrationStagingException>(() =>
            fixture.CreateService(new FakeRunner()).StartAsync(
                Fixture.MigrationId,
                new StartMigrationStagingRequest(),
                CancellationToken.None));

        Assert.Equal("final_candidate_not_ready", exception.Code);
        Assert.Empty(await fixture.Db.MigrationStagingRuns.ToListAsync());
    }


    [Fact]
    public async Task Uses_verified_candidate_from_authoritative_final_revision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.PromoteFinalRevisionAsync();
        var finalCandidate = await fixture.AddFinalCandidateAsync();
        var runner = new FakeRunner();

        var result = await fixture.CreateService(runner).StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            CancellationToken.None);

        Assert.Equal("verified", result.Status);
        Assert.Equal("mpr_final", runner.LastPackageRevisionId);
        Assert.Equal(finalCandidate.CandidateArtifactId, result.CandidateArtifactId);
    }

    [Fact]
    public async Task Retry_rejects_unknown_or_ineligible_staging_runs()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CreateService(new FakeRunner());

        var missing = await Assert.ThrowsAsync<MigrationStagingException>(() =>
            service.StartAsync(
                Fixture.MigrationId,
                new StartMigrationStagingRequest(
                    RetryOfStagingRunId: "mstg_missing"),
                CancellationToken.None));
        Assert.Equal("staging_retry_source_not_found", missing.Code);

        var verified = await fixture.AddHistoricalRunAsync(
            status: "verified",
            activeMigrationKey: null,
            privateRuntimeStagingId: "private_historical");

        var ineligible = await Assert.ThrowsAsync<MigrationStagingException>(() =>
            service.StartAsync(
                Fixture.MigrationId,
                new StartMigrationStagingRequest(
                    RetryOfStagingRunId: verified.StagingRunId),
                CancellationToken.None));
        Assert.Equal("staging_retry_not_allowed", ineligible.Code);
    }

    [Fact]
    public async Task Failure_before_a_runtime_identity_is_returned_releases_the_active_key_and_redacts_evidence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runner = new FakeRunner
        {
            CreateBehavior = (_, _, _, _, _, _) => throw new InvalidOperationException(
                "password=topsecret https://operator:credential@example.test/\u0001")
        };
        var service = fixture.CreateService(runner);

        var exception = await Assert.ThrowsAsync<MigrationStagingException>(() =>
            service.StartAsync(
                Fixture.MigrationId,
                new StartMigrationStagingRequest(),
                CancellationToken.None));

        Assert.Equal("staging_failed", exception.Code);

        var failed = Assert.Single(await fixture.Db.MigrationStagingRuns.ToListAsync());
        Assert.Equal("failed", failed.Status);
        Assert.Null(failed.ActiveMigrationKey);
        Assert.Null(failed.PrivateRuntimeStagingId);
        Assert.DoesNotContain("topsecret", failed.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("operator:credential", failed.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\u0001', failed.FailureSummary!);
        Assert.Contains("[redacted]", failed.FailureSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_retained_runtime_blocks_another_creation_and_exposes_retirement_review()
    {
        await using var fixture = await Fixture.CreateAsync();
        var runner = new FakeRunner
        {
            CreateBehavior = (_, _, _, _, _, _) => Task.FromResult(CreateResult(
                status: "failed",
                stagingId: "private_failed_retained",
                detail: "token=do-not-show https://user:pass@example.test/",
                requiresExplicitDestroy: true))
        };
        var service = fixture.CreateService(runner);

        var failed = await service.StartAsync(
            Fixture.MigrationId,
            new StartMigrationStagingRequest(),
            CancellationToken.None);

        Assert.Equal("failed", failed.Status);
        Assert.True(failed.RetirementReviewAvailable);
        Assert.DoesNotContain("do-not-show", failed.FailureSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user:pass", failed.FailureSummary, StringComparison.OrdinalIgnoreCase);

        var entity = await fixture.Db.MigrationStagingRuns
            .SingleAsync(x => x.StagingRunId == failed.StagingRunId);
        Assert.Equal(Fixture.MigrationId, entity.ActiveMigrationKey);

        var blocked = await Assert.ThrowsAsync<MigrationStagingException>(() =>
            service.StartAsync(
                Fixture.MigrationId,
                new StartMigrationStagingRequest(),
                CancellationToken.None));
        Assert.Equal("staging_already_active", blocked.Code);

        Assert.True(failed.RetirementReviewAvailable);
        Assert.Null(entity.DestroyedAtUtc);
    }

    private static PrivateStagingRunResult CreateResult(
        string status = "ready",
        string stagingId = "private_1",
        string? detail = "ready",
        bool requiresExplicitDestroy = true,
        bool destroyed = false) => new(
        Source: "control-plane",
        Status: destroyed ? "destroyed" : status,
        Mode: "private-synapse-staging",
        StagingId: stagingId,
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
            RequiresExplicitDestroy: requiresExplicitDestroy,
            Notes: []),
        Database: new PrivateStagingDatabaseSummary(
            ImportSucceeded: status == "ready",
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
            PostgresContainerStarted: true,
            SynapseContainerStarted: true,
            SynapseHealthPassed: status == "ready",
            HealthResponse: "ok",
            SynapseLogsTail: null,
            ElementConfigPatched: true,
            ElementContainerStarted: status == "ready",
            ElementHealthPassed: status == "ready",
            ElementSynapseConnectivityPassed: status == "ready",
            ElementNetworkAttached: status == "ready",
            ElementHealthResponse: "ok",
            ElementLogsTail: null),
        Checks: [],
        Warnings: [],
        Errors: status == "ready" ? [] : ["failed"],
        Destroy: destroyed
            ? new PrivateStagingDestroySummary(
                DateTimeOffset.UtcNow,
                SynapseContainerRemoved: true,
                PostgresContainerRemoved: true,
                NetworkRemoved: true,
                WorkspaceRemoved: true,
                Warnings: [])
            : null,
        Detail: detail,
        SourceKind: PrivateStagingSourceKinds.MigrationCandidate,
        CatalogEntryId: null,
        SynapseApprovedReference: "sha256:synapse-approved",
        ElementContainerName: "private-element",
        ElementContainerId: "element-id",
        ElementImage: "sha256:element",
        ElementApprovedReference: "sha256:element-approved",
        ElementConfigSha256: new string('f', 64));

    private sealed class FakeRunner : IMigrationPrivateStagingRunner
    {
        public Func<
            MigrationIntakeEntity,
            MigrationPackageRevisionEntity,
            MigrationCandidateArtifactEntity,
            string,
            string?,
            CancellationToken,
            Task<PrivateStagingRunResult>> CreateBehavior { get; set; } =
            (_, _, _, _, _, _) => Task.FromResult(CreateResult());

        public int DestroyCount { get; private set; }
        public string? LastPackageRevisionId { get; private set; }

        public Task<PrivateStagingRunResult> CreateAsync(
            MigrationIntakeEntity intake,
            MigrationPackageRevisionEntity packageRevision,
            MigrationCandidateArtifactEntity candidate,
            string runId,
            string? slug,
            CancellationToken cancellationToken)
        {
            LastPackageRevisionId = packageRevision.PackageRevisionId;
            return CreateBehavior(
                intake,
                packageRevision,
                candidate,
                runId,
                slug,
                cancellationToken);
        }

        public Task<PrivateStagingRunResult> DestroyAsync(
            string id,
            CancellationToken cancellationToken)
        {
            DestroyCount++;
            return Task.FromResult(CreateResult(
                status: "destroyed",
                stagingId: id,
                detail: "destroyed",
                requiresExplicitDestroy: false,
                destroyed: true));
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string MigrationId = "mig_test";

        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationIntakeEntity intake,
            MigrationCandidateArtifactEntity candidate)
        {
            _connection = connection;
            Db = db;
            Intake = intake;
            Candidate = candidate;
        }

        public MemDbContext Db { get; }
        public MigrationIntakeEntity Intake { get; }
        public MigrationCandidateArtifactEntity Candidate { get; }

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
                DisplayName = "Test",
                CreatedAtUtc = now,
            };
            var previewRevision = new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = "mpr_preview",
                MigrationIntake = intake,
                MigrationIntakeEntityId = intake.Id,
                RevisionNumber = 1,
                Purpose = "preview",
                Status = "package-validated",
                RetentionState = "active",
                ActivePurposeKey = $"{MigrationId}:preview",
                CreatedAtUtc = now,
                UploadedAtUtc = now,
                ValidatedAtUtc = now,
                DecryptedArchiveSha256 = new string('b', 64),
                CaptureKind = "preview",
                SourceFrozen = false,
                RehearsalOnly = true
            };
            var attempt = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(),
                ConversionAttemptId = "conv_test",
                MigrationIntake = intake,
                MigrationIntakeEntityId = intake.Id,
                MigrationPackageRevisionEntityId = previewRevision.Id,
                PackageRevision = previewRevision,
                SourcePackageSha256 = new string('b', 64),
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
                CandidateArtifactId = "mca_test",
                ConversionAttempt = attempt,
                MigrationConversionAttemptEntityId = attempt.Id,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "v1",
                SourcePackageSha256 = new string('b', 64),
                ArtifactSha256 = new string('c', 64),
                ManifestSha256 = new string('d', 64),
                ChecksumsSha256 = new string('e', 64),
                ProvenanceJson = "{}",
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-filesystem",
                ArtifactPath = "/private/dump",
                CreatedAtUtc = now,
                VerifiedAtUtc = now
            };

            db.AddRange(intake, previewRevision, attempt, candidate);
            await db.SaveChangesAsync();
            return new Fixture(connection, db, intake, candidate);
        }


        public async Task AddAwaitingFinalRevisionAsync()
        {
            var now = DateTime.UtcNow;
            Db.MigrationPackageRevisions.Add(new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = "mpr_final_awaiting",
                MigrationIntake = Intake,
                MigrationIntakeEntityId = Intake.Id,
                RevisionNumber = 2,
                Purpose = "final",
                Status = "awaiting-package",
                RetentionState = "active",
                ActivePurposeKey = $"{MigrationId}:final",
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddHours(1),
                AgeRecipient = "age1awaiting",
                RecipientFingerprint = "0000-0000-0000-0000",
                ProtectedAgeIdentity = "protected"
            });
            await Db.SaveChangesAsync();
        }

        public async Task PromoteFinalRevisionAsync()
        {
            var now = DateTime.UtcNow;
            var hash = new string('f', 64);
            Db.MigrationPackageRevisions.Add(new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = "mpr_final",
                MigrationIntake = Intake,
                MigrationIntakeEntityId = Intake.Id,
                RevisionNumber = 2,
                Purpose = "final",
                Status = "package-validated",
                RetentionState = "active",
                ActivePurposeKey = $"{MigrationId}:final",
                CreatedAtUtc = now,
                UploadedAtUtc = now,
                ValidatedAtUtc = now,
                DecryptedArchiveSha256 = hash,
                CaptureKind = "final",
                SourceFrozen = true,
                RehearsalOnly = false
            });
            await Db.SaveChangesAsync();
        }


        public async Task<MigrationCandidateArtifactEntity> AddFinalCandidateAsync()
        {
            var now = DateTime.UtcNow;
            var finalRevision = await Db.MigrationPackageRevisions
                .SingleAsync(x => x.PackageRevisionId == "mpr_final");
            var attempt = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(),
                ConversionAttemptId = "conv_final",
                MigrationIntake = Intake,
                MigrationIntakeEntityId = Intake.Id,
                MigrationPackageRevisionEntityId = finalRevision.Id,
                PackageRevision = finalRevision,
                SourcePackageSha256 = finalRevision.DecryptedArchiveSha256!,
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
                CandidateArtifactId = "mca_final",
                ConversionAttempt = attempt,
                MigrationConversionAttemptEntityId = attempt.Id,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "v1",
                SourcePackageSha256 = finalRevision.DecryptedArchiveSha256!,
                ArtifactSha256 = new string('1', 64),
                ManifestSha256 = new string('2', 64),
                ChecksumsSha256 = new string('3', 64),
                ProvenanceJson = "{}",
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-filesystem",
                ArtifactPath = "/private/final-dump",
                CreatedAtUtc = now,
                VerifiedAtUtc = now
            };

            Db.AddRange(attempt, candidate);
            await Db.SaveChangesAsync();
            return candidate;
        }

        public MigrationStagingOrchestrator CreateService(IMigrationPrivateStagingRunner runner)
        {
            var operationLifetime = new MigrationStagingOperationLifetime(
                new FakeHostApplicationLifetime(),
                NullLogger<MigrationStagingOperationLifetime>.Instance,
                TimeSpan.FromMinutes(1));
            return new MigrationStagingOrchestrator(
                Db,
                runner,
                operationLifetime,
                TimeProvider.System);
        }

        public async Task<MigrationStagingRunEntity> AddHistoricalRunAsync(
            string status,
            string? activeMigrationKey,
            string? privateRuntimeStagingId)
        {
            var now = DateTime.UtcNow.AddMinutes(-5);
            var run = new MigrationStagingRunEntity
            {
                Id = Guid.NewGuid(),
                StagingRunId = $"mstg_history_{Guid.NewGuid():N}"[..45],
                MigrationIntakeEntityId = Intake.Id,
                MigrationIntake = Intake,
                MigrationCandidateArtifactEntityId = Candidate.Id,
                CandidateArtifact = Candidate,
                ActiveMigrationKey = activeMigrationKey,
                Status = status,
                CurrentStep = status,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                StartedAtUtc = now,
                CompletedAtUtc = now,
                PrivateRuntimeStagingId = privateRuntimeStagingId,
                PrivateOnly = true,
                PublicRoutesCreated = false
            };
            Db.MigrationStagingRuns.Add(run);
            await Db.SaveChangesAsync();
            return run;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
