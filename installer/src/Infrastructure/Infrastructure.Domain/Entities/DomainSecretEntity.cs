using System;

namespace Infrastructure.Data.Entities;

public sealed class DomainSecretEntity
{
    public Guid Id { get; set; }

    public Guid DomainId { get; set; }
    public DomainEntity Domain { get; set; } = default!;

    public string Category { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string ProtectedValue { get; set; } = default!;
    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
