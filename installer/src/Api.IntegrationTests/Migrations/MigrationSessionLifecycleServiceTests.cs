using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Services.Identity;
using Modules.Operator.Migrations;
using Modules.Operator.Migrations.Workspace;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionLifecycleServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Awaiting_package_cancel_clears_secret_retires_revision_and_is_idempotent()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_awaiting",
            status: "awaiting-package");

        fixture.WritePackageFiles();

        var result = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.RetainEncrypted),
            "named-owner",
            CancellationToken.None);

        Assert.False(result.Idempotent);
        Assert.Equal("migration_session_cancelled", result.ResultCode);
        Assert.Equal(MigrationSessionLifecycleStatuses.Cancelled, result.Lifecycle.LifecycleStatus);
        Assert.Equal(2, result.Lifecycle.StateVersion);
        Assert.False(File.Exists(fixture.DecryptedPath));
        Assert.True(File.Exists(fixture.EncryptedPath));

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.MigrationIntakes
            .AsNoTracking()
            .Include(x => x.PackageRevisions)
            .SingleAsync();

        Assert.Equal(MigrationSessionLifecycleStatuses.Cancelled, persisted.LifecycleStatus);
        Assert.Equal("operator-cancelled", persisted.ClosureKind);
        Assert.Equal("named-owner", persisted.CancelledBy);
        Assert.Equal(Now.UtcDateTime, persisted.CancelledAtUtc);
        Assert.Equal(Now.UtcDateTime, persisted.ClosedAtUtc);

        var revision = Assert.Single(persisted.PackageRevisions);
        Assert.Equal("cancelled", revision.Status);
        Assert.Equal("retired", revision.RetentionState);
        Assert.Null(revision.ActivePurposeKey);
        Assert.Null(revision.ProtectedAgeIdentity);
        Assert.Equal(Now.UtcDateTime, revision.RetiredAtUtc);
        Assert.NotNull(revision.EncryptedPackageSha256);
        Assert.NotNull(revision.DecryptedArchiveSha256);

        var repeated = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.RetainEncrypted),
            "named-owner",
            CancellationToken.None);

        Assert.True(repeated.Idempotent);
        Assert.Equal("migration_session_already_cancelled", repeated.ResultCode);
        Assert.Equal(2, repeated.Lifecycle.StateVersion);
    }

    [Fact]
    public async Task Cancellation_durably_retires_revision_without_active_key()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_inactive_revision",
            status: "awaiting-package");

        fixture.Db.ChangeTracker.Clear();
        var revision = await fixture.Db.MigrationPackageRevisions.SingleAsync();
        revision.ActivePurposeKey = null;
        revision.RetentionState = "active";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var expectedStateVersion = await fixture.Db.MigrationIntakes
            .AsNoTracking()
            .Select(x => x.StateVersion)
            .SingleAsync();

        var result = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: expectedStateVersion,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.Remove),
            "named-owner",
            CancellationToken.None);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.MigrationPackageRevisions
            .AsNoTracking()
            .SingleAsync();

        Assert.Null(persisted.ActivePurposeKey);
        Assert.Equal("retired", persisted.RetentionState);
        Assert.Equal(Now.UtcDateTime, persisted.RetiredAtUtc);
        Assert.True(result.Lifecycle.Capabilities.CanDelete);
    }

    [Fact]
    public async Task Validated_package_cancel_rejects_stale_version_then_removes_target_package()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_validated",
            status: "package-validated");

        fixture.WritePackageFiles();

        var stale = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(
                fixture.IntakeId,
                new MigrationSessionCancelRequest(
                    ExpectedStateVersion: 99,
                    AcknowledgeSourceUnaffected: true,
                    EncryptedPackageRetention:
                        MigrationSessionEncryptedPackageRetentionPolicies.Remove),
                "owner",
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);
        Assert.Equal("migration_session_state_stale", stale.Code);
        Assert.True(File.Exists(fixture.DecryptedPath));
        Assert.True(File.Exists(fixture.EncryptedPath));

        var result = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.Remove),
            "owner",
            CancellationToken.None);

        Assert.Equal("migration_session_cancelled", result.ResultCode);
        Assert.False(File.Exists(fixture.DecryptedPath));
        Assert.False(File.Exists(fixture.EncryptedPath));
    }

    [Fact]
    public async Task Cancel_requires_source_acknowledgement_and_explicit_retention_policy()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_contract",
            status: "awaiting-package");

        var acknowledgement = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(
                fixture.IntakeId,
                new MigrationSessionCancelRequest(
                    ExpectedStateVersion: 1,
                    AcknowledgeSourceUnaffected: false,
                    EncryptedPackageRetention:
                        MigrationSessionEncryptedPackageRetentionPolicies.Remove),
                "owner",
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, acknowledgement.StatusCode);
        Assert.Equal(
            "migration_session_source_acknowledgement_required",
            acknowledgement.Code);

        var retention = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(
                fixture.IntakeId,
                new MigrationSessionCancelRequest(
                    ExpectedStateVersion: 1,
                    AcknowledgeSourceUnaffected: true,
                    EncryptedPackageRetention: "unspecified"),
                "owner",
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status400BadRequest, retention.StatusCode);
        Assert.Equal("migration_session_retention_policy_invalid", retention.Code);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.MigrationIntakes
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(MigrationSessionLifecycleStatuses.Active, persisted.LifecycleStatus);
        Assert.Equal(1, persisted.StateVersion);
    }

    [Fact]
    public async Task Expired_request_can_be_explicitly_cancelled_from_closed_history()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_expired",
            status: "awaiting-package",
            expired: true);

        var inspection = await fixture.Service.GetAsync(
            fixture.IntakeId,
            CancellationToken.None);

        Assert.NotNull(inspection);
        Assert.Equal(MigrationSessionLifecycleStatuses.Closed, inspection.LifecycleStatus);
        Assert.Equal("expired", inspection.ClosureKind);
        Assert.True(inspection.Capabilities.CanCancel);
        Assert.True(inspection.Capabilities.CanDelete);
        Assert.Equal("expired", inspection.PackageState);

        var result = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.Remove),
            "owner",
            CancellationToken.None);

        Assert.Equal("migration_session_cancelled", result.ResultCode);
        Assert.Equal(MigrationSessionLifecycleStatuses.Cancelled, result.Lifecycle.LifecycleStatus);
        Assert.Equal("operator-cancelled", result.Lifecycle.ClosureKind);
    }

    [Fact]
    public async Task Expired_request_can_be_deleted_without_rewriting_persisted_lifecycle_root()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_expired",
            status: "awaiting-package",
            expired: true);

        var persisted = await fixture.Db.MigrationIntakes
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal(MigrationSessionLifecycleStatuses.Active, persisted.LifecycleStatus);

        var inspection = await fixture.Service.GetAsync(
            fixture.IntakeId,
            CancellationToken.None);
        Assert.NotNull(inspection);
        Assert.Equal(MigrationSessionLifecycleStatuses.Closed, inspection.LifecycleStatus);
        Assert.Equal("expired", inspection.ClosureKind);
        Assert.True(inspection.Capabilities.CanDelete);

        var result = await fixture.Service.DeleteAsync(
            fixture.IntakeId,
            new MigrationSessionDeleteRequest(
                inspection.StateVersion,
                fixture.IntakeId,
                true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("migration_session_deleted", result.ResultCode);
        Assert.False(await fixture.Db.MigrationIntakes.AnyAsync());
        var audit = await fixture.Db.MemOperatorAuditEvents
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("expired-request", audit.ReasonCode);
    }

    [Fact]
    public async Task Physically_expired_revision_is_not_treated_as_live_package_authority()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_physically_expired",
            status: "awaiting-package",
            expired: true);

        var intake = await fixture.Db.MigrationIntakes.SingleAsync();
        var revision = await fixture.Db.MigrationPackageRevisions.SingleAsync();
        revision.Status = "expired";
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var inspection = await fixture.Service.GetAsync(
            fixture.IntakeId,
            CancellationToken.None);

        Assert.NotNull(inspection);
        Assert.Equal(MigrationSessionLifecycleStatuses.Closed, inspection.LifecycleStatus);
        Assert.Equal("expired", inspection.PackageState);
        Assert.True(inspection.Capabilities.CanDelete);

        var result = await fixture.Service.DeleteAsync(
            fixture.IntakeId,
            new MigrationSessionDeleteRequest(
                inspection.StateVersion,
                fixture.IntakeId,
                true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("migration_session_deleted", result.ResultCode);
        Assert.False(await fixture.Db.MigrationIntakes.AnyAsync());
    }

    [Fact]
    public async Task Active_conversion_still_blocks_progressed_cancellation()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_conversion_active",
            status: "package-validated",
            addConversion: true);

        var exception = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(
                fixture.IntakeId,
                new MigrationSessionCancelRequest(
                    ExpectedStateVersion: 1,
                    AcknowledgeSourceUnaffected: true,
                    EncryptedPackageRetention:
                        MigrationSessionEncryptedPackageRetentionPolicies.Remove),
                "owner",
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("migration_session_conversion_active", exception.Code);
    }

    [Fact]
    public async Task Progressed_cancel_retires_candidate_and_removes_conversion_workspace()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_progressed",
            status: "package-validated");
        fixture.WritePackageFiles();
        var stateVersion = await fixture.AddCompletedConversionWithCandidateAsync();

        var before = await fixture.Service.GetAsync(
            fixture.IntakeId,
            CancellationToken.None);
        Assert.NotNull(before);
        Assert.True(before.Capabilities.CanCancel);
        Assert.Equal(
            "progressed",
            MigrationSessionLifecycleService.BuildCancellation(
                await fixture.LoadIntakeAsync(),
                Now.UtcDateTime).ConfirmationKind);

        var result = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: stateVersion,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.RetainEncrypted),
            "owner",
            CancellationToken.None);

        Assert.Equal("migration_session_cancelled", result.ResultCode);
        Assert.Equal(MigrationSessionLifecycleStatuses.Cancelled, result.Lifecycle.LifecycleStatus);
        Assert.False(Directory.Exists(fixture.ConversionSessionRoot));
        Assert.False(File.Exists(fixture.DecryptedPath));
        Assert.True(File.Exists(fixture.EncryptedPath));

        fixture.Db.ChangeTracker.Clear();
        var candidate = await fixture.Db.MigrationCandidateArtifacts
            .AsNoTracking()
            .SingleAsync();
        Assert.Equal("retired", candidate.RetentionState);
        Assert.Equal(Now.UtcDateTime, candidate.RetiredAtUtc);
        var attempt = await fixture.Db.MigrationConversionAttempts
            .AsNoTracking()
            .SingleAsync();
        Assert.Null(attempt.ActiveMigrationKey);
        Assert.False(result.Lifecycle.Capabilities.CanDelete);
    }

    [Fact]
    public async Task Progressed_cancel_fails_closed_for_unknown_conversion_material()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_cancel_progressed_unknown",
            status: "package-validated");
        fixture.WritePackageFiles();
        var stateVersion = await fixture.AddCompletedConversionWithCandidateAsync(
            addUnknownAttemptEntry: true);

        var exception = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(
                fixture.IntakeId,
                new MigrationSessionCancelRequest(
                    ExpectedStateVersion: stateVersion,
                    AcknowledgeSourceUnaffected: true,
                    EncryptedPackageRetention:
                        MigrationSessionEncryptedPackageRetentionPolicies.RetainEncrypted),
                "owner",
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
        Assert.Equal("migration_session_progressed_cleanup_failed", exception.Code);
        fixture.Db.ChangeTracker.Clear();
        var intake = await fixture.Db.MigrationIntakes.AsNoTracking().SingleAsync();
        var candidate = await fixture.Db.MigrationCandidateArtifacts.AsNoTracking().SingleAsync();
        Assert.Equal(MigrationSessionLifecycleStatuses.Active, intake.LifecycleStatus);
        Assert.Equal("active", candidate.RetentionState);
        Assert.True(File.Exists(Path.Combine(
            fixture.ConversionAttemptRoot,
            "operator-notes.txt")));
    }

    [Fact]
    public async Task Completed_session_cancel_is_blocked()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_completed",
            status: "package-validated",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Completed);

        var exception = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(
                fixture.IntakeId,
                new MigrationSessionCancelRequest(
                    ExpectedStateVersion: 1,
                    AcknowledgeSourceUnaffected: true,
                    EncryptedPackageRetention:
                        MigrationSessionEncryptedPackageRetentionPolicies.Remove),
                "owner",
                CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal("migration_session_completed", exception.Code);
    }

    [Fact]
    public async Task Archive_and_unarchive_change_visibility_without_touching_package_files()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_archive",
            status: "abandoned",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Closed);

        fixture.WritePackageFiles();

        var archived = await fixture.Service.ArchiveAsync(
            fixture.IntakeId,
            expectedStateVersion: 1,
            actor: "archive-owner",
            CancellationToken.None);

        Assert.Equal("migration_session_archived", archived.ResultCode);
        Assert.True(archived.Lifecycle.Archived);
        Assert.Equal(2, archived.Lifecycle.StateVersion);
        Assert.True(File.Exists(fixture.DecryptedPath));
        Assert.True(File.Exists(fixture.EncryptedPath));

        var unarchived = await fixture.Service.UnarchiveAsync(
            fixture.IntakeId,
            expectedStateVersion: 2,
            CancellationToken.None);

        Assert.Equal("migration_session_unarchived", unarchived.ResultCode);
        Assert.False(unarchived.Lifecycle.Archived);
        Assert.Equal(3, unarchived.Lifecycle.StateVersion);
        Assert.True(File.Exists(fixture.DecryptedPath));
        Assert.True(File.Exists(fixture.EncryptedPath));
    }

    [Fact]
    public void Resource_owning_foreign_keys_restrict_session_root_deletion()
    {
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new MemDbContext(options);

        AssertRootDeleteBehavior<MigrationSourceEntity>(
            db,
            DeleteBehavior.Restrict);
        AssertRootDeleteBehavior<MigrationPackageRevisionEntity>(
            db,
            DeleteBehavior.Restrict);
        AssertRootDeleteBehavior<MigrationConversionAttemptEntity>(
            db,
            DeleteBehavior.Restrict);
        AssertRootDeleteBehavior<MigrationStagingRunEntity>(
            db,
            DeleteBehavior.Restrict);
    }

    [Fact]
    public async Task Cancelled_before_upload_delete_purges_explicit_graph_and_retains_redacted_audit()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_cancelled_empty",
            status: "awaiting-package");

        var cancelled = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.Remove),
            "owner",
            CancellationToken.None);
        Assert.True(cancelled.Lifecycle.Capabilities.CanDelete);

        fixture.Db.ChangeTracker.Clear();
        var intake = await fixture.Db.MigrationIntakes.SingleAsync();
        fixture.Db.MigrationSources.Add(new MigrationSourceEntity
        {
            Id = Guid.NewGuid(),
            MigrationIntakeEntityId = intake.Id,
            SourceId = "source-1",
            SourceKind = "external",
            Product = "MatrixEasyMode",
            ProductVersion = "0.1.0",
            SourceFingerprint = new string('1', 64),
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var expectedStateVersion = await fixture.Db.MigrationIntakes
            .AsNoTracking()
            .Select(x => x.StateVersion)
            .SingleAsync();

        var actorOperatorId = Guid.NewGuid();
        var request = new MigrationSessionDeleteRequest(
            ExpectedStateVersion: expectedStateVersion,
            ConfirmationMigrationId: fixture.IntakeId,
            AcknowledgeSourceUnaffected: true);

        var result = await fixture.Service.DeleteAsync(
            fixture.IntakeId,
            request,
            actorOperatorId,
            CancellationToken.None);

        Assert.False(result.Idempotent);
        Assert.Equal("migration_session_deleted", result.ResultCode);
        Assert.False(await fixture.Db.MigrationIntakes.AnyAsync());
        Assert.False(await fixture.Db.MigrationSources.AnyAsync());
        Assert.False(await fixture.Db.MigrationPackageRevisions.AnyAsync());

        var audit = await fixture.Db.MemOperatorAuditEvents.AsNoTracking().SingleAsync();
        Assert.Equal("migration.session.deleted", audit.EventType);
        Assert.Equal("succeeded", audit.Outcome);
        Assert.Equal(actorOperatorId, audit.ActorOperatorId);
        Assert.Equal(fixture.IntakeId, audit.CorrelationId);
        Assert.Equal("cancelled-before-upload", audit.ReasonCode);

        var repeated = await fixture.Service.DeleteAsync(
            fixture.IntakeId,
            request,
            actorOperatorId,
            CancellationToken.None);
        Assert.True(repeated.Idempotent);
        Assert.Equal("migration_session_already_deleted", repeated.ResultCode);
    }

    [Fact]
    public async Task Cancelled_package_only_delete_removes_verified_target_remnants()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_package_only",
            status: "package-validated");
        fixture.WritePackageFiles();

        var cancelled = await fixture.Service.CancelAsync(
            fixture.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.RetainEncrypted),
            "owner",
            CancellationToken.None);

        Assert.True(File.Exists(fixture.EncryptedPath));
        Assert.True(cancelled.Lifecycle.Capabilities.CanDelete);

        var result = await fixture.Service.DeleteAsync(
            fixture.IntakeId,
            new MigrationSessionDeleteRequest(
                ExpectedStateVersion: cancelled.Lifecycle.StateVersion,
                ConfirmationMigrationId: fixture.IntakeId,
                AcknowledgeSourceUnaffected: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("migration_session_deleted", result.ResultCode);
        Assert.False(Directory.Exists(
            MigrationPackageRevisionStorage.ResolveIntakeRoot(
                fixture.DataRoot,
                fixture.IntakeId)));
        Assert.False(await fixture.Db.MigrationPackageRevisions.AnyAsync());
    }

    [Fact]
    public async Task Delete_fails_closed_for_conversion_completed_and_unknown_target_state()
    {
        await using var conversion = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_conversion",
            status: "cancelled",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled,
            addConversion: true);

        var conversionBlocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            conversion.Service.DeleteAsync(
                conversion.IntakeId,
                new MigrationSessionDeleteRequest(1, conversion.IntakeId, true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal("migration_session_conversion_exists", conversionBlocked.Code);

        await using var activePackage = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_active_package",
            status: "cancelled",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled);
        var activePackageBlocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            activePackage.Service.DeleteAsync(
                activePackage.IntakeId,
                new MigrationSessionDeleteRequest(1, activePackage.IntakeId, true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(
            "migration_session_package_authority_active",
            activePackageBlocked.Code);

        activePackage.Db.ChangeTracker.Clear();
        var driftedRevision = await activePackage.Db.MigrationPackageRevisions.SingleAsync();
        driftedRevision.ActivePurposeKey = null;
        driftedRevision.RetentionState = "active";
        await activePackage.Db.SaveChangesAsync();
        activePackage.Db.ChangeTracker.Clear();
        var driftedStateVersion = await activePackage.Db.MigrationIntakes
            .AsNoTracking()
            .Select(x => x.StateVersion)
            .SingleAsync();

        var driftedPackageBlocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            activePackage.Service.DeleteAsync(
                activePackage.IntakeId,
                new MigrationSessionDeleteRequest(
                    driftedStateVersion,
                    activePackage.IntakeId,
                    true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(
            "migration_session_package_authority_active",
            driftedPackageBlocked.Code);

        await using var completed = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_completed",
            status: "accepted",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Completed,
            addPackageRevision: false);
        var completedBlocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            completed.Service.DeleteAsync(
                completed.IntakeId,
                new MigrationSessionDeleteRequest(1, completed.IntakeId, true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal("migration_session_completed", completedBlocked.Code);

        await using var unknown = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_unknown",
            status: "cancelled",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled,
            addPackageRevision: false);
        var root = MigrationPackageRevisionStorage.ResolveIntakeRoot(
            unknown.DataRoot,
            unknown.IntakeId);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "operator-notes.txt"), "unknown");

        var unknownBlocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            unknown.Service.DeleteAsync(
                unknown.IntakeId,
                new MigrationSessionDeleteRequest(1, unknown.IntakeId, true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal("migration_session_target_state_unverifiable", unknownBlocked.Code);
        Assert.True(await unknown.Db.MigrationIntakes.AnyAsync());
        Assert.True(File.Exists(Path.Combine(root, "operator-notes.txt")));
    }

    [Fact]
    public async Task Delete_blocks_canonical_acceptance_even_when_lifecycle_is_not_completed()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_acceptance",
            status: "cancelled",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled,
            addPackageRevision: false);

        var intake = await fixture.Db.MigrationIntakes.SingleAsync();
        fixture.Db.MigrationAcceptances.Add(new MigrationAcceptanceEntity
        {
            Id = Guid.NewGuid(),
            AcceptanceId = "maccept_lifecycle_delete",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            ExecutionId = "mexec_lifecycle_delete",
            CandidateArtifactId = "mca_lifecycle_delete",
            StagingRunId = "mstg_lifecycle_delete",
            PublicVerificationStatus = "passed",
            PublicVerificationEvidenceJson = "{}",
            PublicVerificationEvidenceSha256 = new string('a', 64),
            PublicCutoverAtUtc = Now.UtcDateTime.AddMinutes(-2),
            AcceptedAtUtc = Now.UtcDateTime.AddMinutes(-1),
            AcceptedBy = "operator",
            FreshPublicVerificationAcknowledged = true,
            TargetWriteDivergenceAcknowledged = true,
            RollbackBoundaryAcknowledged = true,
            LegacyRetentionAcknowledged = true,
            NoAutomaticLegacyDeletionAcknowledged = true,
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var inspection = await fixture.Service.GetAsync(
            fixture.IntakeId,
            CancellationToken.None);
        Assert.NotNull(inspection);
        Assert.False(inspection.Capabilities.CanDelete);
        Assert.Equal(
            "migration_session_completed",
            inspection.Capabilities.DeleteBlockedCode);

        var blocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.DeleteAsync(
                fixture.IntakeId,
                new MigrationSessionDeleteRequest(
                    inspection.StateVersion,
                    fixture.IntakeId,
                    true),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("migration_session_completed", blocked.Code);
        Assert.True(await fixture.Db.MigrationIntakes.AnyAsync());
        Assert.True(await fixture.Db.MigrationAcceptances.AnyAsync());
    }

    [Fact]
    public async Task Delete_blocks_inconsistent_known_package_material()
    {
        await using var noRevision = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_unexpected_package",
            status: "cancelled",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled,
            addPackageRevision: false);
        var legacyEncrypted =
            MigrationPackageRevisionStorage.ResolveLegacyEncryptedArchivePath(
                noRevision.DataRoot,
                noRevision.IntakeId);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyEncrypted)!);
        File.WriteAllText(legacyEncrypted, "unexpected encrypted material");

        var unexpected = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            noRevision.Service.DeleteAsync(
                noRevision.IntakeId,
                new MigrationSessionDeleteRequest(1, noRevision.IntakeId, true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(
            "migration_session_unexpected_package_material",
            unexpected.Code);
        Assert.True(File.Exists(legacyEncrypted));

        await using var plaintext = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_plaintext_package",
            status: "package-validated");
        plaintext.WritePackageFiles();
        var cancelled = await plaintext.Service.CancelAsync(
            plaintext.IntakeId,
            new MigrationSessionCancelRequest(
                ExpectedStateVersion: 1,
                AcknowledgeSourceUnaffected: true,
                EncryptedPackageRetention:
                    MigrationSessionEncryptedPackageRetentionPolicies.RetainEncrypted),
            "owner",
            CancellationToken.None);
        File.WriteAllText(plaintext.DecryptedPath, "unexpected plaintext material");

        var plaintextBlocked = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            plaintext.Service.DeleteAsync(
                plaintext.IntakeId,
                new MigrationSessionDeleteRequest(
                    cancelled.Lifecycle.StateVersion,
                    plaintext.IntakeId,
                    true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(
            "migration_session_plaintext_package_present",
            plaintextBlocked.Code);
        Assert.True(File.Exists(plaintext.DecryptedPath));
    }

    [Fact]
    public async Task Delete_requires_exact_id_acknowledgement_and_current_state_version()
    {
        await using var fixture = await LifecycleFixture.CreateAsync(
            "mig_lifecycle_delete_contract",
            status: "cancelled",
            lifecycleStatus: MigrationSessionLifecycleStatuses.Cancelled,
            addPackageRevision: false);

        var acknowledgement = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.DeleteAsync(
                fixture.IntakeId,
                new MigrationSessionDeleteRequest(1, fixture.IntakeId, false),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, acknowledgement.StatusCode);
        Assert.Equal("migration_session_source_acknowledgement_required", acknowledgement.Code);

        var confirmation = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.DeleteAsync(
                fixture.IntakeId,
                new MigrationSessionDeleteRequest(1, "wrong-id", true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, confirmation.StatusCode);
        Assert.Equal("migration_session_delete_confirmation_mismatch", confirmation.Code);

        var paddedConfirmation = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.DeleteAsync(
                fixture.IntakeId,
                new MigrationSessionDeleteRequest(1, $" {fixture.IntakeId} ", true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(
            "migration_session_delete_confirmation_mismatch",
            paddedConfirmation.Code);

        var stale = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.DeleteAsync(
                fixture.IntakeId,
                new MigrationSessionDeleteRequest(99, fixture.IntakeId, true),
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(StatusCodes.Status409Conflict, stale.StatusCode);
        Assert.Equal("migration_session_state_stale", stale.Code);
    }

    [Fact]
    public async Task Guided_empty_cancel_retires_request_and_returns_terminal_inventory_without_deleting_history()
    {
        await using var fixture = await LifecycleFixture.CreateAsync("mig_guided_empty_cancel", "awaiting-package");
        var time = new FixedTimeProvider(Now);
        var workspace = new MigrationWorkspaceProjectionService(fixture.Db,
            new MigrationSessionProjectionService(fixture.Db, time), time);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["HostAgent:DataRoot"] = fixture.DataRoot }).Build();
        var inventory = new MigrationSessionInventoryService(fixture.Db, time, configuration);
        var query = new MigrationSessionInventoryQuery(1, 10, null, "all", "all", null, null, "updated", "desc", false);
        var before = await workspace.GetAsync(fixture.IntakeId, CancellationToken.None);
        Assert.NotNull(before);
        Assert.True(before.Guided.Cancellation!.CanCancel);
        Assert.Equal("empty-session", before.Guided.Cancellation.ConfirmationKind);
        Assert.Equal(1, (await inventory.ListAsync(query, CancellationToken.None)).Summary.ActiveCount);

        await fixture.Service.CancelAsync(fixture.IntakeId,
            new MigrationSessionCancelRequest(before.Guided.Cancellation.StateVersion, true, "remove"),
            "named-owner", CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();

        var after = await workspace.GetAsync(fixture.IntakeId, CancellationToken.None);
        Assert.Equal("cancelled", after!.Guided.Cancellation!.LifecycleStatus);
        Assert.False(after.Guided.Cancellation.CanCancel);
        Assert.NotEqual(before.Guided.OperationRevisions["cancel-migration"], after.Guided.OperationRevisions["cancel-migration"]);
        Assert.All(after.Stages, stage => Assert.False(stage.PrimaryAction?.Enabled ?? false));
        var saved = await fixture.Db.MigrationIntakes.AsNoTracking().Include(x => x.PackageRevisions).SingleAsync();
        Assert.Null(saved.ArchivedAtUtc);
        Assert.Equal("operator-cancelled", saved.ClosureKind);
        Assert.Null(MigrationPackageRevisionAuthority.ResolveAwaitingUpload(saved.PackageRevisions));
        Assert.Null(Assert.Single(saved.PackageRevisions).ProtectedAgeIdentity);
        var result = await inventory.ListAsync(query, CancellationToken.None);
        Assert.Equal(0, result.Summary.ActiveCount);
        Assert.Equal(1, result.Summary.CancelledCount);
        Assert.Equal("cancelled", Assert.Single(result.Sessions).LifecycleStatus);
        Assert.False(await fixture.Db.MigrationProductionAdoptions.AnyAsync());
        Assert.False(await fixture.Db.MigrationStagingRuns.AnyAsync());
    }

    [Fact]
    public async Task Upload_after_empty_confirmation_rejects_old_version_without_removing_the_new_package()
    {
        await using var fixture = await LifecycleFixture.CreateAsync("mig_cancel_upload_race", "awaiting-package");
        var before = await fixture.Service.GetAsync(fixture.IntakeId, CancellationToken.None);
        var revision = await fixture.Db.MigrationPackageRevisions.SingleAsync();
        revision.Status = "package-validated";
        revision.UploadedAtUtc = Now.UtcDateTime;
        revision.EncryptedPackageSha256 = new string('a', 64);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        fixture.WritePackageFiles();

        var rejected = await Assert.ThrowsAsync<MigrationSessionLifecycleException>(() =>
            fixture.Service.CancelAsync(fixture.IntakeId,
                new MigrationSessionCancelRequest(before!.StateVersion, true, "remove"), "owner", CancellationToken.None));
        Assert.Equal("migration_session_state_stale", rejected.Code);
        Assert.True(File.Exists(fixture.EncryptedPath));
        Assert.True(File.Exists(fixture.DecryptedPath));
        var root = await fixture.Db.MigrationIntakes.AsNoTracking().Include(x => x.PackageRevisions).SingleAsync();
        Assert.Equal("active", root.LifecycleStatus);
        Assert.NotNull(Assert.Single(root.PackageRevisions).ProtectedAgeIdentity);
        Assert.Equal("package", MigrationSessionLifecycleService.BuildCancellation(root, Now.UtcDateTime).ConfirmationKind);
    }

    private static void AssertRootDeleteBehavior<TEntity>(
        MemDbContext db,
        DeleteBehavior expected)
        where TEntity : class
    {
        var entityType = db.Model.FindEntityType(typeof(TEntity));
        Assert.NotNull(entityType);
        var foreignKey = Assert.Single(
            entityType.GetForeignKeys(),
            candidate => candidate.PrincipalEntityType.ClrType ==
                typeof(MigrationIntakeEntity));
        Assert.Equal(expected, foreignKey.DeleteBehavior);
    }

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private LifecycleFixture(
            SqliteConnection connection,
            MemDbContext db,
            MigrationSessionLifecycleService service,
            string dataRoot,
            string intakeId,
            string packageRevisionId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            DataRoot = dataRoot;
            IntakeId = intakeId;
            PackageRevisionId = packageRevisionId;
        }

        public MemDbContext Db { get; }
        public MigrationSessionLifecycleService Service { get; }
        public string DataRoot { get; }
        public string IntakeId { get; }
        public string PackageRevisionId { get; }

        public string EncryptedPath =>
            MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
                DataRoot,
                IntakeId,
                PackageRevisionId);

        public string DecryptedPath =>
            MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                DataRoot,
                IntakeId,
                PackageRevisionId);

        public string ConversionSessionRoot =>
            Path.Combine(DataRoot, "migration-conversions", IntakeId);

        public string ConversionAttemptRoot { get; private set; } = string.Empty;

        public static async Task<LifecycleFixture> CreateAsync(
            string intakeId,
            string status,
            string lifecycleStatus = MigrationSessionLifecycleStatuses.Active,
            bool addConversion = false,
            bool expired = false,
            bool addPackageRevision = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var timeProvider = new FixedTimeProvider(Now);
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .AddInterceptors(
                    new MigrationSessionLifecycleInterceptor(timeProvider))
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                "mem-migration-lifecycle-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dataRoot);

            var intakeEntityId = Guid.NewGuid();
            var revisionId = Guid.NewGuid();
            var packageRevisionId = $"mpr_{Guid.NewGuid():N}";
            var createdAtUtc = Now.UtcDateTime.AddMinutes(-5);
            var intake = new MigrationIntakeEntity
            {
                Id = intakeEntityId,
                IntakeId = intakeId,
                DisplayName = "Lifecycle test",
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
                LifecycleStatus = lifecycleStatus,
                StateVersion = 1,
                ClosedAtUtc = lifecycleStatus == MigrationSessionLifecycleStatuses.Active
                    ? null
                    : createdAtUtc,
                ClosureKind = lifecycleStatus switch
                {
                    MigrationSessionLifecycleStatuses.Cancelled => "operator-cancelled",
                    MigrationSessionLifecycleStatuses.Completed => "accepted-baseline-created",
                    MigrationSessionLifecycleStatuses.Closed => "operator-closed",
                    _ => null,
                },
                CancelledAtUtc = lifecycleStatus == MigrationSessionLifecycleStatuses.Cancelled
                    ? createdAtUtc
                    : null,
            };
            var revision = new MigrationPackageRevisionEntity
            {
                Id = revisionId,
                PackageRevisionId = packageRevisionId,
                MigrationIntakeEntityId = intakeEntityId,
                MigrationIntake = intake,
                RevisionNumber = 1,
                Purpose = "preview",
                Status = status,
                RetentionState = "active",
                ActivePurposeKey = $"{intakeId}:preview",
                CreatedAtUtc = Now.UtcDateTime.AddMinutes(-5),
                ExpiresAtUtc = expired
                    ? Now.UtcDateTime.AddHours(-1)
                    : Now.UtcDateTime.AddHours(1),
                UploadedAtUtc = status == "package-validated"
                    ? Now.UtcDateTime.AddMinutes(-4)
                    : null,
                ValidatedAtUtc = status == "package-validated"
                    ? Now.UtcDateTime.AddMinutes(-3)
                    : null,
                AgeRecipient = "age1test",
                ProtectedAgeIdentity = "protected-revision-identity",
                RecipientFingerprint = "recipient-test",
                PackageFileName = status == "package-validated"
                    ? "source.memmigration.zip.age"
                    : null,
                PackageSizeBytes = status == "package-validated" ? 512 : null,
                EncryptedPackageSha256 = status == "package-validated"
                    ? new string('a', 64)
                    : null,
                DecryptedArchiveSha256 = status == "package-validated"
                    ? new string('b', 64)
                    : null,
            };
            if (addPackageRevision)
            {
                intake.PackageRevisions.Add(revision);
            }

            if (addConversion)
            {
                intake.ConversionAttempts.Add(new MigrationConversionAttemptEntity
                {
                    Id = Guid.NewGuid(),
                    ConversionAttemptId = $"mca_{Guid.NewGuid():N}",
                    MigrationIntakeEntityId = intakeEntityId,
                    MigrationIntake = intake,
                    MigrationPackageRevisionEntityId = revisionId,
                    PackageRevision = revision,
                    ActiveMigrationKey = intakeId,
                    SourcePackageSha256 = new string('b', 64),
                    SourceAdapterId = "mem-v010",
                    SourceAdapterVersion = "1",
                    ConverterId = "mem-converter",
                    ConverterVersion = "1",
                    Status = "created",
                    CurrentStep = "queued",
                    CreatedAtUtc = Now.UtcDateTime.AddMinutes(-2),
                    UpdatedAtUtc = Now.UtcDateTime.AddMinutes(-2),
                });
            }

            db.MigrationIntakes.Add(intake);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot,
                })
                .Build();

            return new LifecycleFixture(
                connection,
                db,
                new MigrationSessionLifecycleService(
                    db,
                    configuration,
                    timeProvider,
                    new MemOperatorAuditService(db, timeProvider)),
                dataRoot,
                intakeId,
                packageRevisionId);
        }

        public async Task<long> AddCompletedConversionWithCandidateAsync(
            bool addUnknownAttemptEntry = false)
        {
            Db.ChangeTracker.Clear();

            // Seed the already-completed conversion state without the lifecycle interceptor.
            // This fixture is establishing the durable precondition for cancellation; the
            // interceptor has its own focused coverage. Seeding through it here would make
            // this helper test optimistic root-version mutation as well as cancellation.
            var seedOptions = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(_connection)
                .Options;
            await using var seedDb = new MemDbContext(seedOptions);
            var intakeSeed = await seedDb.MigrationIntakes
                .AsNoTracking()
                .Select(x => new { x.Id, x.StateVersion })
                .SingleAsync();
            var revisionId = await seedDb.MigrationPackageRevisions
                .AsNoTracking()
                .Select(x => x.Id)
                .SingleAsync();
            var attemptId = $"mca_{Guid.NewGuid():N}";
            var attemptEntityId = Guid.NewGuid();
            ConversionAttemptRoot = Path.Combine(ConversionSessionRoot, attemptId);
            var workspacePath = Path.Combine(ConversionAttemptRoot, "work");
            var outputPath = Path.Combine(ConversionAttemptRoot, "output");
            var logsPath = Path.Combine(ConversionAttemptRoot, "logs");
            Directory.CreateDirectory(workspacePath);
            Directory.CreateDirectory(outputPath);
            Directory.CreateDirectory(logsPath);
            File.WriteAllText(Path.Combine(workspacePath, "conversion.tmp"), "working");
            File.WriteAllText(Path.Combine(logsPath, "worker-events.jsonl"), "{}\n");
            File.WriteAllText(Path.Combine(ConversionAttemptRoot, "worker-request.json"), "{}");
            var artifactPath = Path.Combine(outputPath, "candidate.sql");
            var evidencePath = Path.Combine(outputPath, "checksums.json");
            var reportPath = Path.Combine(outputPath, "conversion-report.json");
            File.WriteAllText(artifactPath, "candidate");
            File.WriteAllText(evidencePath, "{}");
            File.WriteAllText(reportPath, "{}");
            if (addUnknownAttemptEntry)
            {
                File.WriteAllText(Path.Combine(ConversionAttemptRoot, "operator-notes.txt"), "unknown");
            }

            var attempt = new MigrationConversionAttemptEntity
            {
                Id = attemptEntityId,
                ConversionAttemptId = attemptId,
                MigrationIntakeEntityId = intakeSeed.Id,
                MigrationPackageRevisionEntityId = revisionId,
                ActiveMigrationKey = null,
                SourcePackageSha256 = new string('b', 64),
                SourceAdapterId = "mem-v010",
                SourceAdapterVersion = "1",
                ConverterId = "mem-converter",
                ConverterVersion = "1",
                Status = "completed",
                CurrentStep = "candidate-created",
                CreatedAtUtc = Now.UtcDateTime.AddMinutes(-3),
                UpdatedAtUtc = Now.UtcDateTime.AddMinutes(-1),
                StartedAtUtc = Now.UtcDateTime.AddMinutes(-3),
                CompletedAtUtc = Now.UtcDateTime.AddMinutes(-1),
                WorkspacePath = workspacePath,
                EvidenceDirectoryPath = outputPath,
                LogDirectoryPath = logsPath,
                CompletionReportPath = reportPath,
            };
            attempt.CandidateArtifact = new MigrationCandidateArtifactEntity
            {
                Id = Guid.NewGuid(),
                CandidateArtifactId = $"candidate_{Guid.NewGuid():N}",
                MigrationConversionAttemptEntityId = attemptEntityId,
                ArtifactKind = "synapse-postgresql-conversion",
                ArtifactSchemaVersion = "mem-conversion-output/v1",
                SourcePackageSha256 = new string('b', 64),
                ArtifactSha256 = new string('c', 64),
                ManifestSha256 = new string('d', 64),
                ChecksumsSha256 = new string('e', 64),
                ProvenanceJson = "{}",
                VerificationStatus = "verified",
                RetentionState = "active",
                StorageKind = "server-filesystem",
                ArtifactPath = artifactPath,
                VerificationReportPath = evidencePath,
                CreatedAtUtc = Now.UtcDateTime.AddMinutes(-1),
                VerifiedAtUtc = Now.UtcDateTime.AddMinutes(-1),
            };
            // Seed only the new child rows. Do not track the existing Migration root or
            // package revision in this context: this helper establishes already-durable
            // conversion/candidate evidence and must not manufacture a root concurrency write.
            seedDb.MigrationConversionAttempts.Add(attempt);
            seedDb.MigrationCandidateArtifacts.Add(attempt.CandidateArtifact!);
            Assert.DoesNotContain(
                seedDb.ChangeTracker.Entries(),
                entry => entry.State is EntityState.Modified or EntityState.Deleted);
            await seedDb.SaveChangesAsync();
            return intakeSeed.StateVersion;
        }

        public async Task<MigrationIntakeEntity> LoadIntakeAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.MigrationIntakes
                .AsNoTracking()
                .Include(x => x.PackageRevisions)
                .Include(x => x.ConversionAttempts)
                    .ThenInclude(x => x.CandidateArtifact)
                .Include(x => x.StagingRuns)
                .Include(x => x.ProductionAuthorities)
                .Include(x => x.ProductionAdoption)
                .Include(x => x.Acceptance)
                .Include(x => x.LegacyRetentionRecord)
                .Include(x => x.BaselineBackupHandoff)
                .Include(x => x.TwoServerQualification)
                .SingleAsync();
        }

        public void WritePackageFiles()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EncryptedPath)!);
            File.WriteAllText(EncryptedPath, "encrypted");
            File.WriteAllText(DecryptedPath, "decrypted");
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();

            try
            {
                Directory.Delete(DataRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
