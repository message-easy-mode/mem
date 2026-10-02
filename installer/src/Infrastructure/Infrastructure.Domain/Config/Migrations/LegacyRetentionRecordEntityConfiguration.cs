using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class LegacyRetentionRecordEntityConfiguration : IEntityTypeConfiguration<LegacyRetentionRecordEntity>
{
    public void Configure(EntityTypeBuilder<LegacyRetentionRecordEntity> builder)
    {
        builder.ToTable("LegacyRetentionRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RetentionRecordId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.SourceMigrationId).IsRequired().HasMaxLength(120);
        builder.Property(x => x.SourceProduct).IsRequired().HasMaxLength(120);
        builder.Property(x => x.SourceVersion).HasMaxLength(80);
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(1000);
        builder.HasIndex(x => x.RetentionRecordId).IsUnique();
        builder.HasIndex(x => x.MigrationIntakeEntityId).IsUnique();
        builder.HasIndex(x => x.MigrationAcceptanceEntityId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CleanupEligibleAtUtc);
        builder.HasOne(x => x.MigrationIntake).WithOne(x => x.LegacyRetentionRecord)
            .HasForeignKey<LegacyRetentionRecordEntity>(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.MigrationAcceptance).WithOne(x => x.LegacyRetentionRecord)
            .HasForeignKey<LegacyRetentionRecordEntity>(x => x.MigrationAcceptanceEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
