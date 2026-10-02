using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemOperatorStepUpGrantEntityConfiguration
    : IEntityTypeConfiguration<MemOperatorStepUpGrantEntity>
{
    public void Configure(EntityTypeBuilder<MemOperatorStepUpGrantEntity> builder)
    {
        builder.ToTable("MemOperatorStepUpGrants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.OperatorId)
            .IsRequired();

        builder.Property(x => x.SessionId)
            .IsRequired();

        builder.Property(x => x.SecurityStamp)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.IssuedAtUtc)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.RevokedAtUtc)
            .IsRequired(false);

        builder.Property(x => x.RevokedReasonCode)
            .IsRequired(false)
            .HasMaxLength(100);

        // One mutable current grant is retained for each exact authenticated
        // browser session. Historical security evidence belongs in the
        // structured audit table rather than this capability record.
        builder.HasIndex(x => new { x.OperatorId, x.SessionId })
            .IsUnique();

        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasIndex(x => new { x.OperatorId, x.RevokedAtUtc });
    }
}
