namespace Infrastructure.Data.Entities;

public sealed class InstallationStepExecutionEntity
{
    public Guid Id { get; set; }

    public Guid InstallationId { get; set; }
    public InstallationEntity Installation { get; set; } = default!;

    public string StepName { get; set; } = default!;
    public int Sequence { get; set; }

    public string Status { get; set; } = default!; // Pending, Running, Succeeded, Failed, Skipped

    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }

    public int AttemptCount { get; set; }

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}