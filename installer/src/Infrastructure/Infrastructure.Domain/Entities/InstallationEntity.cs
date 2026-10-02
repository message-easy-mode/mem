namespace Infrastructure.Data.Entities;

public sealed class InstallationEntity
{
    public Guid Id { get; set; }

    public string Status { get; set; } = default!; // Draft, Ready, Running, Succeeded, Failed, Cancelled

    public string? ConfigJson { get; set; }
    public string? FrozenConfigJson { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public ICollection<InstallationStepExecutionEntity> Steps { get; set; } =
        new List<InstallationStepExecutionEntity>();
    
    public ICollection<InstallationSecretEntity> Secrets { get; set; } =
        new List<InstallationSecretEntity>();
}