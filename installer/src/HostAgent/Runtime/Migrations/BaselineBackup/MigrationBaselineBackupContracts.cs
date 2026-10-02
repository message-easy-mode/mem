namespace HostAgent.Runtime.Migrations.BaselineBackup;

public sealed record MigrationBaselineBackupSummary(
    string HandoffId,
    string Status,
    int AttemptCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string TargetStackSlug,
    string CandidateId,
    string PrivateRuntimeId,
    string? BackupId,
    string? CatalogEntryId,
    DateTime? BackupCreatedAtUtc,
    long? BackupTotalBytes,
    long? BackupTotalFiles,
    int? BackupWarningCount,
    string? FailureCode,
    string? FailureSummary,
    string Detail);

public sealed record MigrationBaselineBackupStateResponse(
    string Source,
    string Status,
    string MigrationId,
    bool Accepted,
    bool RetryAvailable,
    MigrationBaselineBackupSummary? BaselineBackup,
    IReadOnlyList<string> Blockers,
    string Detail);
