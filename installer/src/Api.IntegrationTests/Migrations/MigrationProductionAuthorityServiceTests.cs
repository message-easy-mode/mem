using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Assurance;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Services.Identity;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationProductionAuthorityServiceTests
{
    [Fact]
    public async Task Verified_preview_chain_creates_exact_operator_attested_authority()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.CreateOperatorAttestedSnapshotAsync(
            fixture.Intake.IntakeId,
            fixture.OperatorId,
            CompleteRequest(),
            CancellationToken.None);

        Assert.Equal("active", result.Status);
        Assert.True(result.AuthorizesProduction);
        var authority = Assert.IsType<MigrationProductionAuthorityDto>(result.Authority);
        Assert.Equal(MigrationProductionAuthorityTypes.OperatorAttestedSnapshot, authority.AuthorityType);
        Assert.Equal(fixture.PreviewRevision.PackageRevisionId, authority.PackageRevisionId);
        Assert.Equal(fixture.Candidate.CandidateArtifactId, authority.CandidateArtifactId);
        Assert.Equal(fixture.Staging.StagingRunId, authority.StagingRunId);
        Assert.Equal("preview", authority.CaptureKind);
        Assert.False(authority.SourceFrozen);
        Assert.True(authority.RehearsalOnly);
        Assert.Equal(3L, authority.UsersCount);
        Assert.Equal(2L, authority.RoomsCount);
        Assert.Equal(28L, authority.EventsCount);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.MigrationProductionAuthorities
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(fixture.Intake.IntakeId, persisted.ActiveMigrationKey);
        Assert.Equal(MigrationProductionAuthorityStatuses.Active, persisted.Status);
        Assert.Contains("\"formalSourceFreezeEvidenceCollected\":false", persisted.EvidenceJson);
        Assert.Contains("\"finalRecapturePerformed\":false", persisted.EvidenceJson);
        Assert.Contains("\"potentialPostCaptureWritesIndependentlyExcluded\":false", persisted.EvidenceJson);
        Assert.Contains("\"rollbackAssurance\":\"reduced\"", persisted.EvidenceJson);
        Assert.Equal(HashText(persisted.EvidenceJson), persisted.EvidenceSha256);
        Assert.Equal(HashText(persisted.AcknowledgementsJson), persisted.AcknowledgementsSha256);

        var audit = Assert.Single(fixture.Audit.Events);
        Assert.Equal(
            "migration.production_authority.operator_attested_snapshot.created",
            audit.EventType);
        Assert.Equal(fixture.OperatorId, audit.ActorOperatorId);
        Assert.Equal(fixture.Intake.IntakeId, audit.CorrelationId);
    }

    [Fact]
    public async Task Secure_intake_without_source_identity_backfills_from_validated_archive()
    {
        await using var fixture = await Fixture.CreateAsync(includeSource: false);

        var result = await fixture.Service.CreateOperatorAttestedSnapshotAsync(
            fixture.Intake.IntakeId,
            fixture.OperatorId,
            CompleteRequest(),
            CancellationToken.None);

        var authority = Assert.IsType<MigrationProductionAuthorityDto>(result.Authority);
        Assert.Equal(Fixture.SourceFingerprint, authority.SourceFingerprint);
        Assert.Equal(Fixture.CaptureCompletedAtUtc, authority.CapturedAtUtc);

        fixture.Db.ChangeTracker.Clear();
        var source = await fixture.Db.MigrationSources.AsNoTracking().SingleAsync();
        Assert.Equal(Fixture.SourceMigrationId, source.SourceId);
        Assert.Equal("mem-v010-capture", source.SourceKind);
        Assert.Equal("MatrixEasyMode", source.Product);
        Assert.Equal("0.1.0", source.ProductVersion);
        Assert.Equal(Fixture.SourceFingerprint, source.SourceFingerprint);
        Assert.Equal(Fixture.CaptureCompletedAtUtc, source.CapturedAtUtc);
    }

    [Fact]
    public async Task Conflicting_durable_source_identity_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var source = await fixture.Db.MigrationSources.SingleAsync();
        source.SourceFingerprint = new string('f', 64);
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.CreateOperatorAttestedSnapshotAsync(
                fixture.Intake.IntakeId,
                fixture.OperatorId,
                CompleteRequest(),
                CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Db.MigrationProductionAuthorities);
    }

    [Fact]
    public async Task Repeated_creation_is_idempotent_and_retains_one_active_authority()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.CreateOperatorAttestedSnapshotAsync(
            fixture.Intake.IntakeId,
            fixture.OperatorId,
            CompleteRequest(),
            CancellationToken.None);
        var second = await fixture.Service.CreateOperatorAttestedSnapshotAsync(
            fixture.Intake.IntakeId,
            fixture.OperatorId,
            CompleteRequest(),
            CancellationToken.None);

        Assert.Equal(
            first.Authority!.ProductionAuthorityId,
            second.Authority!.ProductionAuthorityId);
        Assert.Equal(1, await fixture.Db.MigrationProductionAuthorities.CountAsync());
        Assert.Single(fixture.Audit.Events);
    }

    [Fact]
    public async Task Incomplete_operator_attestation_is_rejected_before_persistence()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = CompleteRequest() with
        {
            AcknowledgePostCaptureWritesWillNotMigrate = false,
        };

        var exception = await Assert.ThrowsAsync<MigrationProductionAuthorityException>(() =>
            fixture.Service.CreateOperatorAttestedSnapshotAsync(
                fixture.Intake.IntakeId,
                fixture.OperatorId,
                request,
                CancellationToken.None));

        Assert.Equal("operator_attestation_incomplete", exception.Code);
        Assert.Equal(MigrationProductionAuthorityFailureKind.InvalidRequest, exception.Kind);
        Assert.Empty(fixture.Db.MigrationProductionAuthorities);
        Assert.Empty(fixture.Audit.Events);
    }

    [Fact]
    public async Task Candidate_payload_tampering_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        await File.AppendAllTextAsync(fixture.Candidate.ArtifactPath, "tampered");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Service.CreateOperatorAttestedSnapshotAsync(
                fixture.Intake.IntakeId,
                fixture.OperatorId,
                CompleteRequest(),
                CancellationToken.None));

        Assert.Contains("candidate checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Db.MigrationProductionAuthorities);
    }

    [Fact]
    public async Task Destroyed_or_unretained_staging_cannot_authorize_production()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Staging.DestroyedAtUtc = fixture.Now;
        fixture.Staging.ActiveMigrationKey = null;
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MigrationProductionAuthorityException>(() =>
            fixture.Service.CreateOperatorAttestedSnapshotAsync(
                fixture.Intake.IntakeId,
                fixture.OperatorId,
                CompleteRequest(),
                CancellationToken.None));

        Assert.Equal("verified_staging_required", exception.Code);
        Assert.Empty(fixture.Db.MigrationProductionAuthorities);
    }

    private static CreateOperatorAttestedSnapshotAuthorityRequest CompleteRequest() => new(
        AcknowledgeUsersWereInstructedNotToUseSource: true,
        AcknowledgePostCaptureWritesWillNotMigrate: true,
        AcknowledgeSelectedSnapshotBecomesAuthoritative: true,
        AcknowledgeSourceWillBeRetainedUntilVerification: true,
        AcknowledgeNoFormalSourceFreezeEvidence: true,
        AcknowledgeReducedRollbackAssurance: true);

    private static string HashText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed class Fixture : IAsyncDisposable
    {
        public const string SourceMigrationId = "source-capture-service";
        public const string SourceFingerprint =
            "76c31508f9a852b6a2bb85fabed3c6354d06c9ff7085a75dd72f29588287b1f8";
        public static readonly DateTime CaptureStartedAtUtc =
            new(2026, 7, 21, 9, 30, 0, DateTimeKind.Utc);
        public static readonly DateTime CaptureCompletedAtUtc =
            new(2026, 7, 21, 9, 32, 0, DateTimeKind.Utc);
        private readonly SqliteConnection _connection;
        private readonly string _dataRoot;

        private Fixture(
            SqliteConnection connection,
            string dataRoot,
            MemDbContext db,
            MigrationProductionAuthorityService service,
            RecordingAuditService audit,
            DateTime now,
            Guid operatorId,
            MigrationIntakeEntity intake,
            MigrationPackageRevisionEntity previewRevision,
            MigrationCandidateArtifactEntity candidate,
            MigrationStagingRunEntity staging)
        {
            _connection = connection;
            _dataRoot = dataRoot;
            Db = db;
            Service = service;
            Audit = audit;
            Now = now;
            OperatorId = operatorId;
            Intake = intake;
            PreviewRevision = previewRevision;
            Candidate = candidate;
            Staging = staging;
        }

        public MemDbContext Db { get; }
        public MigrationProductionAuthorityService Service { get; }
        public RecordingAuditService Audit { get; }
        public DateTime Now { get; }
        public Guid OperatorId { get; }
        public MigrationIntakeEntity Intake { get; }
        public MigrationPackageRevisionEntity PreviewRevision { get; }
        public MigrationCandidateArtifactEntity Candidate { get; }
        public MigrationStagingRunEntity Staging { get; }

        public static async Task<Fixture> CreateAsync(bool includeSource = true)
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-authority-service-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dataRoot);

            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = new DateTime(2026, 7, 21, 10, 15, 0, DateTimeKind.Utc);
            var timeProvider = new FixedTimeProvider(now);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot,
                })
                .Build();
            var audit = new RecordingAuditService();
            var service = new MigrationProductionAuthorityService(
                db,
                configuration,
                timeProvider,
                audit);

            var migrationId = "mig_operator_attested_service";
            var packageRevisionId = "mpr_preview_service";
            var sourceMigrationId = SourceMigrationId;
            var sourceStackId = Guid.NewGuid();
            const string matrixServerName = "matrix.example.test";

            var revisionRoot = MigrationPackageRevisionStorage.ResolveRevisionRoot(
                dataRoot,
                migrationId,
                packageRevisionId);
            Directory.CreateDirectory(revisionRoot);

            var encryptedPath = MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
                dataRoot,
                migrationId,
                packageRevisionId);
            await File.WriteAllBytesAsync(
                encryptedPath,
                Encoding.UTF8.GetBytes("encrypted-package-service"));
            var encryptedSha256 = await HashFileAsync(encryptedPath);

            var archivePath = MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                dataRoot,
                migrationId,
                packageRevisionId);
            await CreateArchiveAsync(
                archivePath,
                sourceMigrationId,
                sourceStackId,
                matrixServerName);
            var archiveSha256 = await HashFileAsync(archivePath);

            var candidatePath = Path.Combine(dataRoot, "migration-candidates", "candidate.sql");
            Directory.CreateDirectory(Path.GetDirectoryName(candidatePath)!);
            await File.WriteAllTextAsync(candidatePath, "verified postgres dump");
            var candidateSha256 = await HashFileAsync(candidatePath);

            var privateStagingId = "private-staging-service";
            var matrixDataPath = Path.Combine(
                dataRoot,
                "restore-staging",
                "workspaces",
                privateStagingId,
                "matrix-data");
            Directory.CreateDirectory(matrixDataPath);
            await File.WriteAllTextAsync(
                Path.Combine(matrixDataPath, "signing.key"),
                "ed25519 a_test private-key-material");

            var historyRoot = Path.Combine(
                dataRoot,
                "restore-staging",
                "history",
                privateStagingId);
            Directory.CreateDirectory(historyRoot);
            await File.WriteAllTextAsync(
                Path.Combine(historyRoot, "restore-staging-result.json"),
                JsonSerializer.Serialize(new
                {
                    status = "ready",
                    stagingId = privateStagingId,
                    matrixDataPath,
                    matrixServerName,
                    safety = new
                    {
                        privateOnly = true,
                        publicRoutesCreated = false,
                        productionContainersTouched = false,
                        productionDatabasesTouched = false,
                        requiresExplicitDestroy = true,
                    },
                    database = new
                    {
                        importSucceeded = true,
                        usersCount = 3L,
                        roomsCount = 2L,
                        eventsCount = 28L,
                    },
                    runtime = new
                    {
                        signingKeyExtracted = true,
                        synapseHealthPassed = true,
                        elementConfigExtracted = true,
                        elementContainerStarted = true,
                        elementHealthPassed = true,
                        elementSynapseConnectivityPassed = true,
                        elementNetworkAttached = true,
                    },
                    destroy = (object?)null,
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            var intake = new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = migrationId,
                DisplayName = "Operator attested service fixture",
                CreatedAtUtc = now.AddHours(-1),
            };
            var source = new MigrationSourceEntity
            {
                Id = Guid.NewGuid(),
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                SourceId = SourceMigrationId,
                SourceKind = "mem-v010-capture",
                Product = "MatrixEasyMode",
                ProductVersion = "0.1.0",
                SourceFingerprint = SourceFingerprint,
                CapturedAtUtc = CaptureCompletedAtUtc,
            };
            var previewRevision = new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = packageRevisionId,
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                RevisionNumber = 1,
                Purpose = "preview",
                Status = "package-validated",
                RetentionState = "active",
                ActivePurposeKey = $"{migrationId}:preview",
                CreatedAtUtc = now.AddMinutes(-40),
                UploadedAtUtc = now.AddMinutes(-39),
                ValidatedAtUtc = now.AddMinutes(-38),
                EncryptedPackageSha256 = encryptedSha256,
                DecryptedArchiveSha256 = archiveSha256,
                ArchiveMigrationId = sourceMigrationId,
                ArchiveSourceProduct = "MatrixEasyMode",
                ArchiveSourceVersion = "0.1.0",
                ArchiveStackCount = 1,
                CaptureKind = "preview",
                SourceFrozen = false,
                RehearsalOnly = true,
            };
            var finalRecipient = new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = "mpr_final_awaiting_service",
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                RevisionNumber = 2,
                Purpose = "final",
                Status = "awaiting-package",
                RetentionState = "active",
                ActivePurposeKey = $"{migrationId}:final",
                CreatedAtUtc = now.AddMinutes(-5),
            };
            var conversion = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(),
                ConversionAttemptId = "conv_operator_attested_service",
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                MigrationPackageRevisionEntityId = previewRevision.Id,
                PackageRevision = previewRevision,
                SourcePackageSha256 = archiveSha256,
                SourceAdapterId = "mem-v010",
                SourceAdapterVersion = "1",
                ConverterId = "synapse-port-db",
                ConverterVersion = "1",
                Status = "completed-with-warnings",
                CurrentStep = "candidate-created",
                CreatedAtUtc = now.AddMinutes(-30),
                UpdatedAtUtc = now.AddMinutes(-20),
                CompletedAtUtc = now.AddMinutes(-20),
            };
            var candidate = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(),
                CandidateArtifactId = "mca_operator_attested_service",
                MigrationConversionAttemptEntityId = conversion.Id,
                ConversionAttempt = conversion,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "mem-conversion-output/v1",
                SourcePackageSha256 = archiveSha256,
                ArtifactSha256 = candidateSha256,
                ManifestSha256 = new string('2', 64),
                ChecksumsSha256 = new string('3', 64),
                ProvenanceJson = JsonSerializer.Serialize(new
                {
                    MigrationId = sourceMigrationId,
                    SourceStackId = sourceStackId,
                    MatrixServerName = matrixServerName,
                    PackageRevisionId = packageRevisionId,
                    PackagePurpose = "preview",
                    PackageRevisionNumber = 1,
                }),
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-filesystem",
                ArtifactPath = candidatePath,
                CreatedAtUtc = now.AddMinutes(-20),
                VerifiedAtUtc = now.AddMinutes(-20),
            };
            conversion.CandidateArtifact = candidate;

            var staging = new MigrationStagingRunEntity
            {
                Id = Guid.NewGuid(),
                StagingRunId = "mstg_operator_attested_service",
                MigrationIntakeEntityId = intake.Id,
                MigrationIntake = intake,
                MigrationCandidateArtifactEntityId = candidate.Id,
                CandidateArtifact = candidate,
                ActiveMigrationKey = migrationId,
                Status = "verified",
                CurrentStep = "private-verification-complete",
                CreatedAtUtc = now.AddMinutes(-15),
                UpdatedAtUtc = now.AddMinutes(-10),
                CompletedAtUtc = now.AddMinutes(-10),
                PrivateRuntimeStagingId = privateStagingId,
                WorkspacePath = Path.GetDirectoryName(matrixDataPath),
                PrivateOnly = true,
                PublicRoutesCreated = false,
                DatabaseImportSucceeded = true,
                SynapseHealthPassed = true,
                ElementConfigPresent = true,
                ElementContainerStarted = true,
                ElementHealthPassed = true,
                ElementSynapseConnectivityPassed = true,
                ElementNetworkAttached = true,
                MatrixServerName = matrixServerName,
                UsersCount = 3,
                RoomsCount = 2,
                EventsCount = 28,
            };

            if (includeSource)
            {
                intake.Sources.Add(source);
            }
            intake.PackageRevisions.Add(previewRevision);
            intake.PackageRevisions.Add(finalRecipient);
            intake.ConversionAttempts.Add(conversion);
            intake.StagingRuns.Add(staging);
            previewRevision.ConversionAttempts.Add(conversion);

            db.AddRange(
                intake,
                previewRevision,
                finalRecipient,
                conversion,
                candidate,
                staging);
            if (includeSource)
            {
                db.Add(source);
            }
            await db.SaveChangesAsync();

            return new Fixture(
                connection,
                dataRoot,
                db,
                service,
                audit,
                now,
                Guid.NewGuid(),
                intake,
                previewRevision,
                candidate,
                staging);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            try
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for test-only temporary material.
            }
        }

        private static async Task CreateArchiveAsync(
            string path,
            string migrationId,
            Guid sourceStackId,
            string matrixServerName)
        {
            await using var output = File.Create(path);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false);
            var entry = archive.CreateEntry(
                "mem-migration/migration-manifest.json",
                CompressionLevel.NoCompression);
            await using var entryStream = entry.Open();
            await JsonSerializer.SerializeAsync(entryStream, new
            {
                Schema = "mem-v010-migration",
                SchemaVersion = 2,
                MigrationId = migrationId,
                Source = new
                {
                    Product = "MatrixEasyMode",
                    Version = "0.1.0",
                    StartFingerprint = SourceFingerprint,
                    CompletionFingerprint = SourceFingerprint,
                },
                Capture = new
                {
                    Kind = "preview",
                    SourceFrozen = false,
                    RehearsalOnly = true,
                    SourceChangedDuringCapture = false,
                    StartedAtUtc = CaptureStartedAtUtc,
                    CompletedAtUtc = CaptureCompletedAtUtc,
                },
                Stacks = new[]
                {
                    new
                    {
                        SourceStackId = sourceStackId,
                        Slug = "tester",
                        DisplayName = "Tester",
                        MatrixServerName = matrixServerName,
                        MatrixPublicUrl = $"https://{matrixServerName}",
                        ElementPublicUrl = "https://element.example.test",
                    },
                },
            });
        }

        private static async Task<string> HashFileAsync(string path)
        {
            await using var stream = File.OpenRead(path);
            var digest = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(digest).ToLowerInvariant();
        }
    }

    public sealed class RecordingAuditService : IMemOperatorAuditService
    {
        public List<MemOperatorAuditEventWrite> Events { get; } = [];

        public Task WriteAsync(
            MemOperatorAuditEventWrite auditEvent,
            CancellationToken ct = default)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        private readonly DateTimeOffset _utcNow = new(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
