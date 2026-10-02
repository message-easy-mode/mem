using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class DomainCertificateRenewalPolicyEntityConfiguration : IEntityTypeConfiguration<DomainCertificateRenewalPolicyEntity>
{
    public void Configure(EntityTypeBuilder<DomainCertificateRenewalPolicyEntity> builder)
    {
        builder.ToTable("DomainCertificateRenewalPolicies");

        builder.HasKey(x => x.DomainId);

        builder.Property(x => x.AcmeEmail)
            .HasMaxLength(320);

        builder.Property(x => x.RenewalWindowDays)
            .IsRequired();

        builder.Property(x => x.RetryIntervalHours)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.HasOne(x => x.Domain)
            .WithOne(x => x.CertificateRenewalPolicy)
            .HasForeignKey<DomainCertificateRenewalPolicyEntity>(x => x.DomainId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
