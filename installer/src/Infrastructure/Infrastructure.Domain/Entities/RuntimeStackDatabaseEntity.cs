using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeStackDatabaseEntity
{
    public Guid Id { get; set; }

    public Guid RuntimeStackId { get; set; }
    public RuntimeStackEntity RuntimeStack { get; set; } = default!;

    public string DatabaseEngine { get; set; } = default!;
    public string DatabaseHost { get; set; } = default!;
    public int DatabasePort { get; set; }

    public string DatabaseName { get; set; } = default!;
    public string DatabaseUsername { get; set; } = default!;
    public string PasswordSecretKind { get; set; } = default!;

    public string Status { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string? MetadataJson { get; set; }
}