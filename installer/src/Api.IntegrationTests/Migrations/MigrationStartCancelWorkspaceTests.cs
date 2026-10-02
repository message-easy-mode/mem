using Infrastructure.Data.Entities.Migrations;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationStartCancelWorkspaceTests
{
    private static readonly DateTime Now = new(2026, 9, 21, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("awaiting-package", false, true, "empty-session")]
    [InlineData("expired", false, true, "empty-session")]
    [InlineData("package-validated", true, true, "package")]
    [InlineData("package-validated", false, true, "package")]
    [InlineData("awaiting-package", true, true, "package")]
    [InlineData("validating", true, false, "unavailable")]
    [InlineData("package-rejected", true, false, "unavailable")]
    public void Cancellation_confirmation_is_authored_from_recorded_evidence(
        string status, bool uploaded, bool eligible, string confirmation)
    {
        var intake = Awaiting();
        var revision = Assert.Single(intake.PackageRevisions);
        revision.Status = status;
        if (uploaded) revision.UploadedAtUtc = Now.AddMinutes(-1);

        var cancellation = MigrationSessionLifecycleService.BuildCancellation(intake, Now);
        Assert.Equal(eligible, cancellation.CanCancel);
        Assert.Equal(confirmation, cancellation.ConfirmationKind);
        Assert.Equal(7, cancellation.StateVersion);
        Assert.Equal(7, intake.StateVersion); // projection never mutates the root
    }

    [Fact]
    public void Superseded_upload_prevents_the_empty_session_confirmation()
    {
        var intake = Awaiting();
        intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            PackageRevisionId = "mpr_old", RevisionNumber = 0, Purpose = "preview",
            Status = "package-rejected", RetentionState = "retired", UploadedAtUtc = Now.AddHours(-1),
        });
        var cancellation = MigrationSessionLifecycleService.BuildCancellation(intake, Now);
        Assert.True(cancellation.CanCancel);
        Assert.Equal("package", cancellation.ConfirmationKind);
    }

    [Fact]
    public void Even_filename_only_upload_evidence_requires_the_package_confirmation()
    {
        var intake = Awaiting();
        Assert.Single(intake.PackageRevisions).PackageFileName = "partial-upload.zip.age";
        var cancellation = MigrationSessionLifecycleService.BuildCancellation(intake, Now);
        Assert.True(cancellation.CanCancel);
        Assert.Equal("package", cancellation.ConfirmationKind);
    }

    [Fact]
    public void Completed_conversion_and_candidate_offer_progressed_preproduction_cancellation()
    {
        var intake = Awaiting();
        var revision = Assert.Single(intake.PackageRevisions);
        revision.Status = "package-validated";
        revision.UploadedAtUtc = Now.AddMinutes(-4);
        intake.ConversionAttempts.Add(new MigrationConversionAttemptEntity
        {
            ConversionAttemptId = "mca_cancel_progressed",
            Status = "completed",
            CompletedAtUtc = Now.AddMinutes(-2),
            CandidateArtifact = new MigrationCandidateArtifactEntity
            {
                CandidateArtifactId = "candidate_cancel_progressed",
                RetentionState = "active",
                ArtifactPath = "/server-owned/output/candidate.sql",
            },
        });

        var cancellation = MigrationSessionLifecycleService.BuildCancellation(intake, Now);

        Assert.True(cancellation.CanCancel);
        Assert.Equal("progressed", cancellation.ConfirmationKind);
        Assert.Null(cancellation.BlockedCode);
    }

    [Theory]
    [InlineData("active-conversion", "migration_session_conversion_active")]
    [InlineData("staging", "migration_session_staging_retained")]
    [InlineData("staging-public", "migration_session_production_owned")]
    [InlineData("authority", "migration_session_production_owned")]
    [InlineData("adoption", "migration_session_production_owned")]
    [InlineData("acceptance", "migration_session_completed")]
    [InlineData("baseline", "migration_session_production_owned")]
    [InlineData("retention", "migration_session_production_owned")]
    [InlineData("qualification", "migration_session_production_owned")]
    [InlineData("completed", "migration_session_completed")]
    [InlineData("cancelled", "migration_session_lifecycle_terminal")]
    [InlineData("archived", "migration_session_archived")]
    public void Guided_capability_cannot_offer_cancellation_across_an_owned_boundary(string boundary, string code)
    {
        var intake = Awaiting();
        switch (boundary)
        {
            case "active-conversion": intake.ConversionAttempts.Add(new MigrationConversionAttemptEntity
                { ActiveMigrationKey = intake.IntakeId, Status = "running" }); break;
            case "staging": intake.StagingRuns.Add(new MigrationStagingRunEntity { ActiveMigrationKey = intake.IntakeId }); break;
            case "staging-public": intake.StagingRuns.Add(new MigrationStagingRunEntity { PublicRoutesCreated = true }); break;
            case "authority": intake.ProductionAuthorities.Add(new MigrationProductionAuthorityEntity()); break;
            case "adoption": intake.ProductionAdoption = new MigrationProductionAdoptionEntity(); break;
            case "acceptance": intake.Acceptance = new MigrationAcceptanceEntity(); break;
            case "baseline": intake.BaselineBackupHandoff = new MigrationBaselineBackupHandoffEntity { Status = "pending" }; break;
            case "retention": intake.LegacyRetentionRecord = new LegacyRetentionRecordEntity(); break;
            case "qualification": intake.TwoServerQualification = new MigrationTwoServerQualificationEntity(); break;
            case "completed": intake.LifecycleStatus = "completed"; break;
            case "cancelled": intake.LifecycleStatus = "cancelled"; intake.CancelledAtUtc = Now; break;
            case "archived": intake.ArchivedAtUtc = Now; break;
        }
        var capability = MigrationSessionLifecycleService.BuildCancellation(intake, Now);
        var lifecycle = MigrationSessionLifecycleService.BuildInspection(intake, Now,
            new MigrationSessionDeletionDecision(false, "not-inspected", "Not part of this test.", null));
        Assert.False(capability.CanCancel);
        Assert.Equal("unavailable", capability.ConfirmationKind);
        Assert.Equal(code, capability.BlockedCode);
        Assert.Equal(lifecycle.Capabilities.CanCancel, capability.CanCancel);
        Assert.Equal(lifecycle.Capabilities.CancelBlockedCode, capability.BlockedCode);
        Assert.Equal(lifecycle.Capabilities.CancelBlockedReason, capability.BlockedReason);
    }

    private static MigrationIntakeEntity Awaiting() => new()
    {
        IntakeId = "mig_cancel_policy", DisplayName = "Cancel policy", StateVersion = 7,
        PackageRevisions = [new MigrationPackageRevisionEntity
        {
            PackageRevisionId = "mpr_cancel_policy", RevisionNumber = 1, Purpose = "preview",
            Status = "awaiting-package", RetentionState = "active", ActivePurposeKey = "mig_cancel_policy:preview",
            CreatedAtUtc = Now.AddHours(-1), ExpiresAtUtc = Now.AddHours(1),
        }],
    };
}
