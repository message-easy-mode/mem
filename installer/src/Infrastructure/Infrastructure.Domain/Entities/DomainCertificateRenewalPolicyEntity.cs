using System;

namespace Infrastructure.Data.Entities;

public sealed class DomainCertificateRenewalPolicyEntity
{
    public Guid DomainId { get; set; }
    public DomainEntity Domain { get; set; } = default!;

    public bool AutoRenewEnabled { get; set; }
    public string? AcmeEmail { get; set; }
    public int RenewalWindowDays { get; set; }
    public int RetryIntervalHours { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
