namespace Infrastructure.Data.Entities.Migrations;

public sealed class LegacyRetentionRecordEntity
{
    public Guid Id { get; set; }
    public string RetentionRecordId { get; set; } = default!;
    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;
    public Guid MigrationAcceptanceEntityId { get; set; }
    public MigrationAcceptanceEntity MigrationAcceptance { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public DateTime CleanupEligibleAtUtc { get; set; }
    public DateTime? CleanupCompletedAtUtc { get; set; }
    public string SourceMigrationId { get; set; } = default!;
    public string SourceProduct { get; set; } = default!;
    public string? SourceVersion { get; set; }
    public bool SourcePackageRetained { get; set; }
    public bool CandidateArtifactRetained { get; set; }
    public bool PrivateStagingEvidenceRetained { get; set; }
    public bool LegacySourceResourcesRetained { get; set; }
    public bool AutomaticDeletionAllowed { get; set; }
    public string Summary { get; set; } = default!;
}
