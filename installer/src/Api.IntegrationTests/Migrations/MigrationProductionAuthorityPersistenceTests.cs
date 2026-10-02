using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationProductionAuthorityPersistenceTests
{
    [Fact]
    public async Task Authority_round_trips_exact_artifact_and_operator_evidence_bindings()
    {
        await using var fixture = await Fixture.CreateAsync();
        var authority = fixture.CreateAuthority(
            "mpa_operator_attested",
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            MigrationProductionAuthorityStatuses.Active,
            fixture.Intake.IntakeId);

        fixture.Intake.ProductionAuthorities.Add(authority);
        fixture.Db.MigrationProductionAuthorities.Add(authority);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var loaded = await fixture.Db.MigrationProductionAuthorities
            .AsNoTracking()
            .Include(x => x.MigrationIntake)
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .Include(x => x.StagingRun)
            .SingleAsync(x => x.ProductionAuthorityId == authority.ProductionAuthorityId);

        Assert.Equal(MigrationProductionAuthorityTypes.OperatorAttestedSnapshot, loaded.AuthorityType);
        Assert.Equal(MigrationProductionAuthorityStatuses.Active, loaded.Status);
        Assert.Equal(fixture.Intake.IntakeId, loaded.ActiveMigrationKey);
        Assert.Equal(fixture.Revision.PackageRevisionId, loaded.PackageRevision.PackageRevisionId);
        Assert.Equal(fixture.Candidate.CandidateArtifactId, loaded.CandidateArtifact.CandidateArtifactId);
        Assert.Equal(fixture.Staging.StagingRunId, loaded.StagingRun.StagingRunId);
        Assert.Equal(fixture.Revision.EncryptedPackageSha256, loaded.EncryptedPackageSha256);
        Assert.Equal(fixture.Revision.DecryptedArchiveSha256, loaded.DecryptedArchiveSha256);
        Assert.Equal(fixture.Source.SourceFingerprint, loaded.SourceFingerprint);
        Assert.Equal("matrix.example.test", loaded.MatrixServerName);
        Assert.Equal(new string('7', 64), loaded.SigningKeyIdentitySha256);
        Assert.False(loaded.SourceFrozen);
        Assert.True(loaded.RehearsalOnly);
        Assert.Equal("operator-attested-snapshot-v1", loaded.AcknowledgementsSchemaVersion);
        Assert.Equal(new string('9', 64), loaded.AcknowledgementsSha256);
        Assert.Equal(fixture.OperatorId, loaded.CreatedByOperatorId);
    }

    [Fact]
    public async Task Only_one_active_authority_is_allowed_per_migration_while_history_is_retained()
    {
        await using var fixture = await Fixture.CreateAsync();
        var historical = fixture.CreateAuthority(
            "mpa_superseded",
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            MigrationProductionAuthorityStatuses.Superseded,
            activeMigrationKey: null);
        historical.SupersededAtUtc = fixture.Now.AddMinutes(1);
        historical.SupersededByOperatorId = fixture.OperatorId;
        historical.SupersessionReason = "Replaced before cutover by a newer authority.";

        var active = fixture.CreateAuthority(
            "mpa_final_frozen",
            MigrationProductionAuthorityTypes.FinalFrozen,
            MigrationProductionAuthorityStatuses.Active,
            fixture.Intake.IntakeId);

        fixture.Db.MigrationProductionAuthorities.AddRange(historical, active);
        await fixture.Db.SaveChangesAsync();

        var conflicting = fixture.CreateAuthority(
            "mpa_conflicting",
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            MigrationProductionAuthorityStatuses.Active,
            fixture.Intake.IntakeId);
        fixture.Db.MigrationProductionAuthorities.Add(conflicting);

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
        Assert.Contains("UNIQUE", error.InnerException?.Message ?? error.Message, StringComparison.OrdinalIgnoreCase);

        fixture.Db.ChangeTracker.Clear();
        var retained = await fixture.Db.MigrationProductionAuthorities
            .AsNoTracking()
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        Assert.Equal(2, retained.Count);
        Assert.Contains(retained, x =>
            x.Status == MigrationProductionAuthorityStatuses.Superseded &&
            x.ActiveMigrationKey == null);
        Assert.Contains(retained, x =>
            x.Status == MigrationProductionAuthorityStatuses.Active &&
            x.ActiveMigrationKey == fixture.Intake.IntakeId);
    }

    [Fact]
    public async Task Referenced_migration_artifacts_cannot_be_deleted_while_authority_exists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var authority = fixture.CreateAuthority(
            "mpa_restrict",
            MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            MigrationProductionAuthorityStatuses.Active,
            fixture.Intake.IntakeId);
        fixture.Db.MigrationProductionAuthorities.Add(authority);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var revision = await fixture.Db.MigrationPackageRevisions
            .SingleAsync(x => x.Id == fixture.Revision.Id);
        fixture.Db.MigrationPackageRevisions.Remove(revision);

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Db.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            DateTime now,
            Guid operatorId,
            MigrationIntakeEntity intake,
            MigrationSourceEntity source,
            MigrationPackageRevisionEntity revision,
            MigrationCandidateArtifactEntity candidate,
            MigrationStagingRunEntity staging)
        {
            _connection = connection;
            Db = db;
            Now = now;
            OperatorId = operatorId;
            Intake = intake;
            Source = source;
            Revision = revision;
            Candidate = candidate;
            Staging = staging;
        }

        public MemDbContext Db { get; }
        public DateTime Now { get; }
        public Guid OperatorId { get; }
        public MigrationIntakeEntity Intake { get; }
        public MigrationSourceEntity Source { get; }
        public MigrationPackageRevisionEntity Revision { get; }
        public MigrationCandidateArtifactEntity Candidate { get; }
        public MigrationStagingRunEntity Staging { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var now = new DateTime(2026, 7, 21, 9, 30, 0, DateTimeKind.Utc);
            var operatorId = Guid.NewGuid();
            var intake = new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = "mig_authority_persistence",
                DisplayName = "Production authority persistence",
                CreatedAtUtc = now,
            };
            var source = new MigrationSourceEntity
            {
                Id = Guid.NewGuid(),
                MigrationIntakeEntityId = intake.Id,
                SourceId = "source-1",
                SourceKind = "mem-v010",
                Product = "Message Easy Mode",
                ProductVersion = "0.1.0",
                SourceFingerprint = new string('1', 64),
                CapturedAtUtc = now.AddMinutes(-10),
            };
            var revision = new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = "mpr_preview",
                MigrationIntakeEntityId = intake.Id,
                RevisionNumber = 1,
                Purpose = "preview",
                Status = "package-validated",
                RetentionState = "active",
                CreatedAtUtc = now,
                ValidatedAtUtc = now,
                EncryptedPackageSha256 = new string('2', 64),
                DecryptedArchiveSha256 = new string('3', 64),
                ArchiveMigrationId = "source-capture-1",
                CaptureKind = "preview",
                SourceFrozen = false,
                RehearsalOnly = true,
            };
            var conversion = new MigrationConversionAttemptEntity
            {
                Id = Guid.NewGuid(),
                ConversionAttemptId = "conv_authority",
                MigrationIntakeEntityId = intake.Id,
                MigrationPackageRevisionEntityId = revision.Id,
                SourcePackageSha256 = revision.DecryptedArchiveSha256,
                SourceAdapterId = "mem-v010",
                SourceAdapterVersion = "1",
                ConverterId = "synapse-port-db",
                ConverterVersion = "1",
                Status = "completed-with-warnings",
                CurrentStep = "candidate-created",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
            };
            var candidate = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(),
                CandidateArtifactId = "mca_authority",
                MigrationConversionAttemptEntityId = conversion.Id,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "1",
                SourcePackageSha256 = revision.DecryptedArchiveSha256,
                ArtifactSha256 = new string('4', 64),
                ManifestSha256 = new string('5', 64),
                ChecksumsSha256 = new string('6', 64),
                ProvenanceJson = "{}",
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-owned",
                ArtifactPath = "/server-owned/candidate",
                CreatedAtUtc = now,
                VerifiedAtUtc = now,
            };
            var staging = new MigrationStagingRunEntity
            {
                Id = Guid.NewGuid(),
                StagingRunId = "mstg_authority",
                MigrationIntakeEntityId = intake.Id,
                MigrationCandidateArtifactEntityId = candidate.Id,
                ActiveMigrationKey = intake.IntakeId,
                Status = "verified",
                CurrentStep = "private-verification-complete",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                PrivateOnly = true,
                PublicRoutesCreated = false,
                DatabaseImportSucceeded = true,
                SynapseHealthPassed = true,
                ElementConfigPresent = true,
                ElementContainerStarted = true,
                ElementHealthPassed = true,
                ElementSynapseConnectivityPassed = true,
                ElementNetworkAttached = true,
                MatrixServerName = "matrix.example.test",
                UsersCount = 3,
                RoomsCount = 2,
                EventsCount = 28,
            };

            intake.Sources.Add(source);
            intake.PackageRevisions.Add(revision);
            intake.ConversionAttempts.Add(conversion);
            intake.StagingRuns.Add(staging);
            conversion.CandidateArtifact = candidate;
            db.AddRange(intake, source, revision, conversion, candidate, staging);
            await db.SaveChangesAsync();

            return new Fixture(
                connection,
                db,
                now,
                operatorId,
                intake,
                source,
                revision,
                candidate,
                staging);
        }

        public MigrationProductionAuthorityEntity CreateAuthority(
            string authorityId,
            string authorityType,
            string status,
            string? activeMigrationKey)
        {
            return new MigrationProductionAuthorityEntity
            {
                Id = Guid.NewGuid(),
                ProductionAuthorityId = authorityId,
                MigrationIntakeEntityId = Intake.Id,
                MigrationPackageRevisionEntityId = Revision.Id,
                MigrationCandidateArtifactEntityId = Candidate.Id,
                MigrationStagingRunEntityId = Staging.Id,
                AuthorityType = authorityType,
                Status = status,
                ActiveMigrationKey = activeMigrationKey,
                EncryptedPackageSha256 = Revision.EncryptedPackageSha256!,
                DecryptedArchiveSha256 = Revision.DecryptedArchiveSha256!,
                SourceMigrationId = Revision.ArchiveMigrationId!,
                SourceFingerprint = Source.SourceFingerprint,
                MatrixServerName = Staging.MatrixServerName!,
                SigningKeyIdentitySha256 = new string('7', 64),
                CaptureKind = Revision.CaptureKind!,
                SourceFrozen = Revision.SourceFrozen!.Value,
                RehearsalOnly = Revision.RehearsalOnly!.Value,
                CapturedAtUtc = Source.CapturedAtUtc,
                EvidenceSchemaVersion = "migration-production-authority-v1",
                EvidenceJson = "{\"authority\":\"bound\"}",
                EvidenceSha256 = new string('8', 64),
                AcknowledgementsSchemaVersion = "operator-attested-snapshot-v1",
                AcknowledgementsJson = "{\"accepted\":true}",
                AcknowledgementsSha256 = new string('9', 64),
                CreatedByOperatorId = OperatorId,
                CreatedAtUtc = Now,
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
