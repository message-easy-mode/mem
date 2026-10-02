using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Infrastructure.Persistence.Interceptors;

/// <summary>
/// Keeps the small Migration Session lifecycle spine current whenever durable
/// migration records change. Detailed evidence remains in its owning table;
/// this interceptor updates only root activity, optimistic state version and
/// terminal lifecycle boundaries.
/// </summary>
public sealed class MigrationSessionLifecycleInterceptor(
    TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is MemDbContext db)
        {
            UpdateAsync(db, useAsync: false, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is MemDbContext db)
        {
            await UpdateAsync(db, useAsync: true, cancellationToken);
        }

        return await base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private async Task UpdateAsync(
        MemDbContext db,
        bool useAsync,
        CancellationToken cancellationToken)
    {
        db.ChangeTracker.DetectChanges();

        var changedEntries = db.ChangeTracker.Entries()
            .Where(IsChanged)
            .ToArray();
        if (changedEntries.Length == 0)
        {
            return;
        }

        var intakeIds = new HashSet<Guid>();
        var candidateAttemptIds = new HashSet<Guid>();
        var completedBaselines = new Dictionary<Guid, DateTime>();
        var expiredPackageRequests = new Dictionary<Guid, DateTime>();

        foreach (var entry in changedEntries)
        {
            switch (entry.Entity)
            {
                case MigrationIntakeEntity intake:
                    intakeIds.Add(intake.Id);
                    break;
                case MigrationSourceEntity source:
                    intakeIds.Add(source.MigrationIntakeEntityId);
                    break;
                case MigrationPackageRevisionEntity revision:
                    intakeIds.Add(revision.MigrationIntakeEntityId);
                    if (entry.State != EntityState.Deleted &&
                        string.Equals(
                            revision.Status,
                            MigrationPackageRevisionAuthority.ExpiredStatus,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        expiredPackageRequests[revision.MigrationIntakeEntityId] =
                            revision.ExpiresAtUtc ?? revision.RetiredAtUtc ?? revision.CreatedAtUtc;
                    }

                    break;
                case MigrationConversionAttemptEntity conversion:
                    intakeIds.Add(conversion.MigrationIntakeEntityId);
                    break;
                case MigrationCandidateArtifactEntity candidate:
                    candidateAttemptIds.Add(
                        candidate.MigrationConversionAttemptEntityId);
                    break;
                case MigrationStagingRetirementEntity retirement:
                    intakeIds.Add(retirement.MigrationIntakeEntityId);
                    break;
                case MigrationStagingRunEntity staging:
                    intakeIds.Add(staging.MigrationIntakeEntityId);
                    break;
                case MigrationProductionAuthorityEntity authority:
                    intakeIds.Add(authority.MigrationIntakeEntityId);
                    break;
                case MigrationProductionAdoptionEntity adoption:
                    intakeIds.Add(adoption.MigrationIntakeEntityId);
                    break;
                case MigrationAcceptanceEntity acceptance:
                    intakeIds.Add(acceptance.MigrationIntakeEntityId);
                    break;
                case LegacyRetentionRecordEntity retention:
                    intakeIds.Add(retention.MigrationIntakeEntityId);
                    break;
                case MigrationBaselineBackupHandoffEntity baseline:
                    intakeIds.Add(baseline.MigrationIntakeEntityId);
                    if (string.Equals(
                            baseline.Status,
                            "created",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        completedBaselines[baseline.MigrationIntakeEntityId] =
                            baseline.CompletedAtUtc ??
                            baseline.BackupCreatedAtUtc ??
                            baseline.UpdatedAtUtc;
                    }

                    break;
                case MigrationTwoServerQualificationEntity qualification:
                    intakeIds.Add(qualification.MigrationIntakeEntityId);
                    break;
            }
        }

        if (candidateAttemptIds.Count > 0)
        {
            var candidateIntakeIds = useAsync
                ? await db.MigrationConversionAttempts
                    .AsNoTracking()
                    .Where(x => candidateAttemptIds.Contains(x.Id))
                    .Select(x => x.MigrationIntakeEntityId)
                    .ToListAsync(cancellationToken)
                : db.MigrationConversionAttempts
                    .AsNoTracking()
                    .Where(x => candidateAttemptIds.Contains(x.Id))
                    .Select(x => x.MigrationIntakeEntityId)
                    .ToList();

            intakeIds.UnionWith(candidateIntakeIds);
        }

        if (intakeIds.Count == 0)
        {
            return;
        }

        // A retained retirement is a durable mutation reservation, not a browser lock.
        // This check also protects older/internal workflow entry points. All migration
        // writers participate in the root StateVersion check below, so admission cannot
        // race a production materialisation save and then delete its staging dependency.
        var reservations = useAsync
            ? await db.MigrationStagingRetirements.AsNoTracking()
                .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId) && x.Status != "retired")
                .ToArrayAsync(cancellationToken)
            : db.MigrationStagingRetirements.AsNoTracking()
                .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId) && x.Status != "retired")
                .ToArray();
        foreach (var reservation in reservations)
        {
            var retirementWrite = changedEntries.Any(x =>
                x.Entity is MigrationStagingRetirementEntity operation && operation.Id == reservation.Id);
            foreach (var entry in changedEntries)
            {
                var ownerId = MigrationOwnerId(entry.Entity);
                if (entry.Entity is MigrationCandidateArtifactEntity artifact)
                {
                    var attempt = useAsync
                        ? await db.MigrationConversionAttempts.AsNoTracking().FirstOrDefaultAsync(
                            x => x.Id == artifact.MigrationConversionAttemptEntityId, cancellationToken)
                        : db.MigrationConversionAttempts.AsNoTracking().FirstOrDefault(
                            x => x.Id == artifact.MigrationConversionAttemptEntityId);
                    ownerId = attempt?.MigrationIntakeEntityId;
                }
                if (ownerId != reservation.MigrationIntakeEntityId) continue;
                var ownRetirement = entry.Entity is MigrationStagingRetirementEntity op && op.Id == reservation.Id;
                var ownRun = retirementWrite && entry.Entity is MigrationStagingRunEntity run && run.Id == reservation.MigrationStagingRunEntityId;
                if (!ownRetirement && !ownRun)
                    throw new InvalidOperationException("Migration resource retirement is pending or needs attention. Resolve that operation before changing this Migration Session.");
            }
        }

        var trackedRoots = db.ChangeTracker
            .Entries<MigrationIntakeEntity>()
            .Where(x => intakeIds.Contains(x.Entity.Id))
            .ToDictionary(x => x.Entity.Id, x => x);

        var missingIds = intakeIds
            .Where(x => !trackedRoots.ContainsKey(x))
            .ToArray();
        if (missingIds.Length > 0)
        {
            if (useAsync)
            {
                await db.MigrationIntakes
                    .Where(x => missingIds.Contains(x.Id))
                    .LoadAsync(cancellationToken);
            }
            else
            {
                db.MigrationIntakes
                    .Where(x => missingIds.Contains(x.Id))
                    .Load();
            }

            trackedRoots = db.ChangeTracker
                .Entries<MigrationIntakeEntity>()
                .Where(x => intakeIds.Contains(x.Entity.Id))
                .ToDictionary(x => x.Entity.Id, x => x);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var pair in trackedRoots)
        {
            var entry = pair.Value;
            if (entry.State == EntityState.Deleted)
            {
                continue;
            }

            var root = entry.Entity;
            if (entry.State == EntityState.Added)
            {
                root.UpdatedAtUtc = root.UpdatedAtUtc == default
                    ? root.CreatedAtUtc == default
                        ? now
                        : root.CreatedAtUtc
                    : root.UpdatedAtUtc;
                root.StateVersion = Math.Max(root.StateVersion, 1);
            }
            else
            {
                root.UpdatedAtUtc = now;
                root.StateVersion = Math.Max(root.StateVersion, 1) + 1;
            }

            ApplyLifecycle(
                root,
                completedBaselines.GetValueOrDefault(pair.Key),
                expiredPackageRequests.GetValueOrDefault(pair.Key),
                now);

            if (entry.State != EntityState.Added)
            {
                entry.Property(x => x.UpdatedAtUtc).IsModified = true;
                entry.Property(x => x.StateVersion).IsModified = true;
                entry.Property(x => x.LifecycleStatus).IsModified = true;
                entry.Property(x => x.ClosedAtUtc).IsModified = true;
                entry.Property(x => x.ClosureKind).IsModified = true;
            }
        }
    }

    private static void ApplyLifecycle(
        MigrationIntakeEntity root,
        DateTime completedBaselineAtUtc,
        DateTime expiredPackageAtUtc,
        DateTime now)
    {
        if (string.Equals(
                root.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Cancelled,
                StringComparison.Ordinal) ||
            root.CancelledAtUtc.HasValue)
        {
            root.LifecycleStatus = MigrationSessionLifecycleStatuses.Cancelled;
            root.ClosedAtUtc ??= root.CancelledAtUtc ?? now;
            root.ClosureKind ??= "cancelled";
            return;
        }

        if (completedBaselineAtUtc != default)
        {
            root.LifecycleStatus = MigrationSessionLifecycleStatuses.Completed;
            root.ClosedAtUtc ??= completedBaselineAtUtc;
            root.ClosureKind ??= "accepted-baseline-created";
            return;
        }

        if (expiredPackageAtUtc != default)
        {
            root.LifecycleStatus = MigrationSessionLifecycleStatuses.Closed;
            root.ClosedAtUtc ??= expiredPackageAtUtc;
            root.ClosureKind ??= "expired";
            return;
        }

        if (!MigrationSessionLifecycleStatuses.All.Contains(
                root.LifecycleStatus))
        {
            root.LifecycleStatus = MigrationSessionLifecycleStatuses.Active;
        }
    }

    private static Guid? MigrationOwnerId(object entity) => entity switch
    {
        MigrationIntakeEntity x => x.Id,
        MigrationSourceEntity x => x.MigrationIntakeEntityId,
        MigrationPackageRevisionEntity x => x.MigrationIntakeEntityId,
        MigrationConversionAttemptEntity x => x.MigrationIntakeEntityId,
        MigrationStagingRunEntity x => x.MigrationIntakeEntityId,
        MigrationStagingRetirementEntity x => x.MigrationIntakeEntityId,
        MigrationProductionAuthorityEntity x => x.MigrationIntakeEntityId,
        MigrationProductionAdoptionEntity x => x.MigrationIntakeEntityId,
        MigrationAcceptanceEntity x => x.MigrationIntakeEntityId,
        LegacyRetentionRecordEntity x => x.MigrationIntakeEntityId,
        MigrationBaselineBackupHandoffEntity x => x.MigrationIntakeEntityId,
        MigrationTwoServerQualificationEntity x => x.MigrationIntakeEntityId,
        _ => null
    };

    private static bool IsChanged(EntityEntry entry) =>
        entry.State is EntityState.Added
            or EntityState.Modified
            or EntityState.Deleted;
}
