using HostAgent.Runtime.Migrations.BaselineBackup;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.BaselineBackup;

public sealed class MigrationBaselineBackupHandoffStateTests
{
    [Fact]
    public void Failed_baseline_backup_keeps_acceptance_durable_and_allows_retry()
    {
        var handoff = new MigrationBaselineBackupHandoffEntity
        {
            HandoffId = "mbh_failed", Status = "failed", AttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            TargetStackSlug = "migrated-stack", CandidateId = "candidate-1", PrivateRuntimeId = "runtime-1",
            FailureCode = "baseline_backup_failed", FailureSummary = "Backup failed.",
        };

        var state = MigrationBaselineBackupHandoffService.BuildState("mig-1", handoff);

        Assert.True(state.Accepted);
        Assert.True(state.RetryAvailable);
        Assert.Equal("failed", state.Status);
        Assert.Equal("baseline_backup_failed", state.BaselineBackup!.FailureCode);
        Assert.Contains("acceptance remains durable", state.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Created_baseline_backup_enters_normal_backup_lifecycle()
    {
        var handoff = new MigrationBaselineBackupHandoffEntity
        {
            HandoffId = "mbh_created", Status = "created", AttemptCount = 1,
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
            TargetStackSlug = "migrated-stack", CandidateId = "candidate-1", PrivateRuntimeId = "runtime-1",
            BackupId = "backup-1", CatalogEntryId = "catalog-1",
        };

        var state = MigrationBaselineBackupHandoffService.BuildState("mig-1", handoff);

        Assert.False(state.RetryAvailable);
        Assert.Equal("created", state.Status);
        Assert.Equal("catalog-1", state.BaselineBackup!.CatalogEntryId);
        Assert.Contains("normal Backup/Restore lifecycle", state.Detail, StringComparison.OrdinalIgnoreCase);
    }
}
