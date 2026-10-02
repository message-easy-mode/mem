using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemBootstrapGrantEntityConfiguration
    : IEntityTypeConfiguration<MemBootstrapGrantEntity>
{
    public void Configure(EntityTypeBuilder<MemBootstrapGrantEntity> builder)
    {
        builder.ToTable("MemBootstrapGrants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.PendingOperatorId)
            .IsRequired(false);

        builder.HasIndex(x => x.ExpiresAtUtc);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.PendingOperatorId);
    }
}
