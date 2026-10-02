using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class DomainEntityConfiguration : IEntityTypeConfiguration<DomainEntity>
{
    public void Configure(EntityTypeBuilder<DomainEntity> builder)
    {
        builder.ToTable("Domains");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.BaseDomain)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Purpose)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.DnsProvider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.DnsZone)
            .HasMaxLength(253);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Notes)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.BaseDomain)
            .IsUnique();

        builder.HasIndex(x => x.IsMainPlatformDomain);

        builder.HasMany(x => x.Certificates)
            .WithOne(x => x.Domain)
            .HasForeignKey(x => x.DomainId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ActiveCertificate)
            .WithMany()
            .HasForeignKey(x => x.ActiveCertificateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}