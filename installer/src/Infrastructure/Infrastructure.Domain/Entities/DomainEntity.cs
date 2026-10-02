using System;

namespace Infrastructure.Data.Entities;

public sealed class DomainEntity
{
    public Guid Id { get; set; }

    public string BaseDomain { get; set; } = default!;
    public string DisplayName { get; set; } = default!;

    public string Purpose { get; set; } = default!;
    public bool IsMainPlatformDomain { get; set; }

    public string DnsProvider { get; set; } = default!;
    public string? DnsZone { get; set; }

    public string Status { get; set; } = default!;

    public Guid? ActiveCertificateId { get; set; }
    public CertificateEntity? ActiveCertificate { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<CertificateEntity> Certificates { get; set; } =
        new List<CertificateEntity>();

    public ICollection<DomainSecretEntity> Secrets { get; set; } =
        new List<DomainSecretEntity>();

    public DomainCertificateRenewalPolicyEntity? CertificateRenewalPolicy { get; set; }
}