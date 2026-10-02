using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemOperatorEnrollmentGrantEntityConfiguration
    : IEntityTypeConfiguration<MemOperatorEnrollmentGrantEntity>
{
    public void Configure(EntityTypeBuilder<MemOperatorEnrollmentGrantEntity> builder)
    {
        builder.ToTable("MemOperatorEnrollmentGrants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.OperatorId)
            .IsRequired();

        builder.Property(x => x.CodeHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(32)
            .IsConcurrencyToken();

        builder.HasIndex(x => x.CodeHash)
            .IsUnique();

        builder.HasIndex(x => new { x.OperatorId, x.Status });

        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
