using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationProductionAdoptionEntityConfiguration : IEntityTypeConfiguration<MigrationProductionAdoptionEntity>
{
    public void Configure(EntityTypeBuilder<MigrationProductionAdoptionEntity> builder)
    {
        builder.ToTable("MigrationProductionAdoptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AdoptionPlanId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.PlanSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.TargetStackSlug).IsRequired().HasMaxLength(150);
        builder.Property(x => x.TargetDisplayName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.MatrixServerName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.MatrixPublicHost).IsRequired().HasMaxLength(255);
        builder.Property(x => x.MatrixPublicBaseUrl).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.ElementPublicHost).IsRequired().HasMaxLength(255);
        builder.Property(x => x.ElementPublicBaseUrl).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.RuntimeNetworkName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.RuntimeDataRoot).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.ManifestPath).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.MatrixContainerName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.MatrixDataPath).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.ElementContainerName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.ElementDataPath).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.MatrixImageReference).IsRequired().HasMaxLength(500);
        builder.Property(x => x.MatrixImageId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.ElementImageReference).IsRequired().HasMaxLength(500);
        builder.Property(x => x.ElementImageId).IsRequired().HasMaxLength(128);
        builder.Property(x => x.DatabaseEngine).IsRequired().HasMaxLength(80);
        builder.Property(x => x.DatabaseHost).IsRequired().HasMaxLength(253);
        builder.Property(x => x.DatabaseName).IsRequired().HasMaxLength(120);
        builder.Property(x => x.DatabaseUsername).IsRequired().HasMaxLength(120);
        builder.Property(x => x.DatabasePasswordSecretKind).IsRequired().HasMaxLength(120);
        builder.Property(x => x.RoutePlanJson).IsRequired();
        builder.Property(x => x.ProvenanceJson).IsRequired();
        builder.Property(x => x.CollisionEvidenceJson).IsRequired();
        builder.Property(x => x.BlockerSummary).HasMaxLength(1000);
        builder.Property(x => x.MaterializationId).HasMaxLength(100);
        builder.Property(x => x.MaterializationStatus).HasMaxLength(60);
        builder.Property(x => x.MaterializationEvidenceJson);
        builder.Property(x => x.MaterializationFailureCode).HasMaxLength(160);
        builder.Property(x => x.MaterializationFailureSummary).HasMaxLength(2000);
        builder.Property(x => x.CutoverPreviewId).HasMaxLength(100);
        builder.Property(x => x.CutoverPreviewStatus).HasMaxLength(60);
        builder.Property(x => x.CutoverPreviewSha256).HasMaxLength(64);
        builder.Property(x => x.CutoverPreviewJson);
        builder.Property(x => x.CutoverExecutionId).HasMaxLength(100);
        builder.Property(x => x.CutoverStatus).HasMaxLength(60);
        builder.Property(x => x.MatrixNpmRouteId).HasMaxLength(100);
        builder.Property(x => x.ElementNpmRouteId).HasMaxLength(100);
        builder.Property(x => x.CutoverEvidenceJson);
        builder.Property(x => x.CutoverRollbackCheckpointJson);
        builder.Property(x => x.CutoverFailureCode).HasMaxLength(160);
        builder.Property(x => x.CutoverFailureSummary).HasMaxLength(2000);
        builder.Property(x => x.ProductionVerificationId).HasMaxLength(100);
        builder.Property(x => x.ProductionVerificationStatus).HasMaxLength(60);
        builder.Property(x => x.ProductionVerificationEvidenceSha256).HasMaxLength(64);
        builder.Property(x => x.ProductionVerificationEvidenceJson);
        builder.Property(x => x.ProductionVerificationFailureCode).HasMaxLength(160);
        builder.Property(x => x.ProductionVerificationFailureSummary).HasMaxLength(2000);
        builder.Property(x => x.RollbackPreviewId).HasMaxLength(100);
        builder.Property(x => x.RollbackPreviewStatus).HasMaxLength(60);
        builder.Property(x => x.RollbackPreviewSha256).HasMaxLength(64);
        builder.Property(x => x.RollbackPreviewJson);
        builder.Property(x => x.RollbackExecutionId).HasMaxLength(100);
        builder.Property(x => x.RollbackStatus).HasMaxLength(60);
        builder.Property(x => x.RollbackSourceHandoffId).HasMaxLength(100);
        builder.Property(x => x.RollbackSourceHandoffSha256).HasMaxLength(64);
        builder.Property(x => x.RollbackSourceHandoffJson);
        builder.Property(x => x.RollbackEvidenceJson);
        builder.Property(x => x.RollbackFailureCode).HasMaxLength(160);
        builder.Property(x => x.RollbackFailureSummary).HasMaxLength(2000);
        builder.Property(x => x.RollbackCompletionStatus).HasMaxLength(60);
        builder.Property(x => x.RollbackSourceCompletionAttemptId).HasMaxLength(100);
        builder.Property(x => x.RollbackSourceCompletionSha256).HasMaxLength(64);
        builder.Property(x => x.RollbackSourceCompletionJson);

        builder.HasIndex(x => x.AdoptionPlanId).IsUnique();
        builder.HasIndex(x => x.MigrationIntakeEntityId).IsUnique();
        builder.HasIndex(x => x.RuntimeStackId).IsUnique();
        builder.HasIndex(x => x.MatrixInstanceId).IsUnique();
        builder.HasIndex(x => x.ElementInstanceId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.TargetStackSlug);
        builder.HasIndex(x => x.MaterializationId).IsUnique();
        builder.HasIndex(x => x.MaterializationStatus);
        builder.HasIndex(x => x.CutoverPreviewId).IsUnique();
        builder.HasIndex(x => x.CutoverExecutionId).IsUnique();
        builder.HasIndex(x => x.CutoverStatus);
        builder.HasIndex(x => x.ProductionVerificationId).IsUnique();
        builder.HasIndex(x => x.ProductionVerificationStatus);
        builder.HasIndex(x => x.RollbackPreviewId).IsUnique();
        builder.HasIndex(x => x.RollbackExecutionId).IsUnique();
        builder.HasIndex(x => x.RollbackSourceHandoffId).IsUnique();
        builder.HasIndex(x => x.RollbackStatus);
        builder.HasIndex(x => x.RollbackSourceCompletionAttemptId).IsUnique();
        builder.HasIndex(x => x.RollbackCompletionStatus);

        builder.HasOne(x => x.MigrationIntake)
            .WithOne(x => x.ProductionAdoption)
            .HasForeignKey<MigrationProductionAdoptionEntity>(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PackageRevision)
            .WithMany()
            .HasForeignKey(x => x.MigrationPackageRevisionEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CandidateArtifact)
            .WithMany()
            .HasForeignKey(x => x.MigrationCandidateArtifactEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StagingRun)
            .WithMany()
            .HasForeignKey(x => x.MigrationStagingRunEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
