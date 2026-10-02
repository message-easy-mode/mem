namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Durable root for one Migration Session. Package transfer, validation and
/// source archive evidence are owned exclusively by MigrationPackageRevisionEntity.
/// </summary>
public sealed class MigrationIntakeEntity
{
    public Guid Id { get; set; }
    public string IntakeId { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public string LifecycleStatus { get; set; } = MigrationSessionLifecycleStatuses.Active;
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosureKind { get; set; }
    public DateTime? ArchivedAtUtc { get; set; }
    public string? ArchivedBy { get; set; }
    public long StateVersion { get; set; } = 1;
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledBy { get; set; }
    public ICollection<MigrationSourceEntity> Sources { get; set; } = new List<MigrationSourceEntity>();
    public ICollection<MigrationPackageRevisionEntity> PackageRevisions { get; set; } =
        new List<MigrationPackageRevisionEntity>();
    public ICollection<MigrationConversionAttemptEntity> ConversionAttempts { get; set; } =
        new List<MigrationConversionAttemptEntity>();
    public ICollection<MigrationStagingRunEntity> StagingRuns { get; set; } =
        new List<MigrationStagingRunEntity>();
    public ICollection<MigrationProductionAuthorityEntity> ProductionAuthorities { get; set; } =
        new List<MigrationProductionAuthorityEntity>();
    public MigrationProductionAdoptionEntity? ProductionAdoption { get; set; }
    public MigrationAcceptanceEntity? Acceptance { get; set; }
    public LegacyRetentionRecordEntity? LegacyRetentionRecord { get; set; }
    public MigrationBaselineBackupHandoffEntity? BaselineBackupHandoff { get; set; }
    public MigrationTwoServerQualificationEntity? TwoServerQualification { get; set; }
}

public static class MigrationSessionLifecycleStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Active,
            Completed,
            Closed,
            Cancelled,
        };
}
