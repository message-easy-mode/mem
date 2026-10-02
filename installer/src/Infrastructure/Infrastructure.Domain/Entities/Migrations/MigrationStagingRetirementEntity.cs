namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// One durable, explicitly confirmed retirement of one Migration-owned private test.
/// ResourcePlanJson is an immutable server-authored identity snapshot, never a request body.
/// Retries and process recovery continue the same operation and cannot expand its scope.
/// </summary>
public sealed class MigrationStagingRetirementEntity
{
    public Guid Id { get; set; }
    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;
    public Guid MigrationStagingRunEntityId { get; set; }
    public MigrationStagingRunEntity StagingRun { get; set; } = default!;
    public string PrivateRuntimeStagingId { get; set; } = default!;
    public string Status { get; set; } = MigrationStagingRetirementStatuses.Queued;
    public string ResourcePlanJson { get; set; } = default!;
    public Guid RequestedByOperatorId { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public long StateVersion { get; set; } = 1;
    public string CurrentStep { get; set; } = "queued";
    public string? FailureCode { get; set; }
}

public static class MigrationStagingRetirementStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string NeedsAttention = "needs-attention";
    public const string Retired = "retired";

    public static bool BlocksWorkflow(string status) => status != Retired;
}
