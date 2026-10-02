namespace Infrastructure.Data.Entities.Migrations;

public sealed class MigrationAcceptanceEntity
{
    public Guid Id { get; set; }
    public string AcceptanceId { get; set; } = default!;
    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;
    public string ExecutionId { get; set; } = default!;
    public string CandidateArtifactId { get; set; } = default!;
    public string StagingRunId { get; set; } = default!;
    public string PublicVerificationStatus { get; set; } = default!;
    public string PublicVerificationEvidenceJson { get; set; } = default!;
    public string PublicVerificationEvidenceSha256 { get; set; } = default!;
    public DateTime PublicCutoverAtUtc { get; set; }
    public DateTime AcceptedAtUtc { get; set; }
    public string AcceptedBy { get; set; } = default!;
    public string? Note { get; set; }
    public bool FreshPublicVerificationAcknowledged { get; set; }
    public bool TargetWriteDivergenceAcknowledged { get; set; }
    public bool RollbackBoundaryAcknowledged { get; set; }
    public bool LegacyRetentionAcknowledged { get; set; }
    public bool NoAutomaticLegacyDeletionAcknowledged { get; set; }
    public LegacyRetentionRecordEntity LegacyRetentionRecord { get; set; } = default!;
    public MigrationBaselineBackupHandoffEntity? BaselineBackupHandoff { get; set; }
}
