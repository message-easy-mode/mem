using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationBaselineBackupHandoffEntityConfiguration : IEntityTypeConfiguration<MigrationBaselineBackupHandoffEntity>
{
    public void Configure(EntityTypeBuilder<MigrationBaselineBackupHandoffEntity> builder)
    {
        builder.ToTable("MigrationBaselineBackupHandoffs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.HandoffId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.TargetStackSlug).IsRequired().HasMaxLength(150);
        builder.Property(x => x.CandidateId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.PrivateRuntimeId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.BackupId).HasMaxLength(100);
        builder.Property(x => x.CatalogEntryId).HasMaxLength(100);
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.Property(x => x.FailureSummary).HasMaxLength(1000);
        builder.HasIndex(x => x.HandoffId).IsUnique();
        builder.HasIndex(x => x.MigrationIntakeEntityId).IsUnique();
        builder.HasIndex(x => x.MigrationAcceptanceEntityId).IsUnique();
        builder.HasIndex(x => x.CatalogEntryId).IsUnique();
        builder.HasOne(x => x.MigrationIntake).WithOne(x => x.BaselineBackupHandoff)
            .HasForeignKey<MigrationBaselineBackupHandoffEntity>(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MigrationAcceptance).WithOne(x => x.BaselineBackupHandoff)
            .HasForeignKey<MigrationBaselineBackupHandoffEntity>(x => x.MigrationAcceptanceEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
