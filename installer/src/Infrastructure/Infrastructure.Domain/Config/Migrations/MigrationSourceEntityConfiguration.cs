using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationSourceEntityConfiguration : IEntityTypeConfiguration<MigrationSourceEntity>
{
    public void Configure(EntityTypeBuilder<MigrationSourceEntity> builder)
    {
        builder.ToTable("MigrationSources"); builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceId).IsRequired().HasMaxLength(120);
        builder.Property(x => x.SourceKind).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Product).IsRequired().HasMaxLength(120);
        builder.Property(x => x.ProductVersion).HasMaxLength(80);
        builder.Property(x => x.SourceFingerprint).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => new { x.MigrationIntakeEntityId, x.SourceId }).IsUnique();
        builder.HasOne(x => x.MigrationIntake).WithMany(x => x.Sources).HasForeignKey(x => x.MigrationIntakeEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}
