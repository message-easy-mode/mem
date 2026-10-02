using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeServiceEntityConfiguration : IEntityTypeConfiguration<RuntimeServiceEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeServiceEntity> builder)
    {
        builder.ToTable("RuntimeServices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ServiceName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ContainerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Image)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.HostPath)
            .HasMaxLength(1000);

        builder.Property(x => x.SecondaryHostPath)
            .HasMaxLength(1000);

        builder.Property(x => x.ContainerId)
            .HasMaxLength(200);

        builder.HasIndex(x => x.ServiceName)
            .IsUnique();
    }
}