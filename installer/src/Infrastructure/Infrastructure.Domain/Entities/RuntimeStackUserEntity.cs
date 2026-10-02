using System;

namespace Infrastructure.Data.Entities;

public sealed class RuntimeStackUserEntity
{
    public Guid Id { get; set; }

    public Guid RuntimeStackId { get; set; }
    public RuntimeStackEntity RuntimeStack { get; set; } = default!;

    public Guid MatrixInstanceId { get; set; }

    public string Username { get; set; } = default!;
    public string? MatrixUserId { get; set; }

    public bool IsAdmin { get; set; }
    public bool IsFirstAdmin { get; set; }

    public string Status { get; set; } = default!;

    public string? DisplayName { get; set; }
    public string? Email { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? MatrixSyncedAtUtc { get; set; }

    public string? MetadataJson { get; set; }
}
