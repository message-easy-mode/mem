using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class CertificateEntityConfiguration : IEntityTypeConfiguration<CertificateEntity>
{
    public void Configure(EntityTypeBuilder<CertificateEntity> builder)
    {
        builder.ToTable("Certificates");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CertificateId)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.CommonName)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(x => x.Provider)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.FullchainPath)
            .HasMaxLength(1000);

        builder.Property(x => x.PrivateKeyPath)
            .HasMaxLength(1000);

        builder.Property(x => x.Thumbprint)
            .HasMaxLength(200);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.HasIndex(x => x.CertificateId)
            .IsUnique();

        builder.HasIndex(x => new { x.DomainId, x.IsActive });

        builder.HasIndex(x => x.IsMainPlatformCertificate);
    }
}