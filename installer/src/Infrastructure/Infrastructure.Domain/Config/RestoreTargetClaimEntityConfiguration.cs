using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RestoreTargetClaimEntityConfiguration : IEntityTypeConfiguration<RestoreTargetClaimEntity>
{
    public void Configure(EntityTypeBuilder<RestoreTargetClaimEntity> builder)
    {
        builder.ToTable("RestoreTargetClaims");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ResourceType)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.ResourceValue)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(x => x.ActiveClaimKey)
            .HasMaxLength(480);

        builder.Property(x => x.ReleaseReason)
            .HasMaxLength(200);

        builder.HasIndex(x => x.ActiveClaimKey)
            .IsUnique();

        builder.HasIndex(x => x.RestoreAttemptId);
        builder.HasIndex(x => new { x.RestoreAttemptId, x.ResourceType });
    }
}
