using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeRouteEntityConfiguration : IEntityTypeConfiguration<RuntimeRouteEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeRouteEntity> builder)
    {
        builder.ToTable("RuntimeRoutes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ServiceKey)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.RouteKind)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.PublicHost)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(x => x.PublicBaseUrl)
            .HasMaxLength(1000);

        builder.Property(x => x.ForwardScheme)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.ForwardHost)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(x => x.ProviderRouteId)
            .HasMaxLength(100);

        builder.Property(x => x.AdvancedConfigHash)
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.HasIndex(x => new
        {
            x.Provider,
            x.PublicHost
        }).IsUnique();

        builder.HasIndex(x => x.ProviderRouteId);

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.ServiceKey
        });

        builder.HasOne(x => x.RuntimeServiceInstance)
            .WithMany()
            .HasForeignKey(x => x.RuntimeServiceInstanceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}