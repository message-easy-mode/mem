using System;

namespace Infrastructure.Data.Entities;

public sealed class InstallationSecretEntity
{
    public Guid Id { get; set; }

    public Guid InstallationId { get; set; }
    public InstallationEntity Installation { get; set; } = default!;

    public string Category { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}