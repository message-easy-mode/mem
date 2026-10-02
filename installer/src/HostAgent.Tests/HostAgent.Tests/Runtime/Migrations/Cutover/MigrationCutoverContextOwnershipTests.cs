using System.IO.Compression;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Cutover;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.Cutover;

public sealed class MigrationCutoverContextOwnershipTests
{
    private const string TargetMigrationId = "mig_20260716-101702Z_target-session";
    private const string SourceMigrationId = "20260716-033032Z-source-capture";
    private const string PackageSha256 = "4a17bc1013225e863aea4deaebeaaa9c374abdfac009f00ee68eabea23cad50f";

    [Fact]
    public void Accepts_target_session_ownership_and_distinct_source_migration_provenance()
    {
        var fixture = CreateOwnershipFixture();

        MigrationCutoverContextResolver.ValidateCandidateSessionOwnership(
            fixture.Intake,
            fixture.Attempt,
            fixture.Candidate);

        MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
            fixture.Intake,
            fixture.PackageRevision,
            fixture.Attempt,
            fixture.Candidate,
            fixture.Provenance);

        Assert.NotEqual(fixture.Intake.IntakeId, fixture.Provenance.MigrationId);
        Assert.Equal(fixture.PackageRevision.ArchiveMigrationId, fixture.Provenance.MigrationId);
    }

    [Fact]
    public void Rejects_candidate_relationship_from_another_migration_session()
    {
        var fixture = CreateOwnershipFixture();
        fixture.Attempt.MigrationIntakeEntityId = Guid.NewGuid();

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ValidateCandidateSessionOwnership(
                fixture.Intake,
                fixture.Attempt,
                fixture.Candidate));

        Assert.Contains("does not belong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_candidate_bound_to_a_different_conversion_attempt()
    {
        var fixture = CreateOwnershipFixture();
        fixture.Candidate.MigrationConversionAttemptEntityId = Guid.NewGuid();

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ValidateCandidateSessionOwnership(
                fixture.Intake,
                fixture.Attempt,
                fixture.Candidate));

        Assert.Contains("does not belong", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_source_migration_identity_that_does_not_match_the_validated_package()
    {
        var fixture = CreateOwnershipFixture();
        var wrongProvenance = fixture.Provenance with { MigrationId = "another-source-capture" };

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
                fixture.Intake,
                fixture.PackageRevision,
                fixture.Attempt,
                fixture.Candidate,
                wrongProvenance));

        Assert.Contains("source migration identity", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("attempt")]
    [InlineData("candidate")]
    public void Rejects_source_package_checksum_that_does_not_match_the_validated_session(string mismatch)
    {
        var fixture = CreateOwnershipFixture();
        if (mismatch == "attempt")
        {
            fixture.Attempt.SourcePackageSha256 = new string('a', 64);
        }
        else
        {
            fixture.Candidate.SourcePackageSha256 = new string('b', 64);
        }

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
                fixture.Intake,
                fixture.PackageRevision,
                fixture.Attempt,
                fixture.Candidate,
                fixture.Provenance));

        Assert.Contains("checksum", exception.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void Rejects_conversion_attempt_from_another_package_revision()
    {
        var fixture = CreateOwnershipFixture();
        fixture.Attempt.MigrationPackageRevisionEntityId = Guid.NewGuid();

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
                fixture.Intake,
                fixture.PackageRevision,
                fixture.Attempt,
                fixture.Candidate,
                fixture.Provenance));

        Assert.Contains("authoritative package revision", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolves_archive_against_source_migration_identity_not_target_session_identity()
    {
        var stackId = Guid.NewGuid();
        using var archive = ArchiveFixture.Create(SourceMigrationId, stackId, "matrix.example.test");

        var result = MigrationCutoverContextResolver.ReadArchiveSummary(
            archive.Path,
            SourceMigrationId,
            stackId,
            "matrix.example.test");

        Assert.Equal(SourceMigrationId, result.Manifest.MigrationId);
        Assert.Equal(stackId, result.Stack.SourceStackId);

        var targetIdentity = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ReadArchiveSummary(
                archive.Path,
                TargetMigrationId,
                stackId,
                "matrix.example.test"));
        Assert.Contains("unsupported", targetIdentity.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolves_exact_operator_attested_preview_authority_chain()
    {
        var fixture = CreateSelectionFixture(operatorAttested: true);

        var selection = MigrationCutoverContextResolver.ResolveAuthoritativeSelection(
            fixture.Intake);

        Assert.Same(fixture.Authority, selection.ProductionAuthority);
        Assert.Same(fixture.PackageRevision, selection.PackageRevision);
        Assert.Same(fixture.Candidate, selection.CandidateArtifact);
        Assert.Same(fixture.Staging, selection.StagingRun);
    }

    [Fact]
    public void Planning_review_can_use_verified_preview_without_creating_production_authority()
    {
        var fixture = CreateSelectionFixture(operatorAttested: true);
        fixture.Intake.ProductionAuthorities.Clear();

        var selection = MigrationCutoverContextResolver.ResolvePlanningSelection(
            fixture.Intake);

        Assert.Null(selection.ProductionAuthority);
        Assert.Equal("preview", selection.PackageRevision.Purpose);
        Assert.Same(fixture.Candidate, selection.CandidateArtifact);
        Assert.Same(fixture.Staging, selection.StagingRun);
    }

    [Fact]
    public void Accepts_preview_candidate_provenance_only_with_matching_active_authority()
    {
        var fixture = CreateSelectionFixture(operatorAttested: true);
        var attempt = Assert.Single(fixture.Intake.ConversionAttempts);
        var provenance = new MigrationCutoverContextResolver.CandidateProvenance(
            fixture.PackageRevision.ArchiveMigrationId!,
            Guid.NewGuid(),
            "matrix.example.test");

        MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
            fixture.Intake,
            fixture.PackageRevision,
            attempt,
            fixture.Candidate,
            provenance,
            fixture.Authority);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
                fixture.Intake,
                fixture.PackageRevision,
                attempt,
                fixture.Candidate,
                provenance));

        Assert.Contains("requires active production authority", exception.Message, StringComparison.OrdinalIgnoreCase);

        MigrationCutoverContextResolver.ValidateCandidateSourceProvenance(
            fixture.Intake,
            fixture.PackageRevision,
            attempt,
            fixture.Candidate,
            provenance,
            authority: null,
            allowVerifiedPreviewWithoutAuthority: true);
    }

    [Fact]
    public void Preserves_final_frozen_selection_when_no_explicit_authority_exists()
    {
        var fixture = CreateSelectionFixture(operatorAttested: false);

        var selection = MigrationCutoverContextResolver.ResolveAuthoritativeSelection(
            fixture.Intake);

        Assert.Null(selection.ProductionAuthority);
        Assert.Equal("final", selection.PackageRevision.Purpose);
        Assert.Same(fixture.Candidate, selection.CandidateArtifact);
        Assert.Same(fixture.Staging, selection.StagingRun);
    }

    [Fact]
    public void Rejects_operator_authority_when_package_binding_drifts()
    {
        var fixture = CreateSelectionFixture(operatorAttested: true);
        fixture.Authority!.DecryptedArchiveSha256 = new string('f', 64);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationCutoverContextResolver.ResolveAuthoritativeSelection(
                fixture.Intake));

        Assert.Contains("evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SelectionFixture CreateSelectionFixture(bool operatorAttested)
    {
        var migrationId = operatorAttested
            ? "mig_operator_authority_selection"
            : "mig_final_fallback_selection";
        var sourceMigrationId = operatorAttested
            ? "source-preview-capture"
            : "source-final-capture";
        var packageSha256 = operatorAttested
            ? new string('4', 64)
            : new string('5', 64);
        var encryptedSha256 = operatorAttested
            ? new string('6', 64)
            : new string('7', 64);
        var now = DateTime.UtcNow;

        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = migrationId,
        };
        var revision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = operatorAttested ? "mpr_preview" : "mpr_final",
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 1,
            Purpose = operatorAttested ? "preview" : "final",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{migrationId}:{(operatorAttested ? "preview" : "final")}",
            EncryptedPackageSha256 = encryptedSha256,
            DecryptedArchiveSha256 = packageSha256,
            ArchiveMigrationId = sourceMigrationId,
            CaptureKind = operatorAttested ? "preview" : "final",
            SourceFrozen = !operatorAttested,
            RehearsalOnly = operatorAttested,
        };
        var attempt = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            MigrationIntakeEntityId = intake.Id,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            SourcePackageSha256 = packageSha256,
            Status = "completed-with-warnings",
            CurrentStep = "candidate-created",
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = operatorAttested ? "mca_preview" : "mca_final",
            MigrationConversionAttemptEntityId = attempt.Id,
            ConversionAttempt = attempt,
            SourcePackageSha256 = packageSha256,
            VerificationStatus = "verified",
            RetentionState = "active",
            CreatedAtUtc = now.AddMinutes(-5),
            VerifiedAtUtc = now.AddMinutes(-4),
        };
        attempt.CandidateArtifact = candidate;
        var staging = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = operatorAttested ? "mstg_preview" : "mstg_final",
            MigrationIntakeEntityId = intake.Id,
            MigrationCandidateArtifactEntityId = candidate.Id,
            ActiveMigrationKey = migrationId,
            Status = "verified",
            UpdatedAtUtc = now.AddMinutes(-2),
            CompletedAtUtc = now.AddMinutes(-2),
            PrivateRuntimeStagingId = "private-staging",
            PrivateOnly = true,
            PublicRoutesCreated = false,
            DatabaseImportSucceeded = true,
            SynapseHealthPassed = true,
            ElementConfigPresent = true,
            ElementContainerStarted = true,
            ElementHealthPassed = true,
            ElementSynapseConnectivityPassed = true,
            ElementNetworkAttached = true,
        };

        MigrationProductionAuthorityEntity? authority = null;
        if (operatorAttested)
        {
            authority = new MigrationProductionAuthorityEntity
            {
                Id = Guid.NewGuid(),
                ProductionAuthorityId = "mpauth_preview",
                MigrationIntakeEntityId = intake.Id,
                MigrationPackageRevisionEntityId = revision.Id,
                MigrationCandidateArtifactEntityId = candidate.Id,
                MigrationStagingRunEntityId = staging.Id,
                AuthorityType = MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
                Status = MigrationProductionAuthorityStatuses.Active,
                ActiveMigrationKey = migrationId,
                EncryptedPackageSha256 = encryptedSha256,
                DecryptedArchiveSha256 = packageSha256,
                SourceMigrationId = sourceMigrationId,
                MatrixServerName = "matrix.example.test",
                CaptureKind = "preview",
                SourceFrozen = false,
                RehearsalOnly = true,
            };
            intake.ProductionAuthorities.Add(authority);
        }

        intake.PackageRevisions.Add(revision);
        intake.ConversionAttempts.Add(attempt);
        intake.StagingRuns.Add(staging);

        return new SelectionFixture(
            intake,
            revision,
            candidate,
            staging,
            authority);
    }

    private static OwnershipFixture CreateOwnershipFixture()
    {
        var intakeId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var stackId = Guid.NewGuid();

        var intake = new MigrationIntakeEntity
        {
            Id = intakeId,
            IntakeId = TargetMigrationId,
        };
        var packageRevision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpkg_final_001",
            MigrationIntakeEntityId = intakeId,
            RevisionNumber = 2,
            Purpose = "final",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{TargetMigrationId}:final",
            ArchiveMigrationId = SourceMigrationId,
            DecryptedArchiveSha256 = PackageSha256,
            CaptureKind = "final",
            SourceFrozen = true,
            RehearsalOnly = false,
        };
        var attempt = new MigrationConversionAttemptEntity
        {
            Id = attemptId,
            MigrationIntakeEntityId = intakeId,
            MigrationPackageRevisionEntityId = packageRevision.Id,
            PackageRevision = packageRevision,
            SourcePackageSha256 = PackageSha256.ToUpperInvariant(),
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            MigrationConversionAttemptEntityId = attemptId,
            SourcePackageSha256 = PackageSha256,
        };
        var provenance = new MigrationCutoverContextResolver.CandidateProvenance(
            SourceMigrationId,
            stackId,
            "matrix.example.test");

        return new OwnershipFixture(intake, packageRevision, attempt, candidate, provenance);
    }

    private sealed record OwnershipFixture(
        MigrationIntakeEntity Intake,
        MigrationPackageRevisionEntity PackageRevision,
        MigrationConversionAttemptEntity Attempt,
        MigrationCandidateArtifactEntity Candidate,
        MigrationCutoverContextResolver.CandidateProvenance Provenance);

    private sealed record SelectionFixture(
        MigrationIntakeEntity Intake,
        MigrationPackageRevisionEntity PackageRevision,
        MigrationCandidateArtifactEntity Candidate,
        MigrationStagingRunEntity Staging,
        MigrationProductionAuthorityEntity? Authority);

    private sealed class ArchiveFixture : IDisposable
    {
        private ArchiveFixture(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static ArchiveFixture Create(
            string migrationId,
            Guid stackId,
            string matrixServerName)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mem-cutover-ownership-{Guid.NewGuid():N}.zip");

            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                var manifest = new
                {
                    schema = "mem-v010-migration",
                    schemaVersion = 2,
                    migrationId,
                    capture = new
                    {
                        kind = "preview",
                        sourceFrozen = false,
                        rehearsalOnly = true,
                    },
                    stacks = new[]
                    {
                        new
                        {
                            sourceStackId = stackId,
                            slug = "example",
                            displayName = "Example",
                            matrixServerName,
                            matrixPublicUrl = $"https://{matrixServerName}",
                            elementPublicUrl = "https://chat.example.test",
                        },
                    },
                };

                var entry = archive.CreateEntry(
                    "mem-migration/migration-manifest.json",
                    CompressionLevel.NoCompression);
                using var output = entry.Open();
                JsonSerializer.Serialize(output, manifest);
            }

            return new ArchiveFixture(path);
        }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
