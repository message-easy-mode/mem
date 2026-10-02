using System.Security.Cryptography;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Auth.Services.Identity;

namespace Modules.Operator.Migrations;

public sealed class MigrationSessionLifecycleService(
    MemDbContext db,
    IConfiguration configuration,
    TimeProvider timeProvider,
    IMemOperatorAuditService auditService)
{
    private const string DeletedAuditEventType = "migration.session.deleted";

    public const string SourceUnaffectedNotice =
        "This target-side action does not delete or change files, containers, services, or data on the source server.";

    public async Task<MigrationSessionLifecycleInspectionDto?> GetAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var intake = await LoadAsync(
            migrationId,
            tracking: false,
            cancellationToken);
        return intake is null
            ? null
            : BuildInspection(
                intake,
                timeProvider.GetUtcNow().UtcDateTime,
                ResolveDeletionDecision(intake));
    }

    public async Task<MigrationSessionLifecycleMutationResponse> ArchiveAsync(
        string migrationId,
        long expectedStateVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var intake = await LoadRequiredAsync(migrationId, cancellationToken);

        if (intake.ArchivedAtUtc.HasValue)
        {
            return new MigrationSessionLifecycleMutationResponse(
                "migration_session_already_archived",
                true,
                BuildInspection(intake, timeProvider.GetUtcNow().UtcDateTime, ResolveDeletionDecision(intake)));
        }

        EnsureExpectedVersion(intake, expectedStateVersion);
        var inspection = BuildInspection(
            intake,
            timeProvider.GetUtcNow().UtcDateTime,
            ResolveDeletionDecision(intake));
        if (!inspection.Capabilities.CanArchive)
        {
            throw Conflict(
                "migration_session_archive_not_allowed",
                "Only terminal Migration Sessions can be archived.");
        }

        intake.ArchivedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        intake.ArchivedBy = NormalizeActor(actor);

        await SaveAsync(cancellationToken);

        return new MigrationSessionLifecycleMutationResponse(
            "migration_session_archived",
            false,
            BuildInspection(intake, timeProvider.GetUtcNow().UtcDateTime, ResolveDeletionDecision(intake)));
    }

    public async Task<MigrationSessionLifecycleMutationResponse> UnarchiveAsync(
        string migrationId,
        long expectedStateVersion,
        CancellationToken cancellationToken)
    {
        var intake = await LoadRequiredAsync(migrationId, cancellationToken);

        if (!intake.ArchivedAtUtc.HasValue)
        {
            return new MigrationSessionLifecycleMutationResponse(
                "migration_session_already_unarchived",
                true,
                BuildInspection(intake, timeProvider.GetUtcNow().UtcDateTime, ResolveDeletionDecision(intake)));
        }

        EnsureExpectedVersion(intake, expectedStateVersion);

        intake.ArchivedAtUtc = null;
        intake.ArchivedBy = null;

        await SaveAsync(cancellationToken);

        return new MigrationSessionLifecycleMutationResponse(
            "migration_session_unarchived",
            false,
            BuildInspection(intake, timeProvider.GetUtcNow().UtcDateTime, ResolveDeletionDecision(intake)));
    }

    public async Task<MigrationSessionLifecycleMutationResponse> CancelAsync(
        string migrationId,
        MigrationSessionCancelRequest request,
        string actor,
        CancellationToken cancellationToken)
    {
        var intake = await LoadRequiredAsync(migrationId, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (string.Equals(
                intake.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Cancelled,
                StringComparison.Ordinal) ||
            intake.CancelledAtUtc.HasValue)
        {
            return new MigrationSessionLifecycleMutationResponse(
                "migration_session_already_cancelled",
                true,
                BuildInspection(intake, now, ResolveDeletionDecision(intake)));
        }

        if (!request.AcknowledgeSourceUnaffected)
        {
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status400BadRequest,
                "migration_session_source_acknowledgement_required",
                "Confirm that this target-side cancellation does not change the source server.");
        }

        var retentionPolicy = request.EncryptedPackageRetention?.Trim().ToLowerInvariant();
        if (retentionPolicy is null ||
            !MigrationSessionEncryptedPackageRetentionPolicies.All.Contains(retentionPolicy))
        {
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status400BadRequest,
                "migration_session_retention_policy_invalid",
                "Choose whether the target encrypted package is removed or retained.");
        }

        EnsureExpectedVersion(intake, request.ExpectedStateVersion);

        var inspection = BuildInspection(intake, now, ResolveDeletionDecision(intake));
        if (!inspection.Capabilities.CanCancel)
        {
            throw Conflict(
                inspection.Capabilities.CancelBlockedCode ??
                "migration_session_cancel_not_allowed",
                inspection.Capabilities.CancelBlockedReason ??
                "This Migration Session cannot be cancelled from its current state.");
        }

        var targetMaterialRemoved = false;
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            // Acquire the session write boundary without consuming another public
            // StateVersion. If anything changed after the operator reviewed the
            // cancellation, the guarded write fails before filesystem cleanup.
            var claimed = await db.MigrationIntakes
                .Where(x =>
                    x.Id == intake.Id &&
                    x.StateVersion == request.ExpectedStateVersion)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        x => x.UpdatedAtUtc,
                        now),
                    cancellationToken);
            if (claimed != 1)
            {
                throw Conflict(
                    "migration_session_state_stale",
                    "The Migration Session changed while cancellation was being prepared. Refresh and review it before trying again.");
            }

            MigrationSessionTargetStorageInspector.VerifyConversionMaterial(
                ResolveDataRoot(),
                intake);

            await DeleteTargetPackageMaterialAsync(
                intake,
                removeEncrypted:
                    retentionPolicy ==
                    MigrationSessionEncryptedPackageRetentionPolicies.Remove,
                cancellationToken);

            MigrationSessionTargetStorageInspector.DeleteVerifiedConversionMaterial(
                ResolveDataRoot(),
                intake);
            targetMaterialRemoved = true;

            intake.LifecycleStatus = MigrationSessionLifecycleStatuses.Cancelled;
            intake.CancelledAtUtc = now;
            intake.CancelledBy = NormalizeActor(actor);
            intake.ClosedAtUtc = now;
            intake.ClosureKind = "operator-cancelled";

            foreach (var revision in intake.PackageRevisions)
            {
                revision.ProtectedAgeIdentity = null;

                if (revision.ActivePurposeKey is not null)
                {
                    revision.Status = "cancelled";
                }

                revision.ActivePurposeKey = null;
                revision.RetentionState = "retired";
                revision.RetiredAtUtc ??= now;
            }

            foreach (var attempt in intake.ConversionAttempts)
            {
                attempt.ActiveMigrationKey = null;
                if (attempt.CandidateArtifact is { } candidate)
                {
                    candidate.RetentionState = "retired";
                    candidate.RetiredAtUtc ??= now;
                }
            }

            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw Conflict(
                "migration_session_state_stale",
                "The Migration Session changed while cancellation was being applied. Refresh and review it before trying again.");
        }
        catch (MigrationSessionLifecycleException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status503ServiceUnavailable,
                "migration_session_progressed_cleanup_failed",
                "Prepared target-side migration material could not be proven safe to remove. No lifecycle change was committed.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status503ServiceUnavailable,
                "migration_session_cancel_failed",
                targetMaterialRemoved
                    ? "Target-side pre-production material was removed, but the lifecycle update could not be committed. The retained Session can be safely retried."
                    : "Cancellation could not be completed. The Migration Session was retained and target cleanup can be safely retried.");
        }

        return new MigrationSessionLifecycleMutationResponse(
            "migration_session_cancelled",
            false,
            BuildInspection(intake, timeProvider.GetUtcNow().UtcDateTime, ResolveDeletionDecision(intake)));
    }

    public async Task<MigrationSessionDeleteResponse> DeleteAsync(
        string migrationId,
        MigrationSessionDeleteRequest request,
        Guid actorOperatorId,
        CancellationToken cancellationToken)
    {
        if (!request.AcknowledgeSourceUnaffected)
        {
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status400BadRequest,
                "migration_session_source_acknowledgement_required",
                "Confirm that permanent deletion affects only target-side Migration Session resources.");
        }

        if (!string.Equals(
                request.ConfirmationMigrationId,
                migrationId,
                StringComparison.Ordinal))
        {
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status400BadRequest,
                "migration_session_delete_confirmation_mismatch",
                "Enter the exact Migration ID to confirm permanent deletion.");
        }

        var intake = await LoadAsync(
            migrationId,
            tracking: true,
            cancellationToken);
        if (intake is null)
        {
            var priorAudit = await FindSuccessfulDeletionAuditAsync(
                migrationId,
                cancellationToken);
            if (priorAudit is not null)
            {
                return new MigrationSessionDeleteResponse(
                    "migration_session_already_deleted",
                    true,
                    migrationId,
                    priorAudit.OccurredAtUtc);
            }

            throw new MigrationSessionLifecycleException(
                StatusCodes.Status404NotFound,
                "migration_session_not_found",
                "Migration Session was not found.");
        }

        EnsureExpectedVersion(intake, request.ExpectedStateVersion);

        var decision = ResolveDeletionDecision(intake);
        if (!decision.CanDelete)
        {
            throw Conflict(
                decision.BlockedCode ?? "migration_session_delete_not_allowed",
                decision.BlockedReason ??
                "This Migration Session cannot be permanently deleted.");
        }

        var deletedAtUtc = timeProvider.GetUtcNow();
        var targetMaterialRemoved = false;
        await using var transaction = await db.Database.BeginTransactionAsync(
            cancellationToken);

        try
        {
            var claimedStateVersion = checked(request.ExpectedStateVersion + 1);
            var claimed = await db.MigrationIntakes
                .Where(x =>
                    x.Id == intake.Id &&
                    x.StateVersion == request.ExpectedStateVersion)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            x => x.StateVersion,
                            claimedStateVersion)
                        .SetProperty(
                            x => x.UpdatedAtUtc,
                            deletedAtUtc.UtcDateTime),
                    cancellationToken);
            if (claimed != 1)
            {
                throw Conflict(
                    "migration_session_state_stale",
                    "The Migration Session changed while deletion was being prepared. Refresh and review it before trying again.");
            }

            // Synchronise the tracked root with the guarded database claim so
            // the final concurrency-token DELETE is evaluated against the
            // claimed version rather than the browser's earlier version.
            intake.StateVersion = claimedStateVersion;
            intake.UpdatedAtUtc = deletedAtUtc.UtcDateTime;
            var intakeEntry = db.Entry(intake);
            intakeEntry.Property(x => x.StateVersion).OriginalValue =
                claimedStateVersion;

            DeleteTargetOwnedSessionMaterial(intake);
            targetMaterialRemoved = true;

            var sources = await db.MigrationSources
                .Where(x => x.MigrationIntakeEntityId == intake.Id)
                .ToListAsync(cancellationToken);
            var revisions = await db.MigrationPackageRevisions
                .Where(x => x.MigrationIntakeEntityId == intake.Id)
                .ToListAsync(cancellationToken);

            // Deliberately explicit purge order. Resource-owning relationships are
            // blocked by policy and are never entrusted to broad cascade deletion.
            db.MigrationSources.RemoveRange(sources);
            db.MigrationPackageRevisions.RemoveRange(revisions);
            db.MigrationIntakes.Remove(intake);

            await auditService.WriteAsync(
                new MemOperatorAuditEventWrite(
                    DeletedAuditEventType,
                    "succeeded",
                    ActorOperatorId: actorOperatorId,
                    CorrelationId: migrationId,
                    ReasonCode: decision.AuditReasonCode),
                cancellationToken);

            if (await db.MigrationIntakes
                .AsNoTracking()
                .AnyAsync(x => x.IntakeId == migrationId, cancellationToken))
            {
                throw new InvalidOperationException(
                    "The Migration Session root remained after the explicit purge plan.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw Conflict(
                "migration_session_state_stale",
                "The Migration Session changed while deletion was being applied. Refresh and review it before trying again.");
        }
        catch (MigrationSessionLifecycleException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status503ServiceUnavailable,
                "migration_session_delete_failed",
                targetMaterialRemoved
                    ? "Target remnants were removed, but the database purge could not be completed. The retained Session can be safely retried."
                    : "Permanent deletion could not be completed. The database Session was retained and target cleanup can be safely retried.");
        }

        return new MigrationSessionDeleteResponse(
            "migration_session_deleted",
            false,
            migrationId,
            deletedAtUtc);
    }

    internal static MigrationSessionLifecycleInspectionDto BuildInspection(
        MigrationIntakeEntity intake,
        DateTime nowUtc,
        MigrationSessionDeletionDecision deletionDecision)
    {
        var effectiveStatus = MigrationPackageRevisionAuthority.ResolveEffectiveSessionStatus(
            intake,
            nowUtc);
        var effectiveLifecycle = ResolveEffectiveLifecycle(
            intake,
            effectiveStatus);
        var effectiveClosureKind = ResolveEffectiveClosureKind(
            intake,
            effectiveStatus);
        var effectiveClosedAtUtc = ResolveEffectiveClosedAtUtc(
            intake,
            effectiveStatus);
        var archived = intake.ArchivedAtUtc.HasValue;
        var terminal = effectiveLifecycle is
            MigrationSessionLifecycleStatuses.Completed or
            MigrationSessionLifecycleStatuses.Closed or
            MigrationSessionLifecycleStatuses.Cancelled;
        var hasCandidate = intake.ConversionAttempts.Any(x =>
            x.CandidateArtifact is not null);
        var retainedCandidate = intake.ConversionAttempts
            .Select(x => x.CandidateArtifact)
            .Any(x => x is not null && string.Equals(
                x.RetentionState, "active", StringComparison.OrdinalIgnoreCase));
        var retainedStaging = intake.StagingRuns.Any(x =>
            x.ActiveMigrationKey is not null && !x.DestroyedAtUtc.HasValue);
        var cancellation = BuildCancellation(intake, nowUtc);

        return new MigrationSessionLifecycleInspectionDto(
            intake.IntakeId,
            effectiveLifecycle,
            archived,
            intake.ArchivedAtUtc,
            intake.ArchivedBy,
            effectiveClosedAtUtc,
            effectiveClosureKind,
            Math.Max(intake.StateVersion, 1),
            ResolveCurrentOperation(intake),
            ResolvePackageState(intake, nowUtc),
            !hasCandidate
                ? "none"
                : retainedCandidate
                    ? "retained"
                    : "retired",
            intake.StagingRuns.Count == 0
                ? "none"
                : retainedStaging
                    ? "retained"
                    : "destroyed",
            ResolveProductionRuntimeState(intake),
            ResolvePublicRoutesState(intake),
            ResolveSourceState(intake),
            new MigrationSessionLifecycleCapabilitiesDto(
                CanArchive: !archived && terminal,
                CanUnarchive: archived,
                CanCancel: cancellation.CanCancel,
                CanDelete: deletionDecision.CanDelete,
                CancelBlockedCode: cancellation.BlockedCode,
                CancelBlockedReason: cancellation.BlockedReason,
                DeleteBlockedCode: deletionDecision.BlockedCode,
                DeleteBlockedReason: deletionDecision.BlockedReason),
            SourceUnaffectedNotice);
    }

    internal static MigrationSessionCancellationDto BuildCancellation(
        MigrationIntakeEntity intake,
        DateTime nowUtc)
    {
        var effectiveStatus = MigrationPackageRevisionAuthority.ResolveEffectiveSessionStatus(intake, nowUtc);
        var effectiveLifecycle = ResolveEffectiveLifecycle(intake, effectiveStatus);
        var effectiveClosureKind = ResolveEffectiveClosureKind(intake, effectiveStatus);
        var archived = intake.ArchivedAtUtc.HasValue;
        var hasConversion = intake.ConversionAttempts.Count > 0;
        var hasCandidate = intake.ConversionAttempts.Any(x => x.CandidateArtifact is not null);
        var hasActiveConversion = intake.ConversionAttempts.Any(x =>
            x.ActiveMigrationKey is not null ||
            !IsTerminalConversionStatus(x.Status));
        var retainedStaging = intake.StagingRuns.Any(x =>
            x.ActiveMigrationKey is not null && !x.DestroyedAtUtc.HasValue);
        var stagingHasPublicRoutes = intake.StagingRuns.Any(x => x.PublicRoutesCreated);
        var hasProductionAuthority = intake.ProductionAuthorities.Count > 0;
        var hasProduction =
            stagingHasPublicRoutes ||
            hasProductionAuthority ||
            intake.ProductionAdoption is not null ||
            intake.Acceptance is not null ||
            intake.LegacyRetentionRecord is not null ||
            intake.BaselineBackupHandoff is not null ||
            intake.TwoServerQualification is not null;
        var cancellationStatusEligible = effectiveStatus is
            "awaiting-package" or
            "expired" or
            "package-validated";
        var lifecycleEligible =
            effectiveLifecycle == MigrationSessionLifecycleStatuses.Active ||
            (effectiveLifecycle == MigrationSessionLifecycleStatuses.Closed &&
             string.Equals(
                 effectiveClosureKind,
                 "expired",
                 StringComparison.OrdinalIgnoreCase));
        var canCancel =
            !archived &&
            lifecycleEligible &&
            cancellationStatusEligible &&
            !hasActiveConversion &&
            !retainedStaging &&
            !hasProduction;

        var cancelBlock = ResolveCancelBlock(
            intake,
            effectiveLifecycle,
            archived,
            lifecycleEligible,
            cancellationStatusEligible,
            hasActiveConversion,
            retainedStaging,
            hasProduction);

        // Empty means no recorded upload in ANY revision, including superseded
        // or failed uploads. A validated package must never get the empty prompt,
        // even if some of its evidence is incomplete.
        var emptySession = canCancel &&
            (effectiveStatus is "awaiting-package" or "expired") &&
            !HasPackageEvidence(intake.PackageRevisions) &&
            !intake.PackageRevisions.Any(x => !string.IsNullOrWhiteSpace(x.PackageFileName));

        return new MigrationSessionCancellationDto(
            Math.Max(intake.StateVersion, 1),
            effectiveLifecycle,
            canCancel,
            !canCancel
                ? "unavailable"
                : emptySession
                    ? "empty-session"
                    : hasConversion || hasCandidate
                        ? "progressed"
                        : "package",
            canCancel ? null : cancelBlock.Code,
            canCancel ? null : cancelBlock.Reason);
    }

    private static (string Code, string Reason) ResolveCancelBlock(
        MigrationIntakeEntity intake,
        string effectiveLifecycle,
        bool archived,
        bool lifecycleEligible,
        bool cancellationStatusEligible,
        bool hasActiveConversion,
        bool retainedStaging,
        bool hasProduction)
    {
        if (archived)
        {
            return (
                "migration_session_archived",
                "Restore this Session to the normal list before cancelling it.");
        }

        if (intake.Acceptance is not null ||
            effectiveLifecycle == MigrationSessionLifecycleStatuses.Completed)
        {
            return (
                "migration_session_completed",
                "Accepted or completed Migration Sessions cannot be cancelled.");
        }

        if (hasProduction)
        {
            return (
                "migration_session_production_owned",
                "Production planning, runtime, acceptance, retention, backup, or qualification records now own this Session.");
        }

        if (retainedStaging)
        {
            return (
                "migration_session_staging_retained",
                "Destroy the retained private staging runtime before closing this Session.");
        }

        if (hasActiveConversion)
        {
            return (
                "migration_session_conversion_active",
                "A conversion operation is still active. Wait for it to finish before cancelling this Session.");
        }

        if (!lifecycleEligible)
        {
            return (
                "migration_session_lifecycle_terminal",
                "This Session lifecycle is not eligible for cancellation.");
        }

        if (!cancellationStatusEligible)
        {
            return (
                "migration_session_package_state_not_eligible",
                "Only awaiting, expired, or validated pre-production Sessions can be cancelled here.");
        }

        return (
            "migration_session_cancel_not_allowed",
            "This Migration Session cannot be cancelled from its current state.");
    }

    private static bool IsTerminalConversionStatus(string? status) =>
        status is not null && (
            status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("completed-with-warnings", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("cancelled", StringComparison.OrdinalIgnoreCase));

    private static string ResolveCurrentOperation(MigrationIntakeEntity intake)
    {
        if (intake.ProductionAdoption?.RollbackStartedAtUtc.HasValue == true &&
            !intake.ProductionAdoption.RollbackCompletedAtUtc.HasValue)
        {
            return "rollback";
        }

        if (intake.ProductionAdoption?.CutoverStartedAtUtc.HasValue == true &&
            !intake.ProductionAdoption.CutoverCompletedAtUtc.HasValue)
        {
            return "cutover";
        }

        if (intake.ProductionAdoption?.MaterializationStartedAtUtc.HasValue == true &&
            !intake.ProductionAdoption.MaterializationCompletedAtUtc.HasValue)
        {
            return "materialisation";
        }

        if (intake.StagingRuns.Any(x =>
                x.StartedAtUtc.HasValue &&
                !x.CompletedAtUtc.HasValue &&
                !x.DestroyedAtUtc.HasValue))
        {
            return "staging";
        }

        if (intake.ConversionAttempts.Any(x =>
                x.StartedAtUtc.HasValue &&
                !x.CompletedAtUtc.HasValue))
        {
            return "conversion";
        }

        return "none";
    }

    private static string ResolvePackageState(
        MigrationIntakeEntity intake,
        DateTime nowUtc)
    {
        var current = MigrationPackageRevisionAuthority.ResolveCurrentPackage(
            intake.PackageRevisions);
        if (current is null)
        {
            return intake.PackageRevisions.Count == 0 ? "none" : "retired";
        }

        if (current.ActivePurposeKey is null)
        {
            return "retired";
        }

        return MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
            current,
            nowUtc) switch
        {
            "awaiting-package" => "awaiting",
            "expired" => "expired",
            "package-validated" => "retained",
            "cancelled" => "retired",
            var value => value,
        };
    }

    private static string ResolveProductionRuntimeState(
        MigrationIntakeEntity intake)
    {
        if (intake.Acceptance is not null)
        {
            return "accepted";
        }

        if (intake.ProductionAdoption is null)
        {
            return "none";
        }

        if (intake.ProductionAdoption.TargetPublicAtUtc.HasValue ||
            intake.ProductionAdoption.PublicRoutesCreated)
        {
            return "public";
        }

        if (intake.ProductionAdoption.MaterializationCompletedAtUtc.HasValue)
        {
            return "private";
        }

        return "planned";
    }

    private static string ResolvePublicRoutesState(
        MigrationIntakeEntity intake)
    {
        var adoption = intake.ProductionAdoption;
        if (adoption is null)
        {
            return "none";
        }

        if (adoption.PublicRoutesCreated)
        {
            return "active";
        }

        if (adoption.RollbackRoutesRestored)
        {
            return "restored";
        }

        return adoption.CutoverStartedAtUtc.HasValue ? "unknown" : "none";
    }

    private static string ResolveSourceState(MigrationIntakeEntity intake)
    {
        var revision = MigrationPackageRevisionAuthority.ResolveCurrentPackage(
            intake.PackageRevisions) ??
            intake.PackageRevisions
                .OrderByDescending(x => x.RevisionNumber)
                .FirstOrDefault();

        if (revision?.SourceFrozen == true)
        {
            return "frozen";
        }

        if (revision?.SourceFrozen == false)
        {
            return "running";
        }

        return "external";
    }

    private async Task<MigrationIntakeEntity?> LoadAsync(
        string migrationId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            return null;
        }

        IQueryable<MigrationIntakeEntity> query = db.MigrationIntakes
            .AsSplitQuery()
            .Include(x => x.PackageRevisions)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.CandidateArtifact)
            .Include(x => x.StagingRuns)
            .Include(x => x.ProductionAuthorities)
            .Include(x => x.ProductionAdoption)
            .Include(x => x.Acceptance)
            .Include(x => x.LegacyRetentionRecord)
            .Include(x => x.BaselineBackupHandoff)
            .Include(x => x.TwoServerQualification);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(
            x => x.IntakeId == migrationId,
            cancellationToken);
    }

    private async Task<MigrationIntakeEntity> LoadRequiredAsync(
        string migrationId,
        CancellationToken cancellationToken) =>
        await LoadAsync(migrationId, tracking: true, cancellationToken) ??
        throw new MigrationSessionLifecycleException(
            StatusCodes.Status404NotFound,
            "migration_session_not_found",
            "Migration Session was not found.");

    private static void EnsureExpectedVersion(
        MigrationIntakeEntity intake,
        long expectedStateVersion)
    {
        if (expectedStateVersion < 1 ||
            intake.StateVersion != expectedStateVersion)
        {
            throw Conflict(
                "migration_session_state_stale",
                "The Migration Session changed after this page was loaded. Refresh and review it before trying again.");
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict(
                "migration_session_state_stale",
                "The Migration Session changed while the action was being applied. Refresh and review it before trying again.");
        }
    }

    private MigrationSessionDeletionDecision ResolveDeletionDecision(
        MigrationIntakeEntity intake)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var effectiveStatus = MigrationPackageRevisionAuthority.ResolveEffectiveSessionStatus(
            intake,
            nowUtc);
        var effectiveLifecycle = ResolveEffectiveLifecycle(
            intake,
            effectiveStatus);
        var input = new MigrationSessionDeletionPolicyInput(
            effectiveLifecycle,
            effectiveStatus,
            ResolveEffectiveClosureKind(intake, effectiveStatus),
            intake.PackageRevisions.Count > 0,
            HasPackageEvidence(intake.PackageRevisions),
            HasActivePackageAuthority(intake.PackageRevisions, nowUtc),
            intake.ConversionAttempts.Count > 0,
            intake.StagingRuns.Count > 0,
            intake.ProductionAuthorities.Count > 0,
            intake.ProductionAdoption is not null,
            intake.Acceptance is not null,
            intake.BaselineBackupHandoff is not null,
            intake.LegacyRetentionRecord is not null,
            intake.TwoServerQualification is not null,
            TargetStorageVerified: true,
            TargetPackageMaterialPresent: false,
            TargetDecryptedPackageMaterialPresent: false);

        var databaseDecision = MigrationSessionDeletionPolicy.Evaluate(input);
        if (!databaseDecision.CanDelete)
        {
            return databaseDecision;
        }

        var storage = MigrationSessionTargetStorageInspector.Inspect(
            ResolveDataRoot(),
            intake.IntakeId,
            intake.PackageRevisions
                .Select(x => x.PackageRevisionId)
                .ToArray());
        return MigrationSessionDeletionPolicy.Evaluate(
            input with
            {
                TargetStorageVerified = storage.Verified,
                TargetPackageMaterialPresent = storage.HasPackageMaterial,
                TargetDecryptedPackageMaterialPresent = storage.HasDecryptedPackageMaterial,
            });
    }

    private static string ResolveEffectiveLifecycle(
        MigrationIntakeEntity intake,
        string effectiveStatus)
    {
        if (string.Equals(
                effectiveStatus,
                "cancelled",
                StringComparison.OrdinalIgnoreCase) ||
            intake.CancelledAtUtc.HasValue)
        {
            return MigrationSessionLifecycleStatuses.Cancelled;
        }

        if (string.Equals(
                effectiveStatus,
                "expired",
                StringComparison.OrdinalIgnoreCase))
        {
            return MigrationSessionLifecycleStatuses.Closed;
        }

        if (string.Equals(
                intake.BaselineBackupHandoff?.Status,
                "created",
                StringComparison.OrdinalIgnoreCase))
        {
            return MigrationSessionLifecycleStatuses.Completed;
        }

        return MigrationSessionLifecycleStatuses.All.Contains(
            intake.LifecycleStatus)
            ? intake.LifecycleStatus
            : MigrationSessionLifecycleStatuses.Active;
    }

    private static string? ResolveEffectiveClosureKind(
        MigrationIntakeEntity intake,
        string effectiveStatus)
    {
        if (!string.IsNullOrWhiteSpace(intake.ClosureKind))
        {
            return intake.ClosureKind;
        }

        if (string.Equals(
                effectiveStatus,
                "expired",
                StringComparison.OrdinalIgnoreCase))
        {
            return "expired";
        }

        return null;
    }

    private static DateTime? ResolveEffectiveClosedAtUtc(
        MigrationIntakeEntity intake,
        string effectiveStatus)
    {
        if (intake.ClosedAtUtc.HasValue)
        {
            return intake.ClosedAtUtc;
        }

        if (string.Equals(
                effectiveStatus,
                "expired",
                StringComparison.OrdinalIgnoreCase))
        {
            return intake.PackageRevisions
                .Where(x => x.ExpiresAtUtc.HasValue)
                .OrderByDescending(x => x.RevisionNumber)
                .ThenByDescending(x => x.CreatedAtUtc)
                .Select(x => x.ExpiresAtUtc)
                .FirstOrDefault();
        }

        return intake.CancelledAtUtc;
    }

    private static bool HasPackageEvidence(
        IEnumerable<MigrationPackageRevisionEntity> revisions) =>
        revisions.Any(HasPackageEvidence);

    private static bool HasPackageEvidence(
        MigrationPackageRevisionEntity revision) =>
        revision.UploadedAtUtc.HasValue ||
        revision.ValidatedAtUtc.HasValue ||
        revision.PackageSizeBytes.HasValue ||
        !string.IsNullOrWhiteSpace(revision.EncryptedPackageSha256) ||
        !string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256) ||
        !string.IsNullOrWhiteSpace(revision.ArchiveMigrationId);

    private static bool HasActivePackageAuthority(
        IEnumerable<MigrationPackageRevisionEntity> revisions,
        DateTime nowUtc) =>
        revisions.Any(x =>
        {
            var effectivelyExpiredWithoutEvidence =
                string.Equals(
                    MigrationPackageRevisionAuthority.ResolveEffectiveRevisionStatus(
                        x,
                        nowUtc),
                    "expired",
                    StringComparison.OrdinalIgnoreCase) &&
                !HasPackageEvidence(x);

            return !effectivelyExpiredWithoutEvidence &&
                (x.ActivePurposeKey is not null ||
                 !string.Equals(
                     x.RetentionState,
                     "retired",
                     StringComparison.OrdinalIgnoreCase));
        });

    private async Task<MemOperatorAuditEventEntity?> FindSuccessfulDeletionAuditAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        var successfulAudits = await db.MemOperatorAuditEvents
            .AsNoTracking()
            .Where(x =>
                x.EventType == DeletedAuditEventType &&
                x.CorrelationId == migrationId &&
                x.Outcome == "succeeded")
            .ToListAsync(cancellationToken);

        return successfulAudits
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstOrDefault();
    }

    private void DeleteTargetOwnedSessionMaterial(MigrationIntakeEntity intake)
    {
        try
        {
            MigrationSessionTargetStorageInspector.DeleteVerifiedPackageMaterial(
                ResolveDataRoot(),
                intake.IntakeId,
                intake.PackageRevisions
                    .Select(x => x.PackageRevisionId)
                    .ToArray());
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            ArgumentException or
            NotSupportedException or
            System.Security.SecurityException)
        {
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status503ServiceUnavailable,
                "migration_session_target_cleanup_failed",
                "Target-side Migration Session remnants could not be safely removed. The database Session was retained.");
        }
    }

    private async Task DeleteTargetPackageMaterialAsync(
        MigrationIntakeEntity intake,
        bool removeEncrypted,
        CancellationToken cancellationToken)
    {
        var dataRoot = ResolveDataRoot();

        try
        {
            foreach (var revision in intake.PackageRevisions)
            {
                var decryptedPath =
                    MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                        dataRoot,
                        intake.IntakeId,
                        revision.PackageRevisionId);
                var encryptedPath =
                    MigrationPackageRevisionStorage.ResolveEncryptedArchivePath(
                        dataRoot,
                        intake.IntakeId,
                        revision.PackageRevisionId);

                revision.DecryptedArchiveSha256 ??=
                    await HashFileIfPresentAsync(
                        decryptedPath,
                        cancellationToken);
                revision.EncryptedPackageSha256 ??=
                    await HashFileIfPresentAsync(
                        encryptedPath,
                        cancellationToken);

                DeleteAndVerify(decryptedPath);

                if (removeEncrypted)
                {
                    DeleteAndVerify(encryptedPath);
                }
            }

            var legacyDecryptedPath =
                MigrationPackageRevisionStorage.ResolveLegacyDecryptedArchivePath(
                    dataRoot,
                    intake.IntakeId);
            var legacyEncryptedPath =
                MigrationPackageRevisionStorage.ResolveLegacyEncryptedArchivePath(
                    dataRoot,
                    intake.IntakeId);
            var auditRevision = intake.PackageRevisions
                .OrderByDescending(x => x.RevisionNumber)
                .FirstOrDefault();

            if (auditRevision is not null)
            {
                auditRevision.DecryptedArchiveSha256 ??=
                    await HashFileIfPresentAsync(
                        legacyDecryptedPath,
                        cancellationToken);
                auditRevision.EncryptedPackageSha256 ??=
                    await HashFileIfPresentAsync(
                        legacyEncryptedPath,
                        cancellationToken);
            }

            DeleteAndVerify(legacyDecryptedPath);

            if (removeEncrypted)
            {
                DeleteAndVerify(legacyEncryptedPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            CryptographicException)
        {
            throw new MigrationSessionLifecycleException(
                StatusCodes.Status503ServiceUnavailable,
                "migration_session_package_cleanup_failed",
                "The target package files could not be safely inspected and removed. No lifecycle change was committed.");
        }
    }

    private static async Task<string?> HashFileIfPresentAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteAndVerify(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (File.Exists(path))
        {
            throw new IOException(
                "A server-owned migration package file remained after deletion.");
        }
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static string NormalizeActor(string actor) =>
        string.IsNullOrWhiteSpace(actor)
            ? "operator"
            : actor.Trim()[..Math.Min(actor.Trim().Length, 200)];

    private static MigrationSessionLifecycleException Conflict(
        string code,
        string message) =>
        new(StatusCodes.Status409Conflict, code, message);
}
