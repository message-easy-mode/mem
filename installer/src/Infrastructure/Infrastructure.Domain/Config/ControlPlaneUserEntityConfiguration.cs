using Infrastructure.Data.Entities.ControlPlane;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.ControlPlane;

public sealed class ControlPlaneUserEntityConfiguration : IEntityTypeConfiguration<ControlPlaneUserEntity>
{
    public void Configure(EntityTypeBuilder<ControlPlaneUserEntity> builder)
    {
        builder.ToTable("ControlPlaneUsers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Username)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.NormalizedUsername)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Email)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.Property(x => x.NormalizedEmail)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.Property(x => x.PasswordHash)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.NormalizedUsername)
            .IsUnique();

        builder.HasIndex(x => x.NormalizedEmail)
            .IsUnique();

        builder.HasMany(x => x.Roles)
            .WithOne(x => x.ControlPlaneUser)
            .HasForeignKey(x => x.ControlPlaneUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
