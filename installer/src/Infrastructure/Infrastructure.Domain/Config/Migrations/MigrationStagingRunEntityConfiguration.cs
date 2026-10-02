using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationStagingRunEntityConfiguration : IEntityTypeConfiguration<MigrationStagingRunEntity>
{
    public void Configure(EntityTypeBuilder<MigrationStagingRunEntity> builder)
    {
        builder.ToTable("MigrationStagingRuns");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StagingRunId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ActiveMigrationKey).HasMaxLength(100);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.CurrentStep).IsRequired().HasMaxLength(80);
        builder.Property(x => x.PrivateRuntimeStagingId).HasMaxLength(100);
        builder.Property(x => x.WorkspacePath).HasMaxLength(1000);
        builder.Property(x => x.EvidencePath).HasMaxLength(1000);
        builder.Property(x => x.MatrixServerName).HasMaxLength(255);
        builder.Property(x => x.ElementConfigSha256).HasMaxLength(64);
        builder.Property(x => x.ElementContainerName).HasMaxLength(255);
        builder.Property(x => x.ElementContainerId).HasMaxLength(128);
        builder.Property(x => x.ElementImageReference).HasMaxLength(500);
        builder.Property(x => x.ElementImageId).HasMaxLength(128);
        builder.Property(x => x.SynapseImageReference).HasMaxLength(500);
        builder.Property(x => x.SynapseImageId).HasMaxLength(128);
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.Property(x => x.FailureSummary).HasMaxLength(500);
        builder.HasIndex(x => x.StagingRunId).IsUnique();
        builder.HasIndex(x => x.ActiveMigrationKey).IsUnique();
        builder.HasIndex(x => x.MigrationCandidateArtifactEntityId);
        builder.HasIndex(x => x.RetryOfStagingRunEntityId);
        builder.HasOne(x => x.MigrationIntake).WithMany(x => x.StagingRuns)
            .HasForeignKey(x => x.MigrationIntakeEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CandidateArtifact).WithMany()
            .HasForeignKey(x => x.MigrationCandidateArtifactEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RetryOfStagingRun).WithMany(x => x.RetryAttempts)
            .HasForeignKey(x => x.RetryOfStagingRunEntityId).OnDelete(DeleteBehavior.SetNull);
    }
}
