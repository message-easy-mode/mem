using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemCliDeviceSessionEntityConfiguration
    : IEntityTypeConfiguration<MemCliDeviceSessionEntity>
{
    public void Configure(EntityTypeBuilder<MemCliDeviceSessionEntity> builder)
    {
        builder.ToTable("MemCliDeviceSessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.AuthorizationAttemptId)
            .IsRequired();

        builder.Property(x => x.InstallationId)
            .IsRequired();

        builder.Property(x => x.OperatorId)
            .IsRequired();

        builder.Property(x => x.CredentialHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.SecurityStamp)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.DeviceLabel)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.LastSeenAtUtc)
            .IsRequired();

        builder.Property(x => x.IdleExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.AbsoluteExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.RevokedReasonCode)
            .IsRequired(false)
            .HasMaxLength(100);

        builder.HasIndex(x => x.AuthorizationAttemptId)
            .IsUnique();

        builder.HasIndex(x => x.CredentialHash)
            .IsUnique();

        builder.HasIndex(x => new { x.OperatorId, x.RevokedAtUtc });

        builder.HasIndex(x => new { x.InstallationId, x.RevokedAtUtc });

        builder.HasIndex(x => x.AbsoluteExpiresAtUtc);
    }
}
