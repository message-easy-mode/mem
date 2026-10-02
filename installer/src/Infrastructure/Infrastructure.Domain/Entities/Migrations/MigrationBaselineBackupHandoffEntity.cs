namespace Infrastructure.Data.Entities.Migrations;

public sealed class MigrationBaselineBackupHandoffEntity
{
    public Guid Id { get; set; }
    public string HandoffId { get; set; } = default!;
    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;
    public Guid MigrationAcceptanceEntityId { get; set; }
    public MigrationAcceptanceEntity MigrationAcceptance { get; set; } = default!;
    public string Status { get; set; } = default!;
    public int AttemptCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string TargetStackSlug { get; set; } = default!;
    public string CandidateId { get; set; } = default!;
    public string PrivateRuntimeId { get; set; } = default!;
    public string? BackupId { get; set; }
    public string? CatalogEntryId { get; set; }
    public DateTime? BackupCreatedAtUtc { get; set; }
    public long? BackupTotalBytes { get; set; }
    public long? BackupTotalFiles { get; set; }
    public int? BackupWarningCount { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }
}
