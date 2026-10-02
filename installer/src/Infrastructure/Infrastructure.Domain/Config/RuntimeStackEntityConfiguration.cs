using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeStackEntityConfiguration : IEntityTypeConfiguration<RuntimeStackEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeStackEntity> builder)
    {
        builder.ToTable("RuntimeStacks");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Slug)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(x => x.DisplayName)
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.LastVerifiedStatus)
            .HasMaxLength(80);

        builder.Property(x => x.BaseDomain)
            .HasMaxLength(253);

        builder.Property(x => x.RuntimeNetworkName)
            .HasMaxLength(200);

        builder.Property(x => x.DataRoot)
            .HasMaxLength(1000);

        builder.Property(x => x.ManifestPath)
            .HasMaxLength(1000);

        builder.Property(x => x.MatrixPublicBaseUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.ElementPublicBaseUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.LastVerifiedStatus);

        builder.HasMany(x => x.ServiceInstances)
            .WithOne(x => x.RuntimeStack)
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Routes)
            .WithOne(x => x.RuntimeStack)
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}