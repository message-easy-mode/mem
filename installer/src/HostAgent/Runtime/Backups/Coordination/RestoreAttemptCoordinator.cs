using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.StandardRecreate;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.Coordination;

/// <summary>
/// Single authority for active restore ownership. Endpoint adapters and restore
/// implementations must use this service rather than inventing lock files or
/// independently writing restore-attempt state.
/// </summary>
public sealed class RestoreAttemptCoordinator
{
    private readonly MemDbContext _db;
    private readonly RestoreAttemptWorkspaceStore _workspaceStore;
    private readonly RuntimeOperationStore _operationStore;
    private readonly RestoreStructuredLogService _restoreLogs;
    private readonly ILogger<RestoreAttemptCoordinator> _logger;

    public RestoreAttemptCoordinator(
        MemDbContext db,
        RestoreAttemptWorkspaceStore workspaceStore,
        RuntimeOperationStore operationStore,
        RestoreStructuredLogService restoreLogs,
        ILogger<RestoreAttemptCoordinator> logger)
    {
        _db = db;
        _workspaceStore = workspaceStore;
        _operationStore = operationStore;
        _restoreLogs = restoreLogs;
        _logger = logger;
    }


    /// <summary>
    /// Creates or resumes the single active restore attempt for one canonical
    /// managed Backup Catalog entry.
    /// </summary>
    public Task<RestoreAttemptGetOrCreateResult> GetOrCreateBackupCatalogAsync(
        BackupCatalogRestoreSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        return GetOrCreateAsync(
            RestoreSourceKeyFactory.ForBackupCatalog(
                source.CatalogEntryId,
                source.EntryId,
                source.DisplayName,
                source.OriginKind,
                source.SourceStackSlug,
                source.SourceBackupId),
            initialStatus: RestoreAttemptStatuses.Ready,
            initialStage: RestoreAttemptStages.BackupReady,
            ct);
    }


    public async Task<RestoreAttemptSnapshot?> GetByRestoreSessionIdAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        var entity = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestoreSessionId == normalizedRestoreSessionId, ct);

        return entity is null ? null : ToSnapshot(entity);
    }


    /// <summary>
    /// Read-only lifecycle helper for an existing local capture. New attempts
    /// still originate only from its registered Backup Catalog entry.
    /// </summary>
    public async Task<RestoreAttemptSnapshot?> GetActiveLocalBackupAttemptAsync(
        string stackSlug,
        string backupId,
        CancellationToken ct)
    {
        var normalizedStackSlug = RestoreSourceKeyFactory.NormalizePathSegment(
            stackSlug,
            "Stack slug is required.");
        var normalizedBackupId = RestoreSourceKeyFactory.NormalizePathSegment(
            backupId,
            "Backup id is required.");

        var entity = await _db.RestoreAttempts
            .AsNoTracking()
            .Where(x => x.ActiveSourceKey != null &&
                        x.SourceOriginKindSnapshot == BackupCatalogOriginKinds.LocalCaptured &&
                        x.SourceStackSlugSnapshot == normalizedStackSlug &&
                        x.SourceBackupIdSnapshot == normalizedBackupId)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return entity is null ? null : ToSnapshot(entity);
    }

    /// <summary>
    /// Resolves active attempts for a batch of already-canonical source keys.
    /// Inventory projections use this to avoid one SQLite query per source row.
    /// The result contains only attempts that still own an active source key.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, RestoreAttemptSnapshot>> GetActiveAttemptsBySourceKeysAsync(
        IEnumerable<string> sourceKeys,
        CancellationToken ct)
    {
        var normalizedSourceKeys = sourceKeys
            .Where(sourceKey => !string.IsNullOrWhiteSpace(sourceKey))
            .Select(sourceKey => sourceKey.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSourceKeys.Length == 0)
        {
            return new Dictionary<string, RestoreAttemptSnapshot>(
                StringComparer.Ordinal);
        }

        var attempts = await _db.RestoreAttempts
            .AsNoTracking()
            .Where(attempt =>
                attempt.ActiveSourceKey != null &&
                normalizedSourceKeys.Contains(attempt.ActiveSourceKey))
            .OrderByDescending(attempt => attempt.UpdatedAtUtc)
            .ToListAsync(ct);

        return attempts
            .Where(attempt => !string.IsNullOrWhiteSpace(attempt.ActiveSourceKey))
            .GroupBy(attempt => attempt.ActiveSourceKey!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => ToSnapshot(group.First()),
                StringComparer.Ordinal);
    }


    /// <summary>
    /// Performs the read-only part of Standard Recreate target assessment for a
    /// canonical restore attempt. It deliberately does not create claims or
    /// change the restore state; execution repeats the checks and reserves the
    /// targets atomically to close the race between assessment and creation.
    /// </summary>
    public async Task<RestoreStandardRecreateTargetAssessment?> AssessStandardRecreateTargetsAsync(
        string restoreSessionId,
        string targetStackSlug,
        string matrixHost,
        string elementHost,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        var attemptEntity = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestoreSessionId == normalizedRestoreSessionId, ct);

        if (attemptEntity is null)
        {
            return null;
        }

        var attempt = ToSnapshot(attemptEntity);
        var resources = BuildStandardRecreateTargetResources(
            targetStackSlug,
            matrixHost,
            elementHost);
        var claimKeys = resources
            .Select(resource => resource.ActiveClaimKey)
            .ToArray();

        var checks = new List<RestoreStandardRecreateTargetAssessmentCheck>();
        var blockers = new List<string>();

        var attemptReady =
            string.Equals(attempt.Status, RestoreAttemptStatuses.Ready, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attempt.Status, RestoreAttemptStatuses.NeedsAttention, StringComparison.OrdinalIgnoreCase);
        AddAssessmentCheck(
            checks,
            blockers,
            "restore-attempt-ready",
            attemptReady,
            attemptReady
                ? "This restore is ready to create a restored chat server."
                : $"This restore is currently '{attempt.Status}' and cannot start another create operation.");

        var activeOperation = await _db.RuntimeOperations
            .AsNoTracking()
            .AnyAsync(
                operation => operation.RestoreAttemptId == attempt.Id &&
                             (operation.Status == "queued" || operation.Status == "running"),
                ct);

        AddAssessmentCheck(
            checks,
            blockers,
            "restore-operation-idle",
            !activeOperation,
            activeOperation
                ? "A restore operation is already running. Continue that restore instead of starting another one."
                : "No create operation is currently running for this restore.");

        var attemptClaims = await _db.RestoreTargetClaims
            .AsNoTracking()
            .Where(claim => claim.RestoreAttemptId == attempt.Id && claim.ActiveClaimKey != null)
            .ToListAsync(ct);

        var previouslySelectedTarget = attemptClaims
            .FirstOrDefault(claim => !claimKeys.Contains(claim.ActiveClaimKey!));

        AddAssessmentCheck(
            checks,
            blockers,
            "restore-target-selection-compatible",
            previouslySelectedTarget is null,
            previouslySelectedTarget is null
                ? "The selected targets are compatible with this restore."
                : $"This restore already reserved {ToFriendlyResourceName(previouslySelectedTarget.ResourceType).ToLowerInvariant()} '{previouslySelectedTarget.ResourceValue}'. Continue with that target or resolve the existing restore first.");

        var stackResource = resources.Single(resource =>
            resource.ResourceType == RestoreTargetResourceTypes.StackSlug);
        var hostResources = resources
            .Where(resource => resource.ResourceType is RestoreTargetResourceTypes.MatrixHost or RestoreTargetResourceTypes.ElementHost)
            .ToArray();

        var stackRows = await _db.RuntimeStacks
            .AsNoTracking()
            .Select(stack => new { stack.Id, stack.Slug })
            .ToListAsync(ct);
        var existingStack = stackRows.FirstOrDefault(stack =>
            string.Equals(stack.Slug, stackResource.ResourceValue, StringComparison.OrdinalIgnoreCase));

        AddAssessmentCheck(
            checks,
            blockers,
            "target-stack-runtime-available",
            existingStack is null,
            existingStack is null
                ? $"Target stack '{stackResource.ResourceValue}' is not owned by an existing MEM runtime stack."
                : $"Target stack '{stackResource.ResourceValue}' is already owned by an existing MEM runtime stack.");

        var publicRoutes = await _db.RuntimeRoutes
            .AsNoTracking()
            .Where(route => route.IsPublic)
            .Select(route => new { route.RuntimeStackId, route.PublicHost })
            .ToListAsync(ct);

        foreach (var hostResource in hostResources)
        {
            var existingRoute = publicRoutes.FirstOrDefault(route =>
                string.Equals(route.PublicHost, hostResource.ResourceValue, StringComparison.OrdinalIgnoreCase));

            AddAssessmentCheck(
                checks,
                blockers,
                hostResource.ResourceType == RestoreTargetResourceTypes.MatrixHost
                    ? "matrix-host-runtime-available"
                    : "element-host-runtime-available",
                existingRoute is null,
                existingRoute is null
                    ? $"{ToFriendlyResourceName(hostResource.ResourceType)} '{hostResource.ResourceValue}' is not owned by an existing MEM runtime route."
                    : $"{ToFriendlyResourceName(hostResource.ResourceType)} '{hostResource.ResourceValue}' is already owned by an existing MEM runtime route.");
        }

        var activeClaims = await _db.RestoreTargetClaims
            .AsNoTracking()
            .Include(claim => claim.RestoreAttempt)
            .Where(claim => claim.ActiveClaimKey != null && claimKeys.Contains(claim.ActiveClaimKey))
            .ToListAsync(ct);

        foreach (var resource in resources)
        {
            var conflictingClaim = activeClaims.FirstOrDefault(claim =>
                claim.RestoreAttemptId != attempt.Id &&
                string.Equals(claim.ActiveClaimKey, resource.ActiveClaimKey, StringComparison.Ordinal));

            var code = resource.ResourceType switch
            {
                RestoreTargetResourceTypes.StackSlug => "target-stack-claim-available",
                RestoreTargetResourceTypes.MatrixHost => "matrix-host-claim-available",
                RestoreTargetResourceTypes.ElementHost => "element-host-claim-available",
                _ => "restore-target-claim-available"
            };

            var message = conflictingClaim is null
                ? $"{ToFriendlyResourceName(resource.ResourceType)} '{resource.ResourceValue}' is not reserved by another active restore."
                : CreateTargetClaimConflict(attempt, conflictingClaim).Conflict.Detail;

            AddAssessmentCheck(
                checks,
                blockers,
                code,
                conflictingClaim is null,
                message);
        }

        return new RestoreStandardRecreateTargetAssessment(
            attempt,
            resources,
            checks,
            blockers.Distinct(StringComparer.Ordinal).ToArray());
    }


    /// <summary>
    /// Reserves selected Standard Recreate targets for an existing canonical
    /// Backup Catalog restore workspace. This never constructs a validation-
    /// shaped attempt or treats catalog identity as a validation receipt.
    /// </summary>
    public async Task<RestoreStandardRecreateReservation>
        ReserveTargetsAndStartStandardRecreateForCatalogAsync(
            string restoreSessionId,
            Guid backupCatalogEntryId,
            string targetStackSlug,
            string matrixHost,
            string elementHost,
            string recreateId,
            string? requestedBy,
            string? note,
            CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        if (backupCatalogEntryId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Backup Catalog entry identity is required.");
        }

        var attempt = await GetByRestoreSessionIdAsync(
            normalizedRestoreSessionId,
            ct) ?? throw new InvalidOperationException(
                $"Restore attempt '{normalizedRestoreSessionId}' was not found.");

        return await ReserveTargetsAndStartStandardRecreateCoreAsync(
            attempt,
            new StandardRecreateReservationSource(
                SourceKind: "backup-catalog",
                BackupCatalogEntryId: backupCatalogEntryId),
            targetStackSlug,
            matrixHost,
            elementHost,
            recreateId,
            requestedBy,
            note,
            ct);
    }

    private async Task<RestoreStandardRecreateReservation>
        ReserveTargetsAndStartStandardRecreateCoreAsync(
            RestoreAttemptSnapshot initialAttempt,
            StandardRecreateReservationSource expectedSource,
            string targetStackSlug,
            string matrixHost,
            string elementHost,
            string recreateId,
            string? requestedBy,
            string? note,
            CancellationToken ct)
    {
        var normalizedRecreateId = RestoreSourceKeyFactory.NormalizePathSegment(
            recreateId,
            "Recreate id is required.");
        var resources = BuildStandardRecreateTargetResources(
            targetStackSlug,
            matrixHost,
            elementHost);
        var claimKeys = resources
            .Select(x => x.ActiveClaimKey)
            .ToArray();
        var now = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // This conditional update is the per-attempt operation latch. A second
            // request cannot start a second Standard Recreate after the first one
            // changes the attempt to `recreating` and links its operation.
            var transitionCount = await _db.RestoreAttempts
                .Where(x => x.Id == initialAttempt.Id &&
                            x.ActiveSourceKey != null &&
                            (x.Status == RestoreAttemptStatuses.Ready ||
                             x.Status == RestoreAttemptStatuses.NeedsAttention))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, RestoreAttemptStatuses.Recreating)
                    .SetProperty(x => x.CurrentStage, RestoreAttemptStages.CreateRestoredChatServer)
                    .SetProperty(x => x.UpdatedAtUtc, now)
                    .SetProperty(x => x.LastEventAtUtc, now)
                    .SetProperty(x => x.LastErrorCode, (string?)null)
                    .SetProperty(x => x.LastErrorSummary, (string?)null), ct);

            // ExecuteUpdate bypasses the change tracker. Clear any entity that
            // may have been added or read by a source-specific preparation step
            // so the subsequent query observes the persisted state.
            _db.ChangeTracker.Clear();

            if (transitionCount != 1)
            {
                var current = await _db.RestoreAttempts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == initialAttempt.Id, ct)
                    ?? throw new InvalidOperationException("Restore attempt was not found.");

                var runningOperation = await _db.RuntimeOperations
                    .AsNoTracking()
                    .AnyAsync(
                        x => x.RestoreAttemptId == current.Id &&
                             (x.Status == "queued" || x.Status == "running"),
                        ct);

                if (runningOperation ||
                    string.Equals(
                        current.Status,
                        RestoreAttemptStatuses.Recreating,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw CreateOperationInProgressConflict(current);
                }

                if (RestoreAttemptStatuses.IsTerminal(current.Status) ||
                    current.ActiveSourceKey is null)
                {
                    throw new InvalidOperationException(
                        "A terminal restore attempt cannot start Standard Recreate. Start another restore deliberately first.");
                }

                throw new InvalidOperationException(
                    $"Restore attempt '{current.RestoreSessionId}' is not ready to create a restored chat server. Current status is '{current.Status}'.");
            }

            var attempt = await _db.RestoreAttempts
                .FirstOrDefaultAsync(x => x.Id == initialAttempt.Id, ct)
                ?? throw new InvalidOperationException("Restore attempt was not found.");

            ValidateStandardRecreateReservationSource(
                attempt,
                expectedSource);

            await ThrowIfLiveRuntimeOwnsAnyTargetAsync(attempt, resources, ct);

            var attemptActiveClaims = await _db.RestoreTargetClaims
                .Where(x => x.RestoreAttemptId == attempt.Id &&
                            x.ActiveClaimKey != null)
                .ToListAsync(ct);

            var previouslySelectedTarget = attemptActiveClaims
                .FirstOrDefault(x => !claimKeys.Contains(x.ActiveClaimKey!));

            if (previouslySelectedTarget is not null)
            {
                throw CreateTargetSelectionConflict(
                    attempt,
                    previouslySelectedTarget);
            }

            var activeClaims = await _db.RestoreTargetClaims
                .Include(x => x.RestoreAttempt)
                .Where(x => x.ActiveClaimKey != null &&
                            claimKeys.Contains(x.ActiveClaimKey))
                .ToListAsync(ct);

            var conflictingClaim = activeClaims
                .FirstOrDefault(x => x.RestoreAttemptId != attempt.Id);

            if (conflictingClaim is not null)
            {
                throw CreateTargetClaimConflict(attempt, conflictingClaim);
            }

            var claimsByKey = activeClaims
                .Where(x => x.RestoreAttemptId == attempt.Id &&
                            x.ActiveClaimKey is not null)
                .ToDictionary(
                    x => x.ActiveClaimKey!,
                    StringComparer.Ordinal);

            foreach (var resource in resources)
            {
                if (claimsByKey.ContainsKey(resource.ActiveClaimKey))
                {
                    continue;
                }

                var claim = new RestoreTargetClaimEntity
                {
                    Id = Guid.NewGuid(),
                    RestoreAttemptId = attempt.Id,
                    ResourceType = resource.ResourceType,
                    ResourceValue = resource.ResourceValue,
                    ActiveClaimKey = resource.ActiveClaimKey,
                    ClaimedAtUtc = now,
                    ReleasedAtUtc = null,
                    ReleaseReason = null
                };

                _db.RestoreTargetClaims.Add(claim);
                claimsByKey[resource.ActiveClaimKey] = claim;
            }

            await _db.SaveChangesAsync(ct);

            var operationId = await _operationStore.StartAsync(
                runtimeStackId: null,
                operation: "restore.standard-recreate",
                idempotencyKey: $"restore.standard-recreate:{attempt.Id:N}:{normalizedRecreateId}",
                requestedBy: NormalizeRequestedBy(requestedBy),
                hostMutationLevel: "filesystem,postgres-write,docker,npm",
                input: new
                {
                    RestoreSessionId = attempt.RestoreSessionId,
                    SourceKind = expectedSource.SourceKind,
                    BackupCatalogEntryId = expectedSource.BackupCatalogEntryId,
                    RecreateId = normalizedRecreateId,
                    TargetStackSlug = resources.Single(
                        x => x.ResourceType ==
                             RestoreTargetResourceTypes.StackSlug).ResourceValue,
                    MatrixHost = resources.Single(
                        x => x.ResourceType ==
                             RestoreTargetResourceTypes.MatrixHost).ResourceValue,
                    ElementHost = resources.Single(
                        x => x.ResourceType ==
                             RestoreTargetResourceTypes.ElementHost).ResourceValue,
                    Note = Trim(note, 1000)
                },
                ct: ct,
                restoreAttemptId: attempt.Id);

            attempt.RuntimeOperationId = operationId;
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await _operationStore.UpdateStepAsync(
                operationId,
                "targets-reserved",
                ct);

            var snapshot = ToSnapshot(attempt);
            await EnsureWorkspaceOrMarkNeedsAttentionAsync(snapshot, ct);
            await _restoreLogs.RecordAsync(
                snapshot.Id,
                operationId,
                stage: RestoreAttemptStages.CreateRestoredChatServer,
                severity: RestoreLogSeverities.Information,
                eventCode: "restore.standard-recreate.targets-reserved",
                message: "Restore targets were reserved and the create-restored-chat-server operation started.",
                details: CreateNonEmptyDetails(
                    ("sourceKind", expectedSource.SourceKind),
                    ("backupCatalogEntryId",
                        expectedSource.BackupCatalogEntryId?.ToString()),
                    ("recreateId", normalizedRecreateId),
                    ("targetStackSlug", resources.Single(
                        x => x.ResourceType ==
                             RestoreTargetResourceTypes.StackSlug).ResourceValue),
                    ("matrixHost", resources.Single(
                        x => x.ResourceType ==
                             RestoreTargetResourceTypes.MatrixHost).ResourceValue),
                    ("elementHost", resources.Single(
                        x => x.ResourceType ==
                             RestoreTargetResourceTypes.ElementHost).ResourceValue)),
                ct: ct,
                updateAttemptSummary: false);

            return new RestoreStandardRecreateReservation(
                snapshot,
                operationId,
                claimsByKey.Values
                    .OrderBy(x => x.ResourceType, StringComparer.Ordinal)
                    .Select(ToClaimSnapshot)
                    .ToArray());
        }
        catch (DbUpdateException ex) when (IsUniqueConstraint(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();

            var activeClaims = await _db.RestoreTargetClaims
                .AsNoTracking()
                .Include(x => x.RestoreAttempt)
                .Where(x => x.ActiveClaimKey != null &&
                            claimKeys.Contains(x.ActiveClaimKey))
                .ToListAsync(ct);

            var winner = activeClaims
                .FirstOrDefault(
                    x => x.RestoreAttemptId != initialAttempt.Id);

            if (winner is not null)
            {
                throw CreateTargetClaimConflict(initialAttempt, winner);
            }

            throw;
        }
    }


    private static void ValidateStandardRecreateReservationSource(
        RestoreAttemptEntity attempt,
        StandardRecreateReservationSource expectedSource)
    {
        if (!string.Equals(
                expectedSource.SourceKind,
                "backup-catalog",
                StringComparison.OrdinalIgnoreCase) ||
            expectedSource.BackupCatalogEntryId is null ||
            expectedSource.BackupCatalogEntryId == Guid.Empty)
        {
            throw CreateCatalogOnlyException();
        }

        if (!string.Equals(
                attempt.SourceKind,
                "backup-catalog",
                StringComparison.OrdinalIgnoreCase) ||
            attempt.BackupCatalogEntryId != expectedSource.BackupCatalogEntryId)
        {
            throw new InvalidOperationException(
                "The active restore attempt is not linked to the requested Backup Catalog entry.");
        }
    }

    private sealed record StandardRecreateReservationSource(
        string SourceKind,
        Guid? BackupCatalogEntryId);

    public async Task<RestorePrivateTestReservation> StartPrivateTestAsync(
        string restoreSessionId,
        string? requestedBy,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        var initial = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.RestoreSessionId == normalizedRestoreSessionId,
                ct)
            ?? throw new InvalidOperationException(
                $"Restore attempt '{normalizedRestoreSessionId}' was not found.");

        if (!string.Equals(
                initial.SourceKind,
                "backup-catalog",
                StringComparison.OrdinalIgnoreCase) ||
            initial.BackupCatalogEntryId is null)
        {
            throw new InvalidOperationException(
                "Private testing from the Restore Workspace currently requires a canonical Backup Catalog source.");
        }

        if (RestoreAttemptStatuses.IsTerminal(initial.Status) || initial.ActiveSourceKey is null)
        {
            throw new InvalidOperationException(
                "A terminal restore attempt cannot start a private test. Start another restore deliberately first.");
        }

        Guid? retryAfterOperationId = null;
        if (string.Equals(
                initial.Status,
                RestoreAttemptStatuses.NeedsAttention,
                StringComparison.OrdinalIgnoreCase))
        {
            if (initial.RuntimeOperationId is null)
            {
                throw new InvalidOperationException(
                    "This restore needs attention for a reason other than a private test. Resolve that issue before retrying private testing.");
            }

            var failedPrivateTest = await _db.RuntimeOperations
                .AsNoTracking()
                .AnyAsync(
                    x => x.Id == initial.RuntimeOperationId.Value &&
                         x.RestoreAttemptId == initial.Id &&
                         x.Operation == "restore.private-test" &&
                         x.Status == "failed",
                    ct);

            if (!failedPrivateTest)
            {
                throw new InvalidOperationException(
                    "This restore needs attention for a reason other than a failed private test. Resolve that issue before retrying private testing.");
            }

            retryAfterOperationId = initial.RuntimeOperationId;
        }
        else if (!string.Equals(
                     initial.Status,
                     RestoreAttemptStatuses.Ready,
                     StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(
                    initial.Status,
                    RestoreAttemptStatuses.Testing,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new RestoreAttemptConflictException(new RestoreAttemptConflictResponse(
                    Code: "restore_private_test_in_progress",
                    RestoreSessionId: initial.RestoreSessionId,
                    ResourceType: "restore-operation",
                    ResourceValue: "restore.private-test",
                    Detail: $"Restore '{initial.RestoreSessionId}' already has a private test in progress. Continue that test instead of starting another one."));
            }

            throw new InvalidOperationException(
                $"Restore attempt '{initial.RestoreSessionId}' is not ready to run a private test. Current status is '{initial.Status}'.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;

        var transitionCount = await _db.RestoreAttempts
            .Where(x => x.Id == initial.Id &&
                        x.ActiveSourceKey != null &&
                        (x.Status == RestoreAttemptStatuses.Ready ||
                         (retryAfterOperationId.HasValue &&
                          x.Status == RestoreAttemptStatuses.NeedsAttention &&
                          x.RuntimeOperationId == retryAfterOperationId)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, RestoreAttemptStatuses.Testing)
                .SetProperty(x => x.CurrentStage, RestoreAttemptStages.PrivateTest)
                .SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.LastEventAtUtc, now)
                .SetProperty(x => x.LastErrorCode, (string?)null)
                .SetProperty(x => x.LastErrorSummary, (string?)null), ct);

        _db.ChangeTracker.Clear();

        if (transitionCount != 1)
        {
            var current = await _db.RestoreAttempts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == initial.Id, ct)
                ?? throw new InvalidOperationException("Restore attempt was not found.");

            if (string.Equals(
                    current.Status,
                    RestoreAttemptStatuses.Testing,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new RestoreAttemptConflictException(new RestoreAttemptConflictResponse(
                    Code: "restore_private_test_in_progress",
                    RestoreSessionId: current.RestoreSessionId,
                    ResourceType: "restore-operation",
                    ResourceValue: "restore.private-test",
                    Detail: $"Restore '{current.RestoreSessionId}' already has a private test in progress. Continue that test instead of starting another one."));
            }

            throw new InvalidOperationException(
                $"Restore attempt '{current.RestoreSessionId}' is not ready to run a private test. Current status is '{current.Status}'.");
        }

        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == initial.Id, ct)
            ?? throw new InvalidOperationException("Restore attempt was not found.");

        var operationId = await _operationStore.StartAsync(
            runtimeStackId: null,
            operation: "restore.private-test",
            idempotencyKey: $"restore.private-test:{attempt.Id:N}:{Guid.NewGuid():N}",
            requestedBy: NormalizeRequestedBy(requestedBy),
            hostMutationLevel: "filesystem,postgres-write,docker-private",
            input: new
            {
                RestoreSessionId = attempt.RestoreSessionId,
                SourceKind = attempt.SourceKind,
                BackupCatalogEntryId = attempt.BackupCatalogEntryId
            },
            ct: ct,
            restoreAttemptId: attempt.Id);

        attempt.RuntimeOperationId = operationId;
        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var snapshot = ToSnapshot(attempt);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(snapshot, ct);
        await _restoreLogs.RecordAsync(
            snapshot.Id,
            operationId,
            stage: RestoreAttemptStages.PrivateTest,
            severity: RestoreLogSeverities.Information,
            eventCode: "restore.private-test.requested",
            message: "Private restore test was requested from the Restore Workspace.",
            details: CreateNonEmptyDetails(
                ("sourceKind", snapshot.SourceKind),
                ("backupCatalogEntryId", snapshot.BackupCatalogEntryId?.ToString())),
            ct: ct,
            updateAttemptSummary: false);

        return new RestorePrivateTestReservation(snapshot, operationId);
    }

    /// <summary>
    /// Completes a private-test operation and returns the restore workspace to
    /// its ready state. The isolated staging environment remains retained for
    /// operator inspection until it is explicitly destroyed.
    /// </summary>
    public async Task CompletePrivateTestAsync(
        Guid restoreAttemptId,
        Guid runtimeOperationId,
        RestorePrivateTestEvidence evidence,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct)
            ?? throw new InvalidOperationException("Restore attempt was not found.");

        if (!string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Testing,
                StringComparison.OrdinalIgnoreCase) ||
            attempt.RuntimeOperationId != runtimeOperationId)
        {
            throw new InvalidOperationException(
                "Private test completion does not match the current restore workspace operation.");
        }

        await _operationStore.CompleteAsync(
            runtimeOperationId,
            status: "succeeded",
            currentStep: "private-staging-ready",
            result: evidence,
            evidence: evidence,
            ct: ct);

        var now = DateTime.UtcNow;
        attempt.Status = RestoreAttemptStatuses.Ready;
        attempt.CurrentStage = RestoreAttemptStages.BackupReady;
        attempt.RuntimeOperationId = runtimeOperationId;
        attempt.UpdatedAtUtc = now;
        attempt.LastEventAtUtc = now;
        attempt.LastErrorCode = null;
        attempt.LastErrorSummary = null;

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var completedSnapshot = ToSnapshot(attempt);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(completedSnapshot, ct);
        await _restoreLogs.RecordAsync(
            completedSnapshot.Id,
            runtimeOperationId,
            stage: RestoreAttemptStages.PrivateTest,
            severity: RestoreLogSeverities.Information,
            eventCode: "restore.private-test.passed",
            message: "Private restore test completed with an isolated healthy staging runtime.",
            details: CreateNonEmptyDetails(
                ("sourceKind", evidence.SourceKind),
                ("catalogEntryId", evidence.CatalogEntryId),
                ("stagingId", evidence.StagingId),
                ("databaseImportSucceeded", evidence.DatabaseImportSucceeded ? "true" : "false"),
                ("synapseHealthPassed", evidence.SynapseHealthPassed ? "true" : "false"),
                ("requiresExplicitDestroy", evidence.RequiresExplicitDestroy ? "true" : "false")),
            ct: ct,
            updateAttemptSummary: false);
    }

    /// <summary>
    /// Records a safe private-test failure without losing the durable restore
    /// workspace, operation history, or source ownership. Only a curated
    /// operator summary is retained on the attempt; raw exception text must not
    /// be placed in logs or workspace evidence.
    /// </summary>
    public async Task RecordPrivateTestFailureAsync(
        Guid restoreAttemptId,
        Guid runtimeOperationId,
        string errorCode,
        string operatorSummary,
        RestorePrivateTestEvidence? evidence,
        CancellationToken ct)
    {
        var normalizedErrorCode = Trim(errorCode, 200);
        var normalizedOperatorSummary = Trim(operatorSummary, 1000);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        await _operationStore.FailAsync(
            runtimeOperationId,
            currentStep: RestoreAttemptStages.PrivateTest,
            error: normalizedOperatorSummary,
            evidence: evidence,
            ct: ct);

        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct);

        if (attempt is not null && !RestoreAttemptStatuses.IsTerminal(attempt.Status))
        {
            var now = DateTime.UtcNow;
            attempt.Status = RestoreAttemptStatuses.NeedsAttention;
            attempt.CurrentStage = RestoreAttemptStages.NeedsAttention;
            attempt.RuntimeOperationId = runtimeOperationId;
            attempt.UpdatedAtUtc = now;
            attempt.LastEventAtUtc = now;
            attempt.LastErrorCode = normalizedErrorCode;
            attempt.LastErrorSummary = normalizedOperatorSummary;
            attempt.ErrorCount++;
            await _db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        if (attempt is not null)
        {
            var failedSnapshot = ToSnapshot(attempt);
            await EnsureWorkspaceOrMarkNeedsAttentionAsync(failedSnapshot, ct);
            await _restoreLogs.RecordAsync(
                failedSnapshot.Id,
                runtimeOperationId,
                stage: RestoreAttemptStages.PrivateTest,
                severity: RestoreLogSeverities.Error,
                eventCode: "restore.private-test.failed",
                message: normalizedOperatorSummary,
                details: CreateNonEmptyDetails(
                    ("errorCode", normalizedErrorCode),
                    ("sourceKind", evidence?.SourceKind),
                    ("catalogEntryId", evidence?.CatalogEntryId),
                    ("stagingId", evidence?.StagingId)),
                ct: ct,
                updateAttemptSummary: false);
        }
    }

    /// <summary>
    /// Advances the durable Standard Recreate progress projection. The runtime
    /// operation current step is authoritative for the active phase while the
    /// restore-scoped event timestamp provides a safe last-activity heartbeat
    /// for the Restore Workspace UI.
    /// </summary>
    public async Task RecordStandardRecreateProgressAsync(
        Guid restoreAttemptId,
        Guid runtimeOperationId,
        string currentStep,
        string operatorSummary,
        CancellationToken ct)
    {
        var normalizedStep = Trim(currentStep, 120);
        var normalizedSummary = Trim(operatorSummary, 1000);

        await _operationStore.UpdateStepAsync(
            runtimeOperationId,
            normalizedStep,
            ct);

        await _restoreLogs.RecordAsync(
            restoreAttemptId,
            runtimeOperationId,
            stage: RestoreAttemptStages.CreateRestoredChatServer,
            severity: RestoreLogSeverities.Information,
            eventCode: $"restore.standard-recreate.progress.{normalizedStep}",
            message: normalizedSummary,
            details: new Dictionary<string, string?>
            {
                ["currentStep"] = normalizedStep
            },
            ct: ct,
            updateAttemptSummary: true);
    }

    /// <summary>
    /// Records the durable hand-off from temporary restore claims to the normal
    /// runtime stack/route inventory after Standard Recreate has registered the
    /// restored stack. Claims are retained historically but no longer block
    /// future ownership checks; RuntimeStack and RuntimeRoute now own that role.
    /// </summary>
    public async Task CompleteStandardRecreateAsync(
        Guid restoreAttemptId,
        Guid runtimeOperationId,
        Guid runtimeStackId,
        bool publicReadinessPassed,
        StandardRecreateResult result,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct)
            ?? throw new InvalidOperationException("Restore attempt was not found.");

        await _operationStore.AttachRuntimeStackAsync(runtimeOperationId, runtimeStackId, ct);
        await _operationStore.CompleteAsync(
            runtimeOperationId,
            status: "succeeded",
            currentStep: publicReadinessPassed ? "public-readiness-passed" : "stack-registered-needs-verification",
            result: result,
            evidence: new
            {
                SourceKind = result.SourceKind,
                CatalogEntryId = result.CatalogEntryId,
                RecreateId = result.RecreateId,
                RuntimeStackId = runtimeStackId,
                TargetStackSlug = result.TargetStackSlug,
                MatrixHost = result.MatrixHost,
                ElementHost = result.ElementHost,
                DatabaseImportSucceeded = result.Database.ImportSucceeded,
                MatrixHealthPassed = result.Runtime.MatrixHealthPassed,
                ElementHealthPassed = result.Runtime.ElementHealthPassed,
                PublicReadinessPassed = publicReadinessPassed,
                MatrixUserInventoryStatus = result.UserInventory?.Status,
                MatrixUserInventorySource = result.UserInventory?.Source,
                MatrixUserInventoryUserCount = result.UserInventory?.UserCount,
                MatrixUserInventoryActiveAdminCount = result.UserInventory?.ActiveAdminCount,
                MatrixUserInventorySynchronizedAtUtc = result.UserInventory?.SynchronizedAtUtc,
                MatrixUserInventoryErrorCode = result.UserInventory?.ErrorCode,
                TargetClaimsReleased = true
            },
            ct: ct);

        var now = DateTime.UtcNow;
        var activeClaims = await _db.RestoreTargetClaims
            .Where(x => x.RestoreAttemptId == restoreAttemptId && x.ActiveClaimKey != null)
            .ToListAsync(ct);

        foreach (var claim in activeClaims)
        {
            claim.ActiveClaimKey = null;
            claim.ReleasedAtUtc = now;
            claim.ReleaseReason = "handed-off-to-runtime-stack";
        }

        if (!RestoreAttemptStatuses.IsTerminal(attempt.Status))
        {
            attempt.Status = RestoreAttemptStatuses.Verifying;
            attempt.CurrentStage = RestoreAttemptStages.PublicVerification;
            attempt.RuntimeOperationId = runtimeOperationId;
            attempt.UpdatedAtUtc = now;
            attempt.LastEventAtUtc = now;
            attempt.LastErrorCode = null;
            attempt.LastErrorSummary = null;

            if (!publicReadinessPassed)
            {
                attempt.WarningCount++;
            }
        }

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        var completedSnapshot = ToSnapshot(attempt);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(completedSnapshot, ct);
        await _restoreLogs.RecordAsync(
            completedSnapshot.Id,
            runtimeOperationId,
            stage: RestoreAttemptStages.PublicVerification,
            severity: RestoreLogSeverities.Information,
            eventCode: publicReadinessPassed
                ? "restore.standard-recreate.completed"
                : "restore.standard-recreate.completed-needs-verification",
            message: publicReadinessPassed
                ? "Restored chat server was created and public readiness checks passed."
                : "Restored chat server was registered, but public verification still needs attention.",
            details: CreateNonEmptyDetails(
                ("sourceKind", result.SourceKind),
                ("catalogEntryId", result.CatalogEntryId),
                ("recreateId", result.RecreateId),
                ("runtimeStackId", runtimeStackId.ToString()),
                ("targetStackSlug", result.TargetStackSlug),
                ("matrixHost", result.MatrixHost),
                ("elementHost", result.ElementHost),
                ("databaseImportSucceeded", result.Database.ImportSucceeded ? "true" : "false"),
                ("matrixHealthPassed", result.Runtime.MatrixHealthPassed ? "true" : "false"),
                ("elementHealthPassed", result.Runtime.ElementHealthPassed ? "true" : "false"),
                ("publicReadinessPassed", publicReadinessPassed ? "true" : "false"),
                ("matrixUserInventoryStatus", result.UserInventory?.Status),
                ("matrixUserInventorySource", result.UserInventory?.Source),
                ("matrixUserInventoryUserCount", result.UserInventory?.UserCount?.ToString()),
                ("matrixUserInventoryActiveAdminCount", result.UserInventory?.ActiveAdminCount?.ToString()),
                ("matrixUserInventorySynchronizedAtUtc", result.UserInventory?.SynchronizedAtUtc?.ToString("O")),
                ("matrixUserInventoryErrorCode", result.UserInventory?.ErrorCode)),
            ct: ct,
            updateAttemptSummary: false);
    }

    /// <summary>
    /// Keeps target claims after a failed Standard Recreate so a retry or a
    /// controlled cleanup can safely reason about partially-created resources.
    /// </summary>
    public async Task RecordStandardRecreateFailureAsync(
        Guid restoreAttemptId,
        Guid runtimeOperationId,
        string errorCode,
        string failureCategory,
        string operatorSummary,
        object? evidence,
        CancellationToken ct)
    {
        // The canonical restore attempt, operation summary, NDJSON event stream,
        // and support report are operator-facing surfaces. They must receive only
        // a stable, safe summary. Raw exception text belongs exclusively in the
        // protected operation evidence supplied by the caller.
        var normalizedErrorCode = Trim(errorCode, 200);
        var normalizedFailureCategory = Trim(failureCategory, 120);
        var normalizedOperatorSummary = Trim(operatorSummary, 1000);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        await _operationStore.FailAsync(
            runtimeOperationId,
            currentStep: RestoreAttemptStages.CreateRestoredChatServer,
            error: normalizedOperatorSummary,
            evidence: evidence,
            ct: ct);

        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct);

        if (attempt is not null && !RestoreAttemptStatuses.IsTerminal(attempt.Status))
        {
            var now = DateTime.UtcNow;
            attempt.Status = RestoreAttemptStatuses.NeedsAttention;
            attempt.CurrentStage = RestoreAttemptStages.NeedsAttention;
            attempt.RuntimeOperationId = runtimeOperationId;
            attempt.UpdatedAtUtc = now;
            attempt.LastEventAtUtc = now;
            attempt.LastErrorCode = normalizedErrorCode;
            attempt.LastErrorSummary = normalizedOperatorSummary;
            attempt.ErrorCount++;
            await _db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        if (attempt is not null)
        {
            var failedSnapshot = ToSnapshot(attempt);
            await EnsureWorkspaceOrMarkNeedsAttentionAsync(failedSnapshot, ct);
            await _restoreLogs.RecordAsync(
                failedSnapshot.Id,
                runtimeOperationId,
                stage: RestoreAttemptStages.CreateRestoredChatServer,
                severity: RestoreLogSeverities.Error,
                eventCode: normalizedErrorCode,
                message: normalizedOperatorSummary,
                details: new Dictionary<string, string?>
                {
                    ["failureCategory"] = normalizedFailureCategory,
                    ["operation"] = "restore.standard-recreate"
                },
                ct: ct,
                updateAttemptSummary: false);
        }
    }



    public async Task RecordPreparationFailureAsync(
        Guid restoreAttemptId,
        string errorCode,
        string errorSummary,
        CancellationToken ct)
    {
        var entity = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct);

        if (entity is null || RestoreAttemptStatuses.IsTerminal(entity.Status))
        {
            return;
        }

        var now = DateTime.UtcNow;
        entity.Status = RestoreAttemptStatuses.NeedsAttention;
        entity.CurrentStage = RestoreAttemptStages.NeedsAttention;
        entity.UpdatedAtUtc = now;
        entity.LastEventAtUtc = now;
        entity.LastErrorCode = Trim(errorCode, 200);
        entity.LastErrorSummary = Trim(errorSummary, 4000);
        entity.ErrorCount++;

        await _db.SaveChangesAsync(ct);
        var failedSnapshot = ToSnapshot(entity);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(failedSnapshot, ct);
        await _restoreLogs.RecordAsync(
            failedSnapshot.Id,
            operationId: null,
            stage: "source",
            severity: RestoreLogSeverities.Error,
            eventCode: errorCode,
            message: errorSummary,
            details: null,
            ct: ct,
            updateAttemptSummary: false);
    }

    /// <summary>
    /// Marks an attempt terminal without deleting its workspace or historical
    /// evidence. Cancellation is deliberately blocked while a linked runtime
    /// operation is still queued or running.
    /// </summary>
    public async Task<RestoreAttemptSnapshot> CancelAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        var entity = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.RestoreSessionId == normalizedRestoreSessionId, ct)
            ?? throw new InvalidOperationException("Restore attempt was not found.");

        if (string.Equals(
                entity.Status,
                RestoreAttemptStatuses.Cancelled,
                StringComparison.OrdinalIgnoreCase))
        {
            // Cancellation is idempotent for the one terminal state that this
            // action itself creates. Returning the snapshot lets an operator
            // safely retry after a browser or network interruption.
            return ToSnapshot(entity);
        }

        if (RestoreAttemptStatuses.IsTerminal(entity.Status))
        {
            throw new InvalidOperationException(
                $"Restore attempt is terminal with status '{entity.Status}' and cannot be cancelled.");
        }

        var runningOperation = await _db.RuntimeOperations
            .AsNoTracking()
            .AnyAsync(
                x => x.RestoreAttemptId == entity.Id &&
                     (x.Status == "queued" || x.Status == "running"),
                ct);

        if (runningOperation)
        {
            throw new InvalidOperationException(
                "This restore has a queued or running operation and cannot be cancelled safely yet.");
        }

        var now = DateTime.UtcNow;
        var activeClaims = await _db.RestoreTargetClaims
            .Where(x => x.RestoreAttemptId == entity.Id && x.ActiveClaimKey != null)
            .ToListAsync(ct);

        foreach (var claim in activeClaims)
        {
            claim.ActiveClaimKey = null;
            claim.ReleasedAtUtc = now;
            claim.ReleaseReason = "restore-cancelled";
        }

        entity.Status = RestoreAttemptStatuses.Cancelled;
        entity.CurrentStage = RestoreAttemptStages.Cancelled;
        entity.ActiveSourceKey = null;
        entity.TerminalAtUtc = now;
        entity.UpdatedAtUtc = now;
        entity.LastEventAtUtc = now;
        entity.RuntimeOperationId = null;

        await _db.SaveChangesAsync(ct);

        var snapshot = ToSnapshot(entity);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(snapshot, ct);
        await _restoreLogs.RecordAsync(
            snapshot.Id,
            operationId: null,
            stage: RestoreAttemptStages.Cancelled,
            severity: RestoreLogSeverities.Information,
            eventCode: "restore.cancelled",
            message: "Restore was cancelled by the operator. Active target claims were released.",
            details: null,
            ct: ct,
            updateAttemptSummary: false);
        return snapshot;
    }

    /// <summary>
    /// Marks a restore attempt as completed after a fresh successful public
    /// readiness report has been recorded for the stack attached to its
    /// Standard Recreate operation. This is a terminal audit transition only;
    /// it does not mutate the restored runtime, routes, database, or claims.
    /// </summary>
    public async Task<RestoreAttemptSnapshot> CompleteHandoverAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        var attempt = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.RestoreSessionId == normalizedRestoreSessionId, ct)
            ?? throw new InvalidOperationException("Restore attempt was not found.");

        if (string.Equals(attempt.Status, RestoreAttemptStatuses.Completed, StringComparison.OrdinalIgnoreCase))
        {
            return ToSnapshot(attempt);
        }

        if (RestoreAttemptStatuses.IsTerminal(attempt.Status))
        {
            throw new InvalidOperationException(
                $"Restore attempt is terminal with status '{attempt.Status}' and cannot be handed over.");
        }

        if (attempt.RuntimeOperationId is null)
        {
            throw new InvalidOperationException(
                "The restored server has not been registered for final handover.");
        }

        var operation = await _db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attempt.RuntimeOperationId.Value, ct);

        if (operation?.RuntimeStackId is null)
        {
            throw new InvalidOperationException(
                "The restored runtime stack is not available for final handover.");
        }

        var latestDoctor = await _db.RuntimeReadinessReports
            .AsNoTracking()
            .Where(x => x.RuntimeStackId == operation.RuntimeStackId.Value && x.ReportKind == "doctor")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (latestDoctor is null || !latestDoctor.AllPassed)
        {
            throw new InvalidOperationException(
                "Run and pass the public restored-server checks before completing handover.");
        }

        var now = DateTime.UtcNow;
        attempt.Status = RestoreAttemptStatuses.Completed;
        attempt.CurrentStage = RestoreAttemptStages.PublicVerification;
        attempt.ActiveSourceKey = null;
        attempt.TerminalAtUtc = now;
        attempt.UpdatedAtUtc = now;
        attempt.LastEventAtUtc = now;
        attempt.LastErrorCode = null;
        attempt.LastErrorSummary = null;

        await _db.SaveChangesAsync(ct);

        var snapshot = ToSnapshot(attempt);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(snapshot, ct);
        await _restoreLogs.RecordAsync(
            snapshot.Id,
            attempt.RuntimeOperationId,
            stage: RestoreAttemptStages.PublicVerification,
            severity: RestoreLogSeverities.Information,
            eventCode: "restore.handover.completed",
            message: "Restore handover was completed after public server checks passed.",
            details: new Dictionary<string, string?>
            {
                ["runtimeStackId"] = operation.RuntimeStackId.Value.ToString(),
                ["verificationReportKind"] = "doctor"
            },
            ct: ct,
            updateAttemptSummary: false);

        return snapshot;
    }

    /// <summary>
    /// Read-only first-pass reconciliation. It does not create authority for
    /// orphaned folders and it does not release target claims automatically.
    /// </summary>
    public async Task<RestoreAttemptReconciliationReport> ReconcileCreatingAttemptsAsync(
        CancellationToken ct)
    {
        var attempts = await _db.RestoreAttempts
            .AsNoTracking()
            .Where(x => x.ActiveSourceKey != null && x.Status == RestoreAttemptStatuses.Creating)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var items = new List<RestoreAttemptReconciliationItem>();
        var warnings = new List<string>();

        foreach (var entity in attempts)
        {
            var snapshot = ToSnapshot(entity);
            var workspaceExists = _workspaceStore.WorkspaceExists(snapshot.RestoreSessionId);
            var requiresAttention = !workspaceExists;

            items.Add(new RestoreAttemptReconciliationItem(
                snapshot.RestoreSessionId,
                snapshot.Status,
                snapshot.CurrentStage,
                workspaceExists,
                requiresAttention,
                workspaceExists
                    ? "Creating restore attempt has a canonical workspace and can be reviewed or resumed."
                    : "Creating restore attempt has no canonical workspace and needs controlled repair."));

            if (requiresAttention)
            {
                warnings.Add(
                    $"Restore attempt '{snapshot.RestoreSessionId}' is creating but its workspace is missing.");
            }
        }

        return new RestoreAttemptReconciliationReport(
            DateTimeOffset.UtcNow,
            items,
            warnings);
    }

    private static IReadOnlyList<RestoreTargetClaimResource> BuildStandardRecreateTargetResources(
        string targetStackSlug,
        string matrixHost,
        string elementHost)
    {
        var normalizedStackSlug = NormalizeStackSlug(targetStackSlug);
        var normalizedMatrixHost = NormalizeTargetHost(matrixHost, "Matrix host is required.");
        var normalizedElementHost = NormalizeTargetHost(elementHost, "Element host is required.");

        var resources = new[]
        {
            new RestoreTargetClaimResource(
                RestoreTargetResourceTypes.StackSlug,
                normalizedStackSlug,
                $"stack-slug:{normalizedStackSlug}"),
            new RestoreTargetClaimResource(
                RestoreTargetResourceTypes.MatrixHost,
                normalizedMatrixHost,
                $"public-host:{normalizedMatrixHost}"),
            new RestoreTargetClaimResource(
                RestoreTargetResourceTypes.ElementHost,
                normalizedElementHost,
                $"public-host:{normalizedElementHost}")
        };

        if (resources.Select(x => x.ActiveClaimKey).Distinct(StringComparer.Ordinal).Count() != resources.Length)
        {
            throw new InvalidOperationException(
                "Matrix and Element public host names must be different for a restored chat server.");
        }

        return resources;
    }

    private async Task ThrowIfLiveRuntimeOwnsAnyTargetAsync(
        RestoreAttemptEntity attempt,
        IReadOnlyList<RestoreTargetClaimResource> resources,
        CancellationToken ct)
    {
        var stackResource = resources.Single(x => x.ResourceType == RestoreTargetResourceTypes.StackSlug);
        var hostResources = resources
            .Where(x => x.ResourceType is RestoreTargetResourceTypes.MatrixHost or RestoreTargetResourceTypes.ElementHost)
            .ToArray();

        var stackRows = await _db.RuntimeStacks
            .AsNoTracking()
            .Select(x => new { x.Id, x.Slug })
            .ToListAsync(ct);

        var existingStack = stackRows.FirstOrDefault(x =>
            string.Equals(x.Slug, stackResource.ResourceValue, StringComparison.OrdinalIgnoreCase));

        if (existingStack is not null)
        {
            throw new RestoreAttemptConflictException(new RestoreAttemptConflictResponse(
                Code: "restore_target_occupied",
                RestoreSessionId: attempt.RestoreSessionId,
                ResourceType: stackResource.ResourceType,
                ResourceValue: stackResource.ResourceValue,
                Detail: $"Target stack '{stackResource.ResourceValue}' is already owned by an existing MEM runtime stack."));
        }

        var publicRoutes = await _db.RuntimeRoutes
            .AsNoTracking()
            .Where(x => x.IsPublic)
            .Select(x => new { x.RuntimeStackId, x.PublicHost })
            .ToListAsync(ct);

        foreach (var hostResource in hostResources)
        {
            var existingRoute = publicRoutes.FirstOrDefault(x =>
                string.Equals(x.PublicHost, hostResource.ResourceValue, StringComparison.OrdinalIgnoreCase));

            if (existingRoute is not null)
            {
                throw new RestoreAttemptConflictException(new RestoreAttemptConflictResponse(
                    Code: "restore_target_occupied",
                    RestoreSessionId: attempt.RestoreSessionId,
                    ResourceType: hostResource.ResourceType,
                    ResourceValue: hostResource.ResourceValue,
                    Detail: $"Public host '{hostResource.ResourceValue}' is already owned by an existing MEM runtime route."));
            }
        }
    }

    private static RestoreAttemptConflictException CreateTargetClaimConflict(
        RestoreAttemptSnapshot attempt,
        RestoreTargetClaimEntity claim) =>
        CreateTargetClaimConflict(attempt.RestoreSessionId, claim);

    private static RestoreAttemptConflictException CreateTargetClaimConflict(
        RestoreAttemptEntity attempt,
        RestoreTargetClaimEntity claim) =>
        CreateTargetClaimConflict(attempt.RestoreSessionId, claim);

    private static RestoreAttemptConflictException CreateTargetClaimConflict(
        string attemptedRestoreSessionId,
        RestoreTargetClaimEntity claim)
    {
        var ownerRestoreSessionId = claim.RestoreAttempt?.RestoreSessionId;
        var ownerLabel = string.IsNullOrWhiteSpace(ownerRestoreSessionId)
            ? "another active restore"
            : $"active restore '{ownerRestoreSessionId}'";

        return new RestoreAttemptConflictException(new RestoreAttemptConflictResponse(
            Code: "restore_target_claimed",
            RestoreSessionId: string.IsNullOrWhiteSpace(ownerRestoreSessionId)
                ? attemptedRestoreSessionId
                : ownerRestoreSessionId,
            ResourceType: claim.ResourceType,
            ResourceValue: claim.ResourceValue,
            Detail: $"{ToFriendlyResourceName(claim.ResourceType)} '{claim.ResourceValue}' is already reserved by {ownerLabel}. Continue that restore or choose another target."));
    }

    private static RestoreAttemptConflictException CreateTargetSelectionConflict(
        RestoreAttemptEntity attempt,
        RestoreTargetClaimEntity existingClaim) =>
        new(new RestoreAttemptConflictResponse(
            Code: "restore_target_already_selected",
            RestoreSessionId: attempt.RestoreSessionId,
            ResourceType: existingClaim.ResourceType,
            ResourceValue: existingClaim.ResourceValue,
            Detail: $"Restore '{attempt.RestoreSessionId}' already reserved {ToFriendlyResourceName(existingClaim.ResourceType).ToLowerInvariant()} '{existingClaim.ResourceValue}'. Continue with the same target, or cancel/resolve the restore before choosing another target."));

    private static RestoreAttemptConflictException CreateOperationInProgressConflict(
        RestoreAttemptEntity attempt) =>
        new(new RestoreAttemptConflictResponse(
            Code: "restore_operation_in_progress",
            RestoreSessionId: attempt.RestoreSessionId,
            ResourceType: "restore-operation",
            ResourceValue: "restore.standard-recreate",
            Detail: $"Restore '{attempt.RestoreSessionId}' already has a Standard Recreate operation in progress. Continue that restore instead of starting another one."));

    private static RestoreTargetClaimSnapshot ToClaimSnapshot(RestoreTargetClaimEntity claim) =>
        new(
            claim.Id,
            claim.ResourceType,
            claim.ResourceValue,
            claim.ActiveClaimKey,
            ToDateTimeOffset(claim.ClaimedAtUtc),
            ToNullableDateTimeOffset(claim.ReleasedAtUtc),
            claim.ReleaseReason);

    private static string NormalizeStackSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Target stack slug is required.");
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!normalized.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
        {
            throw new InvalidOperationException("Target stack slug may contain only lowercase letters, digits, and hyphens.");
        }

        return normalized;
    }

    private static string NormalizeTargetHost(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(message);
        }

        var normalized = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(normalized) == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException($"'{value}' is not a valid public hostname.");
        }

        return normalized;
    }

    private static InvalidOperationException CreateCatalogOnlyException() =>
        new("Restore attempts can only be created from a managed Backup Catalog entry. " +
            "Materialise uploaded ZIPs into the Backup Catalog before restoring.");

    private static string NormalizeRequestedBy(string? requestedBy)
    {
        var normalized = Trim(requestedBy, 100);
        return string.IsNullOrWhiteSpace(normalized) ? "host-agent" : normalized;
    }

    private static void AddAssessmentCheck(
        ICollection<RestoreStandardRecreateTargetAssessmentCheck> checks,
        ICollection<string> blockers,
        string code,
        bool passed,
        string message)
    {
        checks.Add(new RestoreStandardRecreateTargetAssessmentCheck(code, passed, message));
        if (!passed)
        {
            blockers.Add(message);
        }
    }

    private static string ToFriendlyResourceName(string resourceType) => resourceType switch
    {
        RestoreTargetResourceTypes.StackSlug => "Target stack",
        RestoreTargetResourceTypes.MatrixHost => "Matrix public host",
        RestoreTargetResourceTypes.ElementHost => "Element public host",
        _ => "Restore target"
    };

    private async Task<RestoreAttemptGetOrCreateResult> GetOrCreateAsync(
        RestoreAttemptSourceIdentity source,
        string initialStatus,
        string initialStage,
        CancellationToken ct)
    {
        var existing = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ActiveSourceKey == source.SourceKey, ct);

        if (existing is not null)
        {
            var existingSnapshot = ToSnapshot(existing);
            await EnsureWorkspaceOrMarkNeedsAttentionAsync(existingSnapshot, ct);
            return new RestoreAttemptGetOrCreateResult(existingSnapshot, Created: false, Resumed: true);
        }

        var now = DateTime.UtcNow;
        var restoreSessionId = CreateRestoreSessionId();
        var sessionDirectoryPath = _workspaceStore.GetSessionDirectoryPath(restoreSessionId);
        var entity = new RestoreAttemptEntity
        {
            Id = Guid.NewGuid(),
            RestoreSessionId = restoreSessionId,
            SourceKind = source.SourceKind,
            SourceKey = source.SourceKey,
            ActiveSourceKey = source.SourceKey,
            SourceCatalogEntryIdSnapshot = source.CatalogEntryId,
            SourceDisplayNameSnapshot = source.DisplayName,
            SourceOriginKindSnapshot = source.OriginKind,
            SourceStackSlugSnapshot = source.SourceStackSlug,
            SourceBackupIdSnapshot = source.SourceBackupId,
            BackupCatalogEntryId = source.BackupCatalogEntryId,
            Status = initialStatus,
            CurrentStage = initialStage,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            TerminalAtUtc = null,
            LastEventAtUtc = now,
            LastErrorCode = null,
            LastErrorSummary = null,
            WarningCount = 0,
            ErrorCount = 0,
            SessionDirectoryPath = sessionDirectoryPath,
            LogDirectoryPath = Path.Combine(sessionDirectoryPath, "logs"),
            SupportReportPath = null,
            RuntimeOperationId = null
        };

        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            _db.RestoreAttempts.Add(entity);
            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraint(ex))
        {
            _db.ChangeTracker.Clear();

            var winner = await _db.RestoreAttempts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.ActiveSourceKey == source.SourceKey, ct);

            if (winner is null)
            {
                throw;
            }

            var winnerSnapshot = ToSnapshot(winner);
            await EnsureWorkspaceOrMarkNeedsAttentionAsync(winnerSnapshot, ct);
            return new RestoreAttemptGetOrCreateResult(winnerSnapshot, Created: false, Resumed: true);
        }

        var created = ToSnapshot(entity);
        await EnsureWorkspaceOrMarkNeedsAttentionAsync(created, ct);
        await _restoreLogs.RecordAsync(
            created.Id,
            operationId: null,
            stage: "source",
            severity: RestoreLogSeverities.Information,
            eventCode: "restore.attempt.created",
            message: "Restore workspace was created.",
            details: CreateNonEmptyDetails(
                ("sourceKind", created.SourceKind),
                ("catalogEntryId", created.CatalogEntryId),
                ("sourceDisplayName", created.SourceDisplayName),
                ("sourceOriginKind", created.SourceOriginKind),
                ("sourceStackSlug", created.SourceStackSlug),
                ("sourceBackupId", created.SourceBackupId),
                ("backupCatalogEntryId", created.BackupCatalogEntryId?.ToString())),
            ct: ct);
        return new RestoreAttemptGetOrCreateResult(created, Created: true, Resumed: false);
    }

    private static IReadOnlyDictionary<string, string?> CreateNonEmptyDetails(
        params (string Key, string? Value)[] values)
    {
        var details = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                details[key] = value;
            }
        }

        return details;
    }

    private async Task EnsureWorkspaceOrMarkNeedsAttentionAsync(
        RestoreAttemptSnapshot attempt,
        CancellationToken ct)
    {
        try
        {
            await _workspaceStore.EnsureWorkspaceAsync(attempt, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(
                ex,
                "Restore attempt workspace could not be written. RestoreSessionId={RestoreSessionId}",
                attempt.RestoreSessionId);

            await MarkWorkspaceFailureAsync(attempt.Id, ex.Message, ct);
            throw;
        }
    }

    private async Task MarkWorkspaceFailureAsync(
        Guid restoreAttemptId,
        string error,
        CancellationToken ct)
    {
        var entity = await _db.RestoreAttempts
            .FirstOrDefaultAsync(x => x.Id == restoreAttemptId, ct);
        if (entity is null || RestoreAttemptStatuses.IsTerminal(entity.Status))
        {
            return;
        }

        var now = DateTime.UtcNow;
        entity.Status = RestoreAttemptStatuses.NeedsAttention;
        entity.CurrentStage = RestoreAttemptStages.NeedsAttention;
        entity.UpdatedAtUtc = now;
        entity.LastEventAtUtc = now;
        entity.LastErrorCode = "restore.workspace.write_failed";
        entity.LastErrorSummary = Trim(error, 4000);
        entity.ErrorCount++;
        await _db.SaveChangesAsync(ct);
    }

    private static RestoreAttemptSnapshot ToSnapshot(RestoreAttemptEntity entity)
    {
        return new RestoreAttemptSnapshot(
            entity.Id,
            entity.RestoreSessionId,
            entity.SourceKind,
            entity.SourceKey,
            entity.SourceCatalogEntryIdSnapshot,
            entity.SourceDisplayNameSnapshot,
            entity.SourceOriginKindSnapshot,
            entity.SourceStackSlugSnapshot,
            entity.SourceBackupIdSnapshot,
            entity.Status,
            entity.CurrentStage,
            ToDateTimeOffset(entity.CreatedAtUtc),
            ToDateTimeOffset(entity.UpdatedAtUtc),
            ToNullableDateTimeOffset(entity.TerminalAtUtc),
            ToNullableDateTimeOffset(entity.LastEventAtUtc),
            entity.LastErrorCode,
            entity.LastErrorSummary,
            entity.WarningCount,
            entity.ErrorCount,
            entity.SessionDirectoryPath,
            entity.LogDirectoryPath,
            entity.SupportReportPath,
            entity.RuntimeOperationId,
            entity.BackupCatalogEntryId);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToNullableDateTimeOffset(DateTime? value) =>
        value.HasValue ? ToDateTimeOffset(value.Value) : null;

    private static bool IsUniqueConstraint(DbUpdateException exception) =>
        exception.InnerException is SqliteException sqlite && sqlite.SqliteErrorCode == 19;

    private static string CreateRestoreSessionId() =>
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'") + "-" + Guid.NewGuid().ToString("N")[..8];

    private static string Trim(string value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
