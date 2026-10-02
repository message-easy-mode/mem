namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Durable coordination and evidence record for one source-package conversion attempt.
/// The attempt belongs to Migration and must never be represented as a normal Restore Session.
/// Filesystem paths remain trusted server-side implementation details and are not browser contracts.
/// </summary>
public sealed class MigrationConversionAttemptEntity
{
    public Guid Id { get; set; }

    public string ConversionAttemptId { get; set; } = default!;

    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;

    /// <summary>
    /// Optional compatibility-safe link to the immutable package revision converted by this
    /// attempt. Historical attempts created before MIG-FINAL-01A may remain unlinked until the
    /// package-revision migration backfill is applied.
    /// </summary>
    public Guid? MigrationPackageRevisionEntityId { get; set; }
    public MigrationPackageRevisionEntity? PackageRevision { get; set; }

    /// <summary>
    /// Equals the durable migration intake ID while this attempt is active and is cleared when
    /// the attempt reaches a terminal state. A unique nullable index provides one-active-
    /// conversion-attempt-per-migration semantics without deleting historical attempts.
    /// </summary>
    public string? ActiveMigrationKey { get; set; }

    public string SourcePackageSha256 { get; set; } = default!;
    public string SourceAdapterId { get; set; } = default!;
    public string SourceAdapterVersion { get; set; } = default!;
    public string ConverterId { get; set; } = default!;
    public string ConverterVersion { get; set; } = default!;

    public string Status { get; set; } = default!;
    public string CurrentStep { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public string? ResultCode { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureSummary { get; set; }

    public string? WorkspacePath { get; set; }
    public string? EvidenceDirectoryPath { get; set; }
    public string? LogDirectoryPath { get; set; }
    public string? CompletionReportPath { get; set; }

    /// <summary>
    /// Optional link to the failed or superseded attempt that this attempt retries. Retry history
    /// is retained rather than overwritten.
    /// </summary>
    public Guid? RetryOfConversionAttemptEntityId { get; set; }
    public MigrationConversionAttemptEntity? RetryOfConversionAttempt { get; set; }
    public ICollection<MigrationConversionAttemptEntity> RetryAttempts { get; set; } =
        new List<MigrationConversionAttemptEntity>();

    public MigrationCandidateArtifactEntity? CandidateArtifact { get; set; }
}
