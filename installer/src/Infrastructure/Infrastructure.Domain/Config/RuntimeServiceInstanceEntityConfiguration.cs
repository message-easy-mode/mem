using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeServiceInstanceEntityConfiguration : IEntityTypeConfiguration<RuntimeServiceInstanceEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeServiceInstanceEntity> builder)
    {
        builder.ToTable("RuntimeServiceInstances");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ServiceKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Role)
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.Image)
            .HasMaxLength(300);

        builder.Property(x => x.Version)
            .HasMaxLength(100);

        builder.Property(x => x.ContainerId)
            .HasMaxLength(200);

        builder.Property(x => x.ContainerName)
            .HasMaxLength(200);

        builder.Property(x => x.NetworkName)
            .HasMaxLength(200);

        builder.Property(x => x.InternalHost)
            .HasMaxLength(253);

        builder.Property(x => x.InternalBaseUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.PublicHost)
            .HasMaxLength(253);

        builder.Property(x => x.PublicBaseUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.DataPath)
            .HasMaxLength(1000);

        builder.Property(x => x.ConfigPath)
            .HasMaxLength(1000);

        builder.Property(x => x.ServerName)
            .HasMaxLength(253);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.ServiceKey
        });

        builder.HasIndex(x => x.InstanceId);

        builder.HasIndex(x => x.ContainerName);
    }
}