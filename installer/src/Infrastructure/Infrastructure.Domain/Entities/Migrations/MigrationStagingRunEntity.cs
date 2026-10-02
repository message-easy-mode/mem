namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Durable Migration-owned private staging run. This is not a Restore Session and does not
/// create or depend on a Backup Catalog entry.
/// </summary>
public sealed class MigrationStagingRunEntity
{
    public MigrationStagingRetirementEntity? Retirement { get; set; }

    public Guid Id { get; set; }
    public string StagingRunId { get; set; } = default!;
    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;
    public Guid MigrationCandidateArtifactEntityId { get; set; }
    public MigrationCandidateArtifactEntity CandidateArtifact { get; set; } = default!;

    /// <summary>
    /// Equals the durable migration intake ID while this run owns a retained private runtime.
    /// It is cleared only when the runtime is destroyed or when creation failed before a
    /// destroyable runtime identity was returned. A unique nullable index enforces one retained
    /// private runtime per migration.
    /// </summary>
    public string? ActiveMigrationKey { get; set; }

    public string Status { get; set; } = default!;
    public string CurrentStep { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? DestroyedAtUtc { get; set; }
    public string? PrivateRuntimeStagingId { get; set; }
    public string? WorkspacePath { get; set; }
    public string? EvidencePath { get; set; }
    public bool PrivateOnly { get; set; }
    public bool PublicRoutesCreated { get; set; }
    public bool DatabaseImportSucceeded { get; set; }
    public bool SynapseHealthPassed { get; set; }
    public bool ElementConfigPresent { get; set; }
    public string? ElementConfigSha256 { get; set; }
    public string? ElementContainerName { get; set; }
    public string? ElementContainerId { get; set; }
    public string? ElementImageReference { get; set; }
    public string? ElementImageId { get; set; }
    public bool ElementContainerStarted { get; set; }
    public bool ElementHealthPassed { get; set; }
    public bool ElementSynapseConnectivityPassed { get; set; }
    public bool ElementNetworkAttached { get; set; }
    public string? SynapseImageReference { get; set; }
    public string? SynapseImageId { get; set; }
    public long? UsersCount { get; set; }
    public long? RoomsCount { get; set; }
    public long? EventsCount { get; set; }
    public string? MatrixServerName { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }

    /// <summary>
    /// Optional link to an eligible failed or destroyed staging run that this run retries.
    /// Retry history is retained rather than overwritten.
    /// </summary>
    public Guid? RetryOfStagingRunEntityId { get; set; }
    public MigrationStagingRunEntity? RetryOfStagingRun { get; set; }
    public ICollection<MigrationStagingRunEntity> RetryAttempts { get; set; } =
        new List<MigrationStagingRunEntity>();
}
