using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class DomainSecretEntityConfiguration : IEntityTypeConfiguration<DomainSecretEntity>
{
    public void Configure(EntityTypeBuilder<DomainSecretEntity> builder)
    {
        builder.ToTable("DomainSecrets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.ProtectedValue)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => new { x.DomainId, x.Key })
            .IsUnique();

        builder.HasOne(x => x.Domain)
            .WithMany(x => x.Secrets)
            .HasForeignKey(x => x.DomainId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
