using HostAgent.Runtime.Migrations.Assurance;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.Assurance;

public sealed class MigrationAssuranceProjectionTests
{
    [Fact]
    public void Operator_attested_authority_projects_reduced_assurance_without_rewriting_capture()
    {
        var fixture = CreateFixture(operatorAttested: true);

        var assurance = MigrationAssuranceProjection.Resolve(fixture.Intake, fixture.Plan);

        Assert.Equal(MigrationProductionAuthorityTypes.OperatorAttestedSnapshot, assurance.AuthorityType);
        Assert.Equal("mpauth_test", assurance.ProductionAuthorityId);
        Assert.False(assurance.FormalSourceFreezeEvidenceCollected);
        Assert.False(assurance.FinalRecapturePerformed);
        Assert.False(assurance.PostCaptureWritesIndependentlyExcluded);
        Assert.False(assurance.TwoServerQualificationRequired);
        Assert.Equal(MigrationAssuranceProjection.ReducedRollback, assurance.RollbackAssurance);
        Assert.Equal("preview", assurance.CaptureKind);
        Assert.True(assurance.RehearsalOnly);
    }

    [Fact]
    public void Final_frozen_package_projects_high_assurance_and_requires_two_server_qualification()
    {
        var fixture = CreateFixture(operatorAttested: false);

        var assurance = MigrationAssuranceProjection.Resolve(fixture.Intake, fixture.Plan);

        Assert.Equal(MigrationProductionAuthorityTypes.FinalFrozen, assurance.AuthorityType);
        Assert.True(assurance.FormalSourceFreezeEvidenceCollected);
        Assert.True(assurance.FinalRecapturePerformed);
        Assert.True(assurance.PostCaptureWritesIndependentlyExcluded);
        Assert.True(assurance.TwoServerQualificationRequired);
        Assert.Equal(MigrationAssuranceProjection.CoordinatedRollback, assurance.RollbackAssurance);
    }

    [Fact]
    public void Active_authority_must_match_the_exact_adoption_chain()
    {
        var fixture = CreateFixture(operatorAttested: true);
        fixture.Authority!.MigrationStagingRunEntityId = Guid.NewGuid();

        Assert.Throws<InvalidDataException>(() =>
            MigrationAssuranceProjection.Resolve(fixture.Intake, fixture.Plan));
    }

    private static Fixture CreateFixture(bool operatorAttested)
    {
        var intakeId = Guid.NewGuid();
        var revisionId = Guid.NewGuid();
        var candidateId = Guid.NewGuid();
        var stagingId = Guid.NewGuid();
        var intake = new MigrationIntakeEntity
        {
            Id = intakeId,
            IntakeId = "mig_test",
        };
        var revision = new MigrationPackageRevisionEntity
        {
            Id = revisionId,
            MigrationIntakeEntityId = intakeId,
            PackageRevisionId = operatorAttested ? "mpr_preview" : "mpr_final",
            Purpose = operatorAttested ? "preview" : "final",
            Status = "package-validated",
            CaptureKind = operatorAttested ? "preview" : "final",
            SourceFrozen = !operatorAttested,
            RehearsalOnly = operatorAttested,
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = candidateId,
            CandidateArtifactId = "mca_test",
        };
        var staging = new MigrationStagingRunEntity
        {
            Id = stagingId,
            StagingRunId = "mstg_test",
        };
        var plan = new MigrationProductionAdoptionEntity
        {
            MigrationIntakeEntityId = intakeId,
            MigrationPackageRevisionEntityId = revisionId,
            MigrationCandidateArtifactEntityId = candidateId,
            MigrationStagingRunEntityId = stagingId,
            PackageRevision = revision,
            CandidateArtifact = candidate,
            StagingRun = staging,
        };
        intake.ProductionAdoption = plan;

        MigrationProductionAuthorityEntity? authority = null;
        if (operatorAttested)
        {
            authority = new MigrationProductionAuthorityEntity
            {
                MigrationIntakeEntityId = intakeId,
                MigrationPackageRevisionEntityId = revisionId,
                MigrationCandidateArtifactEntityId = candidateId,
                MigrationStagingRunEntityId = stagingId,
                ProductionAuthorityId = "mpauth_test",
                AuthorityType = MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
                Status = MigrationProductionAuthorityStatuses.Active,
                ActiveMigrationKey = intake.IntakeId,
                PackageRevision = revision,
                CaptureKind = "preview",
                SourceFrozen = false,
                RehearsalOnly = true,
                EvidenceSha256 = new string('a', 64),
            };
            intake.ProductionAuthorities.Add(authority);
        }

        return new Fixture(intake, plan, authority);
    }

    private sealed record Fixture(
        MigrationIntakeEntity Intake,
        MigrationProductionAdoptionEntity Plan,
        MigrationProductionAuthorityEntity? Authority);
}
