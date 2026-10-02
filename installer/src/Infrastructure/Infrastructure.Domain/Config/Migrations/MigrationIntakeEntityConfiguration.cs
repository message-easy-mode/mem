using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationIntakeEntityConfiguration : IEntityTypeConfiguration<MigrationIntakeEntity>
{
    public void Configure(EntityTypeBuilder<MigrationIntakeEntity> builder)
    {
        builder.ToTable("MigrationIntakes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IntakeId).IsRequired().HasMaxLength(80);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(300);
        builder.Property(x => x.LifecycleStatus)
            .IsRequired()
            .HasMaxLength(40)
            .HasDefaultValue(MigrationSessionLifecycleStatuses.Active);
        builder.Property(x => x.ClosureKind).HasMaxLength(80);
        builder.Property(x => x.ArchivedBy).HasMaxLength(200);
        builder.Property(x => x.CancelledBy).HasMaxLength(200);
        builder.Property(x => x.StateVersion)
            .IsConcurrencyToken()
            .HasDefaultValue(1L);
        builder.HasIndex(x => x.IntakeId).IsUnique();
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.UpdatedAtUtc);
        builder.HasIndex(x => new { x.LifecycleStatus, x.UpdatedAtUtc });
        builder.HasIndex(x => new { x.ArchivedAtUtc, x.UpdatedAtUtc });
        builder.HasIndex(x => x.DisplayName);
    }
}
