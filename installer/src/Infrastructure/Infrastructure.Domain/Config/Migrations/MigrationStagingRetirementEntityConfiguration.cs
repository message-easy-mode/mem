using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationStagingRetirementEntityConfiguration : IEntityTypeConfiguration<MigrationStagingRetirementEntity>
{
    public void Configure(EntityTypeBuilder<MigrationStagingRetirementEntity> builder)
    {
        builder.ToTable("MigrationStagingRetirements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PrivateRuntimeStagingId).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.ResourcePlanJson).IsRequired();
        builder.Property(x => x.CurrentStep).IsRequired().HasMaxLength(80);
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.StateVersion).IsConcurrencyToken();
        builder.HasIndex(x => x.PrivateRuntimeStagingId).IsUnique();
        builder.HasIndex(x => x.MigrationStagingRunEntityId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.RequestedAtUtc });
        builder.HasOne(x => x.MigrationIntake).WithMany()
            .HasForeignKey(x => x.MigrationIntakeEntityId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StagingRun).WithOne(x => x.Retirement)
            .HasForeignKey<MigrationStagingRetirementEntity>(x => x.MigrationStagingRunEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
