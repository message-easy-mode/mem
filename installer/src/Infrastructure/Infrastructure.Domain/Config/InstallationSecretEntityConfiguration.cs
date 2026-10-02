using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class InstallationSecretEntityConfiguration
    : IEntityTypeConfiguration<InstallationSecretEntity>
{
    public void Configure(EntityTypeBuilder<InstallationSecretEntity> builder)
    {
        builder.ToTable("InstallationSecrets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Value)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.InstallationId, x.Key })
            .IsUnique();

        builder.HasOne(x => x.Installation)
            .WithMany(x => x.Secrets)
            .HasForeignKey(x => x.InstallationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}