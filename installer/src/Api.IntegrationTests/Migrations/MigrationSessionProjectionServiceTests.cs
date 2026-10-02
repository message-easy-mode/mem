using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionProjectionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Expired_secure_intake_detail_is_projected_without_mutating_persisted_state_or_exposing_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var createdAtUtc = Now.AddHours(-25).UtcDateTime;
        var expiresAtUtc = Now.AddHours(-1).UtcDateTime;

        var expiredIntake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_expired_projection",
            DisplayName = "Expired secure intake",
            CreatedAtUtc = createdAtUtc,
        };
        expiredIntake.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_expired_projection",
            MigrationIntakeEntityId = expiredIntake.Id,
            MigrationIntake = expiredIntake,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = $"{expiredIntake.IntakeId}:preview",
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            AgeRecipient = "age1publicrecipient",
            ProtectedAgeIdentity = "AGE-SECRET-KEY-DO-NOT-EXPOSE",
            RecipientFingerprint = "ABCD-EF01-2345-6789",
        });
        fixture.Db.MigrationIntakes.Add(expiredIntake);
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(
            "mig_expired_projection",
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("expired", detail!.Session.Status);
        Assert.Equal("package-transfer", detail.Session.Phase);
        Assert.Equal("create-replacement-intake", detail.Session.NextAction);
        Assert.Equal("pending", detail.Session.SourceAdapter);
        Assert.Equal("Package not yet received", detail.Session.SourceDisplay);
        Assert.True(detail.Session.NeedsAttention);
        Assert.Equal(expiresAtUtc, detail.Session.UpdatedAtUtc);
        Assert.NotNull(detail.Package);
        Assert.Equal("encrypted", detail.Package!.TransferMode);
        Assert.Equal("expired", detail.Package.Status);
        Assert.Equal("age1publicrecipient", detail.Package.AgeRecipient);

        var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("AGE-SECRET-KEY-DO-NOT-EXPOSE", json, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedAgeIdentity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("manifestJson", json, StringComparison.OrdinalIgnoreCase);

        var persisted = await fixture.Db.MigrationIntakes
            .AsNoTracking()
            .Include(x => x.PackageRevisions)
            .SingleAsync();
        var persistedRevision = Assert.Single(persisted.PackageRevisions);
        Assert.Equal("awaiting-package", persistedRevision.Status);
        Assert.Equal("AGE-SECRET-KEY-DO-NOT-EXPOSE", persistedRevision.ProtectedAgeIdentity);
    }

    [Fact]
    public async Task Detail_projects_canonical_package_without_retired_findings_or_historical_links()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = Now.AddMinutes(-20).UtcDateTime;

        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_package_projection",
            DisplayName = "MEM 0.1.0 package",
            CreatedAtUtc = timestamp,
        };

        intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_package_projection",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:preview",
            CreatedAtUtc = timestamp,
            UploadedAtUtc = timestamp.AddMinutes(2),
            ValidatedAtUtc = timestamp.AddMinutes(3),
            AgeRecipient = "age1projectionrecipient",
            RecipientFingerprint = "1111-2222-3333-4444",
            PackageFileName = "source.memmigration.zip.age",
            PackageSizeBytes = 4096,
            EncryptedPackageSha256 = new string('2', 64),
            DecryptedArchiveSha256 = new string('3', 64),
            ArchiveMigrationId = "legacy-source-capture",
            ArchiveSourceProduct = "MatrixEasyMode",
            ArchiveSourceVersion = "0.1.0",
            ArchiveStackCount = 1,
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
            ValidationCode = "validated",
        });

        fixture.Db.MigrationIntakes.Add(intake);
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(
            intake.IntakeId,
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("mem-v010", detail!.Session.SourceAdapter);
        Assert.Equal("Message Easy Mode 0.1.0", detail.Session.SourceDisplay);
        Assert.Equal("source-assessment", detail.Session.Phase);
        Assert.Equal("package-validated", detail.Session.Status);
        Assert.Equal("review-source", detail.Session.NextAction);
        Assert.Equal(0, detail.Session.BlockerCount);
        Assert.Equal(0, detail.Session.WarningCount);
        Assert.Equal(0, detail.Session.AdvisoryCount);
        Assert.False(detail.Session.NeedsAttention);
        Assert.Equal(1, detail.Session.SourceCount);
        Assert.Equal(1, detail.Session.StackCount);

        var source = Assert.Single(detail.Sources);
        Assert.Equal("migration-package", source.Kind);
        Assert.Equal("MatrixEasyMode", source.Product);
        Assert.Equal("0.1.0", source.ProductVersion);
        Assert.Null(source.SourceFingerprint);

        Assert.NotNull(detail.Package);
        Assert.Equal(new string('2', 64), detail.Package!.EncryptedSha256);
        Assert.Equal(new string('3', 64), detail.Package.DecryptedSha256);
        Assert.Equal("legacy-source-capture", detail.Package.ArchiveMigrationId);
        Assert.Empty(detail.Findings);
        Assert.Empty(detail.LinkedObjects);
        Assert.False(detail.Session.HistoricalCompatibility.UsesLegacyNeutralImportContract);
        Assert.False(detail.Session.HistoricalCompatibility.UsesCatalogRestorePath);
        Assert.Equal(0, detail.Session.HistoricalCompatibility.CatalogEntryCount);
        Assert.Equal(0, detail.Session.HistoricalCompatibility.RestoreSessionCount);
    }

    [Fact]
    public async Task Session_without_package_revision_is_not_projected_as_final_migration_acceptance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var timestamp = Now.AddDays(-1).UtcDateTime;

        fixture.Db.MigrationIntakes.Add(new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_unclassified_history",
            DisplayName = "Historical unclassified Session",
            CreatedAtUtc = timestamp,
            Sources =
            [
                new MigrationSourceEntity
                {
                    Id = Guid.NewGuid(),
                    SourceId = "legacy-source",
                    SourceKind = "mem-v010",
                    Product = "MatrixEasyMode",
                    ProductVersion = "0.1.0",
                    SourceFingerprint = new string('6', 64),
                    CapturedAtUtc = timestamp,
                },
            ],
        });
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(
            "mig_unclassified_history",
            CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("manual-review-required", detail!.Session.Status);
        Assert.Equal("manual-review", detail.Session.Phase);
        Assert.Equal("review-migration", detail.Session.NextAction);
        Assert.Equal("mem-v010", detail.Session.SourceAdapter);
        Assert.Equal("Message Easy Mode 0.1.0", detail.Session.SourceDisplay);
        Assert.Null(detail.Package);
        Assert.Empty(detail.Findings);
        Assert.Empty(detail.LinkedObjects);
        Assert.False(detail.Session.HistoricalCompatibility.UsesLegacyNeutralImportContract);
        Assert.Null(detail.Session.HistoricalCompatibility.LegacyContractVersion);
        Assert.Null(detail.Session.HistoricalCompatibility.LegacyContractStatus);
        Assert.Null(detail.Session.HistoricalCompatibility.LegacyManifestSha256);
    }

    [Fact]
    public async Task Awaiting_final_revision_keeps_preview_candidate_available_for_private_staging()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = Now.AddHours(-1).UtcDateTime;
        var previewHash = new string('2', 64);
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_awaiting_final_projection",
            DisplayName = "Awaiting final projection",
            CreatedAtUtc = now,
        };
        var preview = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(), PackageRevisionId = "mpr_preview_awaiting_projection",
            MigrationIntake = intake, MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 1, Purpose = "preview", Status = "package-validated",
            RetentionState = "active", ActivePurposeKey = $"{intake.IntakeId}:preview",
            CreatedAtUtc = now, UploadedAtUtc = now, ValidatedAtUtc = now,
            DecryptedArchiveSha256 = previewHash, CaptureKind = "preview",
            SourceFrozen = false, RehearsalOnly = true,
        };
        var final = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(), PackageRevisionId = "mpr_final_awaiting_projection",
            MigrationIntake = intake, MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 2, Purpose = "final", Status = "awaiting-package",
            RetentionState = "active", ActivePurposeKey = $"{intake.IntakeId}:final",
            CreatedAtUtc = now.AddMinutes(1), ExpiresAtUtc = now.AddHours(1),
            AgeRecipient = "age1awaiting", RecipientFingerprint = "0000-0000-0000-0000",
            ProtectedAgeIdentity = "protected"
        };
        var attempt = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(), ConversionAttemptId = "conv_preview_awaiting_projection",
            MigrationIntake = intake, MigrationIntakeEntityId = intake.Id,
            PackageRevision = preview, MigrationPackageRevisionEntityId = preview.Id,
            SourcePackageSha256 = previewHash, SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "0.1.0", ConverterId = "worker", ConverterVersion = "v2",
            Status = "completed", CurrentStep = "candidate-created", CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(), CandidateArtifactId = "mca_preview_awaiting_projection",
            ConversionAttempt = attempt, MigrationConversionAttemptEntityId = attempt.Id,
            ArtifactKind = "synapse-postgresql-conversion", ArtifactSchemaVersion = "v1",
            SourcePackageSha256 = previewHash, ArtifactSha256 = new string('3', 64),
            ManifestSha256 = new string('4', 64), ChecksumsSha256 = new string('5', 64),
            ProvenanceJson = "{}", VerificationStatus = "verified", RetentionState = "active",
            StorageKind = "server-filesystem", ArtifactPath = "/private/preview.dump",
            CreatedAtUtc = now, VerifiedAtUtc = now,
        };
        attempt.CandidateArtifact = candidate;
        fixture.Db.AddRange(intake, preview, final, attempt, candidate);
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("staging", detail!.Session.Phase);
        Assert.Equal("staging-ready", detail.Session.Status);
        Assert.Equal("start-private-staging", detail.Session.NextAction);
    }

    [Fact]
    public async Task Final_package_authority_hides_preview_candidate_until_final_conversion_exists()
    {
        await using var fixture = await Fixture.CreateAsync();
        var now = Now.AddHours(-1).UtcDateTime;
        var previewHash = new string('2', 64);
        var finalHash = new string('f', 64);
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_final_authority_projection",
            DisplayName = "Final authority projection",
            CreatedAtUtc = now,
        };
        var preview = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(), PackageRevisionId = "mpr_preview_projection",
            MigrationIntake = intake, MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 1, Purpose = "preview", Status = "package-validated",
            RetentionState = "active", ActivePurposeKey = $"{intake.IntakeId}:preview",
            CreatedAtUtc = now, UploadedAtUtc = now, ValidatedAtUtc = now,
            DecryptedArchiveSha256 = previewHash, CaptureKind = "preview",
            SourceFrozen = false, RehearsalOnly = true,
        };
        var final = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(), PackageRevisionId = "mpr_final_projection",
            MigrationIntake = intake, MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 2, Purpose = "final", Status = "package-validated",
            RetentionState = "active", ActivePurposeKey = $"{intake.IntakeId}:final",
            CreatedAtUtc = now.AddMinutes(1), UploadedAtUtc = now.AddMinutes(1), ValidatedAtUtc = now.AddMinutes(1),
            DecryptedArchiveSha256 = finalHash, CaptureKind = "final",
            SourceFrozen = true, RehearsalOnly = false,
        };
        var previewAttempt = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(), ConversionAttemptId = "conv_preview_projection",
            MigrationIntake = intake, MigrationIntakeEntityId = intake.Id,
            PackageRevision = preview, MigrationPackageRevisionEntityId = preview.Id,
            SourcePackageSha256 = previewHash, SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "0.1.0", ConverterId = "worker", ConverterVersion = "v2",
            Status = "completed", CurrentStep = "candidate-created", CreatedAtUtc = now, UpdatedAtUtc = now,
        };
        var previewCandidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(), CandidateArtifactId = "mca_preview_projection",
            ConversionAttempt = previewAttempt, MigrationConversionAttemptEntityId = previewAttempt.Id,
            ArtifactKind = "synapse-postgresql-conversion", ArtifactSchemaVersion = "v1",
            SourcePackageSha256 = previewHash, ArtifactSha256 = new string('3', 64),
            ManifestSha256 = new string('4', 64), ChecksumsSha256 = new string('5', 64),
            ProvenanceJson = "{}", VerificationStatus = "verified", RetentionState = "active",
            StorageKind = "server-filesystem", ArtifactPath = "/private/preview.dump",
            CreatedAtUtc = now, VerifiedAtUtc = now,
        };
        previewAttempt.CandidateArtifact = previewCandidate;
        intake.PackageRevisions.Add(preview);
        intake.PackageRevisions.Add(final);
        intake.ConversionAttempts.Add(previewAttempt);
        fixture.Db.MigrationIntakes.Add(intake);
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("source-assessment", detail!.Session.Phase);
        Assert.Equal("package-validated", detail.Session.Status);
        Assert.Equal("review-source", detail.Session.NextAction);
    }

    [Fact]
    public async Task Verified_candidate_and_staging_history_drive_the_current_lifecycle_projection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var createdAtUtc = Now.AddHours(-2).UtcDateTime;
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_staging_lifecycle",
            DisplayName = "Private staging lifecycle",
            CreatedAtUtc = createdAtUtc,
        };
        var previewRevision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_staging_lifecycle_preview",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:preview",
            CreatedAtUtc = createdAtUtc,
            UploadedAtUtc = createdAtUtc.AddMinutes(4),
            ValidatedAtUtc = createdAtUtc.AddMinutes(5),
            DecryptedArchiveSha256 = new string('2', 64),
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
        };
        intake.PackageRevisions.Add(previewRevision);
        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "conv_staging_lifecycle",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = previewRevision.Id,
            PackageRevision = previewRevision,
            SourcePackageSha256 = new string('2', 64),
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "1",
            ConverterId = "mem-migrate-worker",
            ConverterVersion = "0.2.0",
            Status = "completed",
            CurrentStep = "candidate-created",
            CreatedAtUtc = createdAtUtc.AddMinutes(10),
            UpdatedAtUtc = createdAtUtc.AddMinutes(20),
            StartedAtUtc = createdAtUtc.AddMinutes(10),
            CompletedAtUtc = createdAtUtc.AddMinutes(20),
            ResultCode = "0",
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = "mca_staging_lifecycle",
            MigrationConversionAttemptEntityId = conversion.Id,
            ConversionAttempt = conversion,
            ArtifactKind = "synapse-postgresql-conversion",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = new string('2', 64),
            ArtifactSha256 = new string('3', 64),
            ManifestSha256 = new string('4', 64),
            ChecksumsSha256 = new string('5', 64),
            ProvenanceJson = "{}",
            VerificationStatus = "verified",
            RetentionState = "active",
            StorageKind = "private-file",
            ArtifactPath = "/private/candidate.dump",
            CreatedAtUtc = createdAtUtc.AddMinutes(20),
            VerifiedAtUtc = createdAtUtc.AddMinutes(20),
        };
        conversion.CandidateArtifact = candidate;
        intake.ConversionAttempts.Add(conversion);
        fixture.Db.MigrationIntakes.Add(intake);
        await fixture.Db.SaveChangesAsync();

        var ready = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);
        Assert.NotNull(ready);
        Assert.Equal("staging", ready!.Session.Phase);
        Assert.Equal("staging-ready", ready.Session.Status);
        Assert.Equal("start-private-staging", ready.Session.NextAction);
        Assert.Equal(candidate.VerifiedAtUtc, ready.Session.UpdatedAtUtc);

        var run = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = "mstg_staging_lifecycle",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            ActiveMigrationKey = intake.IntakeId,
            Status = "running",
            CurrentStep = "materialising-private-runtime",
            CreatedAtUtc = createdAtUtc.AddMinutes(30),
            UpdatedAtUtc = createdAtUtc.AddMinutes(31),
            StartedAtUtc = createdAtUtc.AddMinutes(30),
        };
        fixture.Db.MigrationStagingRuns.Add(run);
        await fixture.Db.SaveChangesAsync();

        var running = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);
        Assert.NotNull(running);
        Assert.Equal("staging-running", running!.Session.Status);
        Assert.Equal("review-private-staging", running.Session.NextAction);
        Assert.Equal(run.UpdatedAtUtc, running.Session.UpdatedAtUtc);

        run.Status = "verified";
        run.CurrentStep = "private-verification-complete";
        run.PrivateOnly = true;
        run.PublicRoutesCreated = false;
        run.DatabaseImportSucceeded = true;
        run.SynapseHealthPassed = true;
        run.CompletedAtUtc = createdAtUtc.AddMinutes(40);
        run.UpdatedAtUtc = createdAtUtc.AddMinutes(40);
        await fixture.Db.SaveChangesAsync();

        var verified = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);
        Assert.NotNull(verified);
        Assert.Equal("staging-verified", verified!.Session.Status);
        Assert.Equal("review-private-staging", verified.Session.NextAction);

        run.Status = "destroyed";
        run.CurrentStep = "destroyed";
        run.ActiveMigrationKey = null;
        run.DestroyedAtUtc = createdAtUtc.AddMinutes(50);
        run.UpdatedAtUtc = createdAtUtc.AddMinutes(50);
        await fixture.Db.SaveChangesAsync();

        var destroyed = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);
        Assert.NotNull(destroyed);
        Assert.Equal("staging-destroyed", destroyed!.Session.Status);
        Assert.Equal("recreate-private-staging", destroyed.Session.NextAction);
    }

    [Fact]
    public async Task Public_production_runtime_is_projected_as_cutover_instead_of_regressing_to_staging()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await AddProductionAdoptionAsync(
            fixture,
            migrationId: "mig_public_projection",
            adoptionStatus: "public-awaiting-verification",
            verificationStatus: null,
            verificationValidUntilUtc: null);

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("cutover", detail!.Session.Phase);
        Assert.Equal("public-awaiting-verification", detail.Session.Status);
        Assert.Equal("review-production-adoption", detail.Session.NextAction);
    }

    [Fact]
    public async Task Passed_production_verification_projects_acceptance_phase_and_action()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await AddProductionAdoptionAsync(
            fixture,
            migrationId: "mig_acceptance_ready_projection",
            adoptionStatus: "production-verification-passed",
            verificationStatus: "passed",
            verificationValidUntilUtc: Now.AddMinutes(30).UtcDateTime);

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("acceptance", detail!.Session.Phase);
        Assert.Equal("production-verification-passed", detail.Session.Status);
        Assert.Equal("accept-migration", detail.Session.NextAction);
        Assert.False(detail.Session.NeedsAttention);
        Assert.Equal(Now.AddMinutes(-5).UtcDateTime, detail.Session.UpdatedAtUtc);
    }

    [Fact]
    public async Task Elapsed_verification_metadata_does_not_remove_acceptance_phase()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await AddProductionAdoptionAsync(
            fixture,
            migrationId: "mig_acceptance_retained_projection",
            adoptionStatus: "production-verification-passed",
            verificationStatus: "passed",
            verificationValidUntilUtc: Now.AddMinutes(-1).UtcDateTime);

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("acceptance", detail!.Session.Phase);
        Assert.Equal("production-verification-passed", detail.Session.Status);
        Assert.Equal("accept-migration", detail.Session.NextAction);
        Assert.False(detail.Session.NeedsAttention);
    }

    [Fact]
    public async Task Unknown_migration_id_returns_null()
    {
        await using var fixture = await Fixture.CreateAsync();

        var detail = await fixture.Service.GetAsync(
            "mig_not_present",
            CancellationToken.None);

        Assert.Null(detail);
    }

    [Fact]
    public async Task Final_migration_acceptance_is_projected_as_acceptance_phase()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_final_acceptance_projection",
            DisplayName = "Accepted migration",
            CreatedAtUtc = Now.AddDays(-1).UtcDateTime,
        };
        var acceptance = new MigrationAcceptanceEntity
        {
            Id = Guid.NewGuid(), AcceptanceId = "macc_projection", MigrationIntakeEntityId = intake.Id,
            ExecutionId = "execution_projection", CandidateArtifactId = "candidate_projection",
            StagingRunId = "staging_projection", PublicVerificationStatus = "passed",
            PublicVerificationEvidenceJson = "{\"checks\":[]}", PublicVerificationEvidenceSha256 = new string('1', 64),
            PublicCutoverAtUtc = Now.AddHours(-2).UtcDateTime, AcceptedAtUtc = Now.AddHours(-1).UtcDateTime,
            AcceptedBy = "owner", FreshPublicVerificationAcknowledged = true, TargetWriteDivergenceAcknowledged = true,
            RollbackBoundaryAcknowledged = true, LegacyRetentionAcknowledged = true,
            NoAutomaticLegacyDeletionAcknowledged = true,
        };
        var retention = new LegacyRetentionRecordEntity
        {
            Id = Guid.NewGuid(), RetentionRecordId = "mlr_projection", MigrationIntakeEntityId = intake.Id,
            MigrationAcceptanceEntityId = acceptance.Id, Status = "active", CreatedAtUtc = Now.AddHours(-1).UtcDateTime,
            RetainUntilUtc = Now.AddDays(13).UtcDateTime, CleanupEligibleAtUtc = Now.AddDays(13).UtcDateTime,
            SourceMigrationId = "source_projection", SourceProduct = "Message Easy Mode", SourceVersion = "0.1.0",
            SourcePackageRetained = true, CandidateArtifactRetained = true, PrivateStagingEvidenceRetained = true,
            LegacySourceResourcesRetained = true, AutomaticDeletionAllowed = false, Summary = "Retained",
        };
        acceptance.LegacyRetentionRecord = retention;
        intake.Acceptance = acceptance;
        intake.LegacyRetentionRecord = retention;
        fixture.Db.AddRange(intake, acceptance, retention);
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("acceptance", detail.Session.Phase);
        Assert.Equal("accepted-baseline-backup-pending", detail.Session.Status);
        Assert.Equal("review-baseline-backup", detail.Session.NextAction);
    }

    [Fact]
    public async Task Baseline_backup_creation_completes_migration_and_links_catalog_entry()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(), IntakeId = "mig_baseline_complete",
            DisplayName = "Completed migration", CreatedAtUtc = Now.AddDays(-1).UtcDateTime,
        };
        var acceptance = new MigrationAcceptanceEntity
        {
            Id = Guid.NewGuid(), AcceptanceId = "macc_complete", MigrationIntakeEntityId = intake.Id,
            ExecutionId = "execution_complete", CandidateArtifactId = "candidate_complete", StagingRunId = "staging_complete",
            PublicVerificationStatus = "passed", PublicVerificationEvidenceJson = "{\"checks\":[]}",
            PublicVerificationEvidenceSha256 = new string('1', 64), PublicCutoverAtUtc = Now.AddHours(-2).UtcDateTime,
            AcceptedAtUtc = Now.AddHours(-1).UtcDateTime, AcceptedBy = "owner",
            FreshPublicVerificationAcknowledged = true, TargetWriteDivergenceAcknowledged = true,
            RollbackBoundaryAcknowledged = true, LegacyRetentionAcknowledged = true,
            NoAutomaticLegacyDeletionAcknowledged = true,
        };
        var handoff = new MigrationBaselineBackupHandoffEntity
        {
            Id = Guid.NewGuid(), HandoffId = "mbh_complete", MigrationIntakeEntityId = intake.Id,
            MigrationAcceptanceEntityId = acceptance.Id, Status = "created", AttemptCount = 1,
            CreatedAtUtc = Now.AddMinutes(-50).UtcDateTime, UpdatedAtUtc = Now.AddMinutes(-49).UtcDateTime,
            CompletedAtUtc = Now.AddMinutes(-49).UtcDateTime, TargetStackSlug = "migrated-stack",
            CandidateId = "candidate-runtime", PrivateRuntimeId = "runtime-1", BackupId = "backup-1",
            CatalogEntryId = "catalog-baseline", BackupCreatedAtUtc = Now.AddMinutes(-49).UtcDateTime,
        };
        var catalog = new BackupCatalogEntryEntity
        {
            Id = Guid.NewGuid(), CatalogEntryId = "catalog-baseline", DisplayName = "First native baseline backup",
            OriginKind = "local-captured", PayloadStorageKind = "local", PayloadDirectoryPath = "/tmp/baseline",
            PayloadState = "available", IntegrityStatus = "valid", CreatedAtUtc = Now.AddMinutes(-49).UtcDateTime,
            WarningCount = 0,
        };
        intake.Acceptance = acceptance; intake.BaselineBackupHandoff = handoff;
        acceptance.BaselineBackupHandoff = handoff;
        fixture.Db.AddRange(intake, acceptance, handoff, catalog);
        await fixture.Db.SaveChangesAsync();

        var detail = await fixture.Service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("completed", detail!.Session.Phase);
        Assert.Equal("migration-completed", detail.Session.Status);
        Assert.Equal("open-baseline-backup", detail.Session.NextAction);
        Assert.Contains(detail.LinkedObjects, linked =>
            linked.Id == "catalog-baseline" && linked.Relationship == "first-native-baseline-backup" && !linked.Historical);
    }

    private static async Task<MigrationIntakeEntity> AddProductionAdoptionAsync(
        Fixture fixture,
        string migrationId,
        string adoptionStatus,
        string? verificationStatus,
        DateTime? verificationValidUntilUtc)
    {
        var createdAtUtc = Now.AddHours(-4).UtcDateTime;
        var packageSha256 = new string('a', 64);
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = migrationId,
            DisplayName = "Production migration projection",
            CreatedAtUtc = createdAtUtc,
        };
        var revision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr_{migrationId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            RevisionNumber = 1,
            Purpose = "preview",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{migrationId}:preview",
            CreatedAtUtc = createdAtUtc,
            ValidatedAtUtc = createdAtUtc.AddMinutes(5),
            DecryptedArchiveSha256 = packageSha256,
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
        };
        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = $"conv_{migrationId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            SourcePackageSha256 = packageSha256,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "1",
            ConverterId = "synapse-postgresql",
            ConverterVersion = "1",
            Status = "completed",
            CurrentStep = "candidate-created",
            CreatedAtUtc = createdAtUtc.AddMinutes(10),
            UpdatedAtUtc = createdAtUtc.AddMinutes(20),
            StartedAtUtc = createdAtUtc.AddMinutes(10),
            CompletedAtUtc = createdAtUtc.AddMinutes(20),
            ResultCode = "0",
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = $"mca_{migrationId}",
            MigrationConversionAttemptEntityId = conversion.Id,
            ConversionAttempt = conversion,
            ArtifactKind = "synapse-postgresql-conversion",
            ArtifactSchemaVersion = "1",
            SourcePackageSha256 = packageSha256,
            ArtifactSha256 = new string('b', 64),
            ManifestSha256 = new string('c', 64),
            ChecksumsSha256 = new string('d', 64),
            ProvenanceJson = "{}",
            VerificationStatus = "verified",
            RetentionState = "active",
            StorageKind = "private-file",
            ArtifactPath = "/private/candidate.dump",
            CreatedAtUtc = createdAtUtc.AddMinutes(20),
            VerifiedAtUtc = createdAtUtc.AddMinutes(20),
        };
        conversion.CandidateArtifact = candidate;
        var staging = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = $"mstg_{migrationId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            ActiveMigrationKey = migrationId,
            Status = "verified",
            CurrentStep = "private-verification-complete",
            CreatedAtUtc = createdAtUtc.AddMinutes(30),
            UpdatedAtUtc = createdAtUtc.AddMinutes(40),
            StartedAtUtc = createdAtUtc.AddMinutes(30),
            CompletedAtUtc = createdAtUtc.AddMinutes(40),
            PrivateOnly = true,
            PublicRoutesCreated = false,
            DatabaseImportSucceeded = true,
            SynapseHealthPassed = true,
            ElementContainerStarted = true,
            ElementHealthPassed = true,
            UsersCount = 3,
            RoomsCount = 2,
            EventsCount = 28,
            MatrixServerName = "matrix.example.test",
        };
        var adoption = new MigrationProductionAdoptionEntity
        {
            Id = Guid.NewGuid(),
            AdoptionPlanId = $"madp_{migrationId}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            MigrationStagingRunEntityId = staging.Id,
            StagingRun = staging,
            Status = adoptionStatus,
            RevisionNumber = 1,
            PlanSha256 = new string('e', 64),
            CreatedAtUtc = createdAtUtc.AddMinutes(45),
            UpdatedAtUtc = Now.AddMinutes(-5).UtcDateTime,
            PreparedAtUtc = createdAtUtc.AddMinutes(45),
            RuntimeStackId = Guid.NewGuid(),
            TargetStackSlug = "tester",
            TargetDisplayName = "tester",
            MatrixInstanceId = Guid.NewGuid(),
            ElementInstanceId = Guid.NewGuid(),
            MatrixServerName = "matrix.example.test",
            MatrixPublicHost = "matrix.example.test",
            MatrixPublicBaseUrl = "https://matrix.example.test",
            ElementPublicHost = "element.example.test",
            ElementPublicBaseUrl = "https://element.example.test",
            RuntimeNetworkName = "mem-gateway",
            RuntimeDataRoot = "/runtime/tester",
            ManifestPath = "/runtime/tester/manifest.json",
            MatrixContainerName = "mem-matrix-tester",
            MatrixDataPath = "/runtime/tester/matrix",
            ElementContainerName = "mem-element-tester",
            ElementDataPath = "/runtime/tester/element",
            MatrixImageReference = "sha256:matrix",
            MatrixImageId = "sha256:matrix",
            ElementImageReference = "sha256:element",
            ElementImageId = "sha256:element",
            DatabaseEngine = "postgres",
            DatabaseHost = "mem-postgres",
            DatabasePort = 5432,
            DatabaseName = "matrix_tester",
            DatabaseUsername = "mxu_tester",
            DatabasePasswordSecretKind = "matrix_postgres_password",
            RoutePlanJson = "[]",
            ProvenanceJson = "{}",
            CollisionEvidenceJson = "[]",
            RuntimeRecordsCreated = true,
            MaterializationStatus = "private-runtime-ready",
            ProductionDatabaseImported = true,
            MatrixProductionContainerStarted = true,
            MatrixProductionHealthPassed = true,
            ElementProductionContainerStarted = true,
            ElementProductionHealthPassed = true,
            RuntimeManifestSaved = true,
            DatabaseOwnershipSaved = true,
            UserInventorySynchronized = true,
            CutoverStatus = "public-awaiting-verification",
            RuntimePromotionCompleted = true,
            PublicRoutesCreated = true,
            TargetPublicAtUtc = createdAtUtc.AddHours(2),
            ProductionVerificationStatus = verificationStatus,
            ProductionVerificationValidUntilUtc = verificationValidUntilUtc,
            ProductionVerificationCompletedAtUtc = verificationStatus is null
                ? null
                : Now.AddMinutes(-5).UtcDateTime,
        };

        intake.PackageRevisions.Add(revision);
        intake.ConversionAttempts.Add(conversion);
        intake.StagingRuns.Add(staging);
        intake.ProductionAdoption = adoption;
        fixture.Db.AddRange(intake, revision, conversion, candidate, staging, adoption);
        await fixture.Db.SaveChangesAsync();
        return intake;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationSessionProjectionService service)
        {
            _connection = connection;
            Db = db;
            Service = service;
        }

        public MemDbContext Db { get; }
        public MigrationSessionProjectionService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var service = new MigrationSessionProjectionService(
                db,
                new FixedTimeProvider(Now));

            return new Fixture(connection, db, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
