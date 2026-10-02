using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Runtime.Migrations.Staging;

public interface IMigrationPrivateStagingRunner
{
    Task<PrivateStagingRunResult> CreateAsync(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity packageRevision,
        MigrationCandidateArtifactEntity candidate,
        string stagingRunId,
        string? targetStackSlug,
        CancellationToken ct);

    Task<PrivateStagingRunResult> DestroyAsync(
        string privateStagingId,
        CancellationToken ct);
}
