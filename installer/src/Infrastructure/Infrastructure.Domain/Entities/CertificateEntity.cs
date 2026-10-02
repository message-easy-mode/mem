using System;

namespace Infrastructure.Data.Entities;

public sealed class CertificateEntity
{
    public Guid Id { get; set; }

    public Guid DomainId { get; set; }
    public DomainEntity Domain { get; set; } = default!;

    public string CertificateId { get; set; } = default!;
    public string CommonName { get; set; } = default!;

    public string Provider { get; set; } = default!;
    public bool IsWildcard { get; set; }
    public bool IsStaging { get; set; }

    public bool IsMainPlatformCertificate { get; set; }
    public bool IsActive { get; set; }

    public string Status { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }

    public string? FullchainPath { get; set; }
    public string? PrivateKeyPath { get; set; }
    public string? Thumbprint { get; set; }

    public int? NpmCertificateId { get; set; }
    public bool ImportedToNpm { get; set; }

    public DateTime? LastValidatedAtUtc { get; set; }
    public DateTime? LastImportedToNpmAtUtc { get; set; }

    public string? LastError { get; set; }
}