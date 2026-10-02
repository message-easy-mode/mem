using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class InstallationEntityConfiguration : IEntityTypeConfiguration<InstallationEntity>
{
    public void Configure(EntityTypeBuilder<InstallationEntity> builder)
    {
        builder.ToTable("Installations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.ConfigJson);

        builder.Property(x => x.FrozenConfigJson);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.Property(x => x.StartedAtUtc);

        builder.Property(x => x.CompletedAtUtc);

        builder.HasMany(x => x.Steps)
            .WithOne(x => x.Installation)
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Secrets)
            .WithOne(x => x.Installation)
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}