using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeStackSecretEntity
{
    public Guid Id { get; set; }

    public Guid RuntimeStackId { get; set; }
    public RuntimeStackEntity RuntimeStack { get; set; } = default!;

    public string SecretKind { get; set; } = default!;
    public string SecretValue { get; set; } = default!;

    public string Status { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? RotatedAtUtc { get; set; }

    public string? MetadataJson { get; set; }
}
