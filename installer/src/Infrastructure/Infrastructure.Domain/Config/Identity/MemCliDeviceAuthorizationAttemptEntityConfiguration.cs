using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemCliDeviceAuthorizationAttemptEntityConfiguration
    : IEntityTypeConfiguration<MemCliDeviceAuthorizationAttemptEntity>
{
    public void Configure(EntityTypeBuilder<MemCliDeviceAuthorizationAttemptEntity> builder)
    {
        builder.ToTable("MemCliDeviceAuthorizationAttempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.InstallationId)
            .IsRequired();

        builder.Property(x => x.UserCodeHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.VerifierHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.DeviceLabel)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(32)
            .IsConcurrencyToken();

        builder.HasIndex(x => x.UserCodeHash)
            .IsUnique();

        builder.HasIndex(x => new { x.InstallationId, x.Status });

        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
