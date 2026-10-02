using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeStackSecretEntityConfiguration : IEntityTypeConfiguration<RuntimeStackSecretEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeStackSecretEntity> builder)
    {
        builder.ToTable("RuntimeStackSecrets");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SecretKind)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.SecretValue)
            .IsRequired();

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.SecretKind
        }).IsUnique();

        builder.HasIndex(x => x.SecretKind);

        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.RuntimeStack)
            .WithMany()
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
