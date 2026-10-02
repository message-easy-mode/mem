using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Operator.Migrations.Workspace;

namespace Modules.Operator.Migrations;

/// <summary>
/// Compact server-side Migration Session inventory. It deliberately retrieves
/// only list-bearing scalar state and never materialises manifests, protected
/// age identities, filesystem paths, evidence JSON, credentials, or complete
/// entity graphs.
/// </summary>
public sealed class MigrationSessionInventoryService(
    MemDbContext db,
    TimeProvider timeProvider,
    IConfiguration configuration)
{
    public async Task<MigrationSessionInventoryResponse> ListAsync(
        MigrationSessionInventoryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var roots = await db.MigrationIntakes
            .AsNoTracking()
            .Select(x => new RootRow(
                x.Id,
                x.IntakeId,
                x.DisplayName,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.CancelledAtUtc,
                x.LifecycleStatus,
                x.ClosedAtUtc,
                x.ClosureKind,
                x.ArchivedAtUtc,
                x.StateVersion))
            .ToListAsync(cancellationToken);

        if (roots.Count == 0)
        {
            return Empty(query);
        }

        var intakeIds = roots.Select(x => x.Id).ToArray();

        var sources = await db.MigrationSources
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new SourceRow(
                x.MigrationIntakeEntityId,
                x.SourceId,
                x.Product,
                x.ProductVersion,
                x.SourceFingerprint,
                x.CapturedAtUtc))
            .ToListAsync(cancellationToken);

        var revisions = await db.MigrationPackageRevisions
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new RevisionRow(
                x.Id,
                x.PackageRevisionId,
                x.MigrationIntakeEntityId,
                x.RevisionNumber,
                x.Purpose,
                x.Status,
                x.RetentionState,
                x.ActivePurposeKey,
                x.PackageSizeBytes,
                x.EncryptedPackageSha256,
                x.DecryptedArchiveSha256,
                x.CaptureKind,
                x.SourceFrozen,
                x.RehearsalOnly,
                x.ArchiveMigrationId,
                x.ArchiveSourceProduct,
                x.ArchiveSourceVersion,
                x.ArchiveStackCount,
                x.CreatedAtUtc,
                x.ExpiresAtUtc,
                x.UploadedAtUtc,
                x.ValidatedAtUtc,
                x.SupersededAtUtc,
                x.RetiredAtUtc))
            .ToListAsync(cancellationToken);

        var conversionRows = await db.MigrationConversionAttempts
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new ConversionRow(
                x.Id,
                x.MigrationIntakeEntityId,
                x.MigrationPackageRevisionEntityId,
                x.ActiveMigrationKey,
                x.Status,
                x.FailureCode,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                null))
            .ToListAsync(cancellationToken);

        var conversionIds = conversionRows.Select(x => x.Id).ToArray();
        var candidates = conversionIds.Length == 0
            ? new List<CandidateAttemptRow>()
            : await db.MigrationCandidateArtifacts
                .AsNoTracking()
                .Where(x => conversionIds.Contains(
                    x.MigrationConversionAttemptEntityId))
                .Select(x => new CandidateAttemptRow(
                    x.MigrationConversionAttemptEntityId,
                    new CandidateRow(
                        x.Id,
                        x.VerificationStatus,
                        x.RetentionState,
                        x.SourcePackageSha256,
                        x.CreatedAtUtc,
                        x.VerifiedAtUtc,
                        x.RetiredAtUtc)))
                .ToListAsync(cancellationToken);
        var candidateByAttempt = candidates.ToDictionary(x => x.AttemptId);
        var conversions = conversionRows
            .Select(x => x with
            {
                Candidate = candidateByAttempt.GetValueOrDefault(x.Id)?.Candidate,
            })
            .ToList();

        var stagings = await db.MigrationStagingRuns
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new StagingRow(
                x.Id,
                x.MigrationIntakeEntityId,
                x.MigrationCandidateArtifactEntityId,
                x.Status,
                x.ActiveMigrationKey,
                x.PublicRoutesCreated,
                x.FailureCode,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.DestroyedAtUtc))
            .ToListAsync(cancellationToken);

        var authorities = await db.MigrationProductionAuthorities
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new AuthorityRow(
                x.MigrationIntakeEntityId,
                x.MigrationPackageRevisionEntityId,
                x.MigrationCandidateArtifactEntityId,
                x.MigrationStagingRunEntityId,
                x.Status,
                x.ActiveMigrationKey,
                x.CreatedAtUtc,
                x.SupersededAtUtc,
                x.RevokedAtUtc))
            .ToListAsync(cancellationToken);

        var adoptions = await db.MigrationProductionAdoptions
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new AdoptionRow(
                x.MigrationIntakeEntityId,
                x.MigrationPackageRevisionEntityId,
                x.MigrationCandidateArtifactEntityId,
                x.MigrationStagingRunEntityId,
                x.Status,
                x.TargetStackSlug,
                x.TargetDisplayName,
                x.MatrixServerName,
                x.MatrixPublicHost,
                x.ElementPublicHost,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.PreparedAtUtc,
                x.MaterializationStartedAtUtc,
                x.MaterializationCompletedAtUtc,
                x.MaterializationFailureCode,
                x.CutoverPreviewStatus,
                x.CutoverPreviewCreatedAtUtc,
                x.CutoverStatus,
                x.CutoverStartedAtUtc,
                x.CutoverCompletedAtUtc,
                x.TargetPublicAtUtc,
                x.PublicRoutesCreated,
                x.RouteCompensationAttempted,
                x.RouteCompensationCompleted,
                x.CutoverFailureCode,
                x.ProductionVerificationStatus,
                x.ProductionVerificationStartedAtUtc,
                x.ProductionVerificationCompletedAtUtc,
                x.ProductionVerificationFailureCode,
                x.RollbackStatus,
                x.RollbackStartedAtUtc,
                x.RollbackCompletedAtUtc,
                x.RollbackCompletionStatus,
                x.RollbackFailureCode))
            .ToListAsync(cancellationToken);

        var acceptances = await db.MigrationAcceptances
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new AcceptanceRow(
                x.MigrationIntakeEntityId,
                x.AcceptedAtUtc,
                x.PublicCutoverAtUtc))
            .ToListAsync(cancellationToken);

        var baselines = await db.MigrationBaselineBackupHandoffs
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => new BaselineRow(
                x.MigrationIntakeEntityId,
                x.Status,
                x.TargetStackSlug,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.BackupCreatedAtUtc))
            .ToListAsync(cancellationToken);

        var legacyRetentionIntakeIds = (await db.LegacyRetentionRecords
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => x.MigrationIntakeEntityId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var qualificationIntakeIds = (await db.MigrationTwoServerQualifications
            .AsNoTracking()
            .Where(x => intakeIds.Contains(x.MigrationIntakeEntityId))
            .Select(x => x.MigrationIntakeEntityId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var sourceGroups = sources
            .GroupBy(x => x.IntakeId)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SourceId).ToArray());
        var revisionGroups = revisions
            .GroupBy(x => x.IntakeId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var conversionGroups = conversions
            .GroupBy(x => x.IntakeId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var stagingGroups = stagings
            .GroupBy(x => x.IntakeId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var authorityGroups = authorities
            .GroupBy(x => x.IntakeId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var adoptionByIntake = adoptions.ToDictionary(x => x.IntakeId);
        var acceptanceByIntake = acceptances.ToDictionary(x => x.IntakeId);
        var baselineByIntake = baselines.ToDictionary(x => x.IntakeId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var projected = roots
            .Select(root => Project(
                root,
                sourceGroups.GetValueOrDefault(root.Id) ?? [],
                revisionGroups.GetValueOrDefault(root.Id) ?? [],
                conversionGroups.GetValueOrDefault(root.Id) ?? [],
                stagingGroups.GetValueOrDefault(root.Id) ?? [],
                authorityGroups.GetValueOrDefault(root.Id) ?? [],
                adoptionByIntake.GetValueOrDefault(root.Id),
                acceptanceByIntake.GetValueOrDefault(root.Id),
                baselineByIntake.GetValueOrDefault(root.Id),
                legacyRetentionIntakeIds.Contains(root.Id),
                qualificationIntakeIds.Contains(root.Id),
                now))
            .ToArray();

        var searched = projected
            .Where(x => MatchesSearch(x, query.Search))
            .Where(x => MatchesTarget(x, query.TargetStack))
            .Where(x => query.IncludeArchived || !x.Row.Archived)
            .Where(x => query.Lifecycle != "archived" || x.Row.Archived)
            .Where(x => query.Stage is null ||
                string.Equals(
                    x.Row.CurrentStageCode,
                    query.Stage,
                    StringComparison.Ordinal))
            .ToArray();

        var summary = new MigrationSessionInventorySummaryDto(
            TotalSessions: searched.Length,
            ActiveCount: searched.Count(x =>
                x.Row.LifecycleStatus == MigrationSessionLifecycleStatuses.Active &&
                !x.Row.Archived),
            NeedsActionCount: searched.Count(x =>
                x.Row.PrimaryAction.Kind == "review"),
            CompletedCount: searched.Count(x =>
                x.Row.LifecycleStatus == MigrationSessionLifecycleStatuses.Completed),
            ClosedCount: searched.Count(x =>
                x.Row.LifecycleStatus == MigrationSessionLifecycleStatuses.Closed),
            CancelledCount: searched.Count(x =>
                x.Row.LifecycleStatus == MigrationSessionLifecycleStatuses.Cancelled),
            ArchivedCount: searched.Count(x => x.Row.Archived));

        var lifecycleFiltered = searched
            .Where(x => query.Lifecycle switch
            {
                "all" => true,
                "archived" => x.Row.Archived,
                _ => string.Equals(
                    x.Row.LifecycleStatus,
                    query.Lifecycle,
                    StringComparison.Ordinal) &&
                    !x.Row.Archived,
            })
            .ToArray();

        var actionFiltered = lifecycleFiltered
            .Where(x => query.Action == "all" ||
                string.Equals(
                    x.Row.PrimaryAction.Kind,
                    query.Action,
                    StringComparison.Ordinal))
            .ToArray();

        var ordered = Order(actionFiltered, query);
        var totalSessions = ordered.Length;
        var totalPages = totalSessions == 0
            ? 0
            : (int)Math.Ceiling(totalSessions / (double)query.PageSize);
        var page = totalPages == 0
            ? 1
            : Math.Min(query.Page, totalPages);
        var sessions = ordered
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(AuthorizeDeletion)
            .ToArray();

        var targetStacks = projected
            .Select(x => x.Row.TargetStackSlug)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var warnings = query.Page > Math.Max(totalPages, 1)
            ? new[]
            {
                new MigrationSessionInventoryWarningDto(
                    "migration_session_page_clamped",
                    "The requested page was beyond the result set and was clamped to the final page."),
            }
            : Array.Empty<MigrationSessionInventoryWarningDto>();

        return new MigrationSessionInventoryResponse(
            SchemaVersion: 1,
            Query: ToDto(query),
            Summary: summary,
            TotalSessions: totalSessions,
            Page: page,
            PageSize: query.PageSize,
            TotalPages: totalPages,
            HasPreviousPage: page > 1,
            HasNextPage: totalPages > 0 && page < totalPages,
            TargetStacks: targetStacks,
            Sessions: sessions,
            Warnings: warnings);
    }

    private ProjectedRow Project(
        RootRow root,
        IReadOnlyList<SourceRow> sources,
        IReadOnlyList<RevisionRow> revisions,
        IReadOnlyList<ConversionRow> conversions,
        IReadOnlyList<StagingRow> stagings,
        IReadOnlyList<AuthorityRow> authorities,
        AdoptionRow? adoption,
        AcceptanceRow? acceptance,
        BaselineRow? baseline,
        bool hasLegacyRetention,
        bool hasTwoServerQualification,
        DateTime now)
    {
        var authoritativeRevision = ResolveAuthoritativeRevision(revisions);
        var authoritativeConversions = authoritativeRevision is null
            ? Array.Empty<ConversionRow>()
            : conversions
                .Where(x => x.PackageRevisionId == authoritativeRevision.Id)
                .ToArray();
        var latestConversion = authoritativeConversions
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var verifiedCandidate = authoritativeConversions
            .Select(x => x.Candidate)
            .Where(x =>
                x is not null &&
                string.Equals(
                    x.VerificationStatus,
                    "verified",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.RetentionState,
                    "active",
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.SourcePackageSha256,
                    authoritativeRevision?.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x!.CreatedAtUtc)
            .FirstOrDefault();

        var authoritativeCandidateIds = authoritativeConversions
            .Where(x => x.Candidate is not null)
            .Select(x => x.Candidate!.Id)
            .ToHashSet();
        var latestStaging = stagings
            .Where(x => authoritativeCandidateIds.Contains(x.CandidateId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var activeAuthority = authorities
            .Where(x =>
                string.Equals(
                    x.Status,
                    MigrationProductionAuthorityStatuses.Active,
                    StringComparison.OrdinalIgnoreCase) &&
                x.ActiveMigrationKey is not null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        var activeAuthorityUsable =
            activeAuthority is not null &&
            latestStaging is not null &&
            authoritativeRevision is not null &&
            verifiedCandidate is not null &&
            string.Equals(
                latestStaging.Status,
                "verified",
                StringComparison.OrdinalIgnoreCase) &&
            latestStaging.ActiveMigrationKey is not null &&
            activeAuthority.PackageRevisionId == authoritativeRevision.Id &&
            activeAuthority.CandidateId == verifiedCandidate.Id &&
            activeAuthority.StagingRunId == latestStaging.Id;

        var accepted = acceptance is not null;
        var effectiveAdoptionStatus = ResolveEffectiveAdoptionStatus(
            adoption,
            authoritativeRevision,
            verifiedCandidate,
            latestStaging,
            activeAuthority,
            accepted);

        const int blockerCount = 0;
        const int warningCount = 0;
        var effectiveIntakeStatus = ResolveEffectiveIntakeStatus(
            root,
            revisions,
            now);

        var statePlan = MigrationWorkspaceStateMapper.Map(
            new MigrationWorkspaceStateInput(
                effectiveIntakeStatus,
                blockerCount,
                LegacyCompatibility: false,
                latestConversion?.Status,
                latestConversion?.FailureCode,
                verifiedCandidate is not null,
                latestStaging?.Status,
                latestStaging?.ActiveMigrationKey is not null,
                latestStaging?.FailureCode,
                activeAuthorityUsable,
                adoption is not null,
                effectiveAdoptionStatus,
                adoption?.MaterializationFailureCode,
                adoption?.CutoverPreviewStatus,
                adoption?.CutoverStatus,
                adoption?.PublicRoutesCreated ?? false,
                adoption?.RouteCompensationAttempted ?? false,
                adoption?.RouteCompensationCompleted ?? false,
                adoption?.CutoverFailureCode,
                adoption?.ProductionVerificationStatus,
                adoption?.ProductionVerificationFailureCode,
                adoption?.RollbackStatus,
                adoption?.RollbackCompletionStatus,
                adoption?.RollbackFailureCode,
                accepted,
                baseline?.Status));

        var currentStage = statePlan.Stages[statePlan.CurrentStageIndex];
        var sourceIdentity = ResolveSource(root, sources, revisions);
        var lifecycle = ResolveLifecycle(root, revisions, baseline, now);
        var archived = root.ArchivedAtUtc.HasValue;
        var needsAttention =
            blockerCount > 0 ||
            warningCount > 0 ||
            statePlan.OverallStatus.Severity is "warning" or "error" ||
            currentStage.State is
                MigrationWorkspaceStageStates.Blocked or
                MigrationWorkspaceStageStates.Failed or
                MigrationWorkspaceStageStates.ActionRequired;
        var actionKind =
            archived ||
            lifecycle != MigrationSessionLifecycleStatuses.Active
                ? "view"
                : needsAttention
                    ? "review"
                    : "continue";
        var actionCode =
            statePlan.OverallStatus.NextAction?.Code ?? "open-migration";
        var updatedAtUtc = ResolveUpdatedAtUtc(
            root,
            sources,
            revisions,
            conversions,
            stagings,
            authorities,
            adoption,
            acceptance,
            baseline,
            now);
        var hasActiveConversion = conversions.Any(x =>
            x.ActiveMigrationKey is not null ||
            !IsTerminalConversionStatus(x.Status));
        var retainedStaging = stagings.Any(x =>
            x.ActiveMigrationKey is not null && !x.DestroyedAtUtc.HasValue);
        var hasProductionOwnership =
            stagings.Any(x => x.PublicRoutesCreated) ||
            authorities.Count > 0 ||
            adoption is not null ||
            acceptance is not null ||
            baseline is not null ||
            hasLegacyRetention ||
            hasTwoServerQualification;
        var canCancel =
            !archived &&
            (lifecycle == MigrationSessionLifecycleStatuses.Active ||
             (lifecycle == MigrationSessionLifecycleStatuses.Closed &&
              string.Equals(
                  effectiveIntakeStatus,
                  "expired",
                  StringComparison.OrdinalIgnoreCase))) &&
            (effectiveIntakeStatus == "awaiting-package" ||
             effectiveIntakeStatus == "expired" ||
             effectiveIntakeStatus == "package-validated") &&
            !hasActiveConversion &&
            !retainedStaging &&
            !hasProductionOwnership;

        var hasPackageEvidence = HasPackageEvidence(revisions);
        var effectiveClosureKind = !string.IsNullOrWhiteSpace(root.ClosureKind)
            ? root.ClosureKind
            : string.Equals(
                effectiveIntakeStatus,
                "expired",
                StringComparison.OrdinalIgnoreCase)
                ? "expired"
                : null;
        var deletionInput = new MigrationSessionDeletionPolicyInput(
            lifecycle,
            effectiveIntakeStatus,
            effectiveClosureKind,
            revisions.Count > 0,
            hasPackageEvidence,
            HasActivePackageAuthority(revisions, now),
            conversions.Count > 0,
            stagings.Count > 0,
            authorities.Count > 0,
            adoption is not null,
            acceptance is not null,
            baseline is not null,
            hasLegacyRetention,
            hasTwoServerQualification,
            TargetStorageVerified: true,
            TargetPackageMaterialPresent: false,
            TargetDecryptedPackageMaterialPresent: false);
        var deletionDecision = MigrationSessionDeletionPolicy.Evaluate(deletionInput);

        var row = new MigrationSessionInventoryRowDto(
            root.MigrationId,
            root.DisplayName,
            sourceIdentity.Adapter,
            sourceIdentity.Display,
            sourceIdentity.Product,
            sourceIdentity.Version,
            adoption?.TargetStackSlug ?? baseline?.TargetStackSlug,
            lifecycle,
            archived,
            statePlan.OverallStatus.CurrentStageCode,
            currentStage.State,
            statePlan.OverallStatus.Code,
            needsAttention,
            warningCount,
            blockerCount,
            root.CreatedAtUtc,
            updatedAtUtc,
            Math.Max(root.StateVersion, 1),
            new MigrationSessionInventoryPrimaryActionDto(
                actionKind,
                actionCode),
            new MigrationSessionInventoryCapabilitiesDto(
                CanOpen: true,
                CanArchive: !archived &&
                    lifecycle != MigrationSessionLifecycleStatuses.Active,
                CanUnarchive: archived,
                CanCancel: canCancel,
                CanDelete: deletionDecision.CanDelete,
                DeleteBlockedReason: deletionDecision.BlockedReason));

        return new ProjectedRow(
            row,
            SearchText(
                root,
                sourceIdentity,
                sources,
                adoption,
                baseline),
            deletionInput,
            revisions
                .Select(x => x.PackageRevisionId)
                .ToArray());
    }

    private static bool IsTerminalConversionStatus(string? status) =>
        status is not null && (
            status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("completed-with-warnings", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("failed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("cancelled", StringComparison.OrdinalIgnoreCase));

    private static bool HasPackageEvidence(
        IEnumerable<RevisionRow> revisions) =>
        revisions.Any(HasPackageEvidence);

    private static bool HasPackageEvidence(RevisionRow revision) =>
        revision.UploadedAtUtc.HasValue ||
        revision.ValidatedAtUtc.HasValue ||
        revision.PackageSizeBytes.HasValue ||
        !string.IsNullOrWhiteSpace(revision.EncryptedPackageSha256) ||
        !string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256) ||
        !string.IsNullOrWhiteSpace(revision.ArchiveMigrationId);

    private static bool HasActivePackageAuthority(
        IEnumerable<RevisionRow> revisions,
        DateTime nowUtc) =>
        revisions.Any(x =>
        {
            var effectivelyExpiredWithoutEvidence =
                string.Equals(
                    ResolveEffectiveRevisionStatus(x, nowUtc),
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

    private static string ResolveEffectiveRevisionStatus(
        RevisionRow revision,
        DateTime nowUtc) =>
        revision.Status == MigrationPackageRevisionAuthority.AwaitingPackageStatus &&
        revision.ActivePurposeKey is not null &&
        revision.ExpiresAtUtc is { } expiresAtUtc &&
        expiresAtUtc <= nowUtc
            ? "expired"
            : revision.Status;

    private MigrationSessionInventoryRowDto AuthorizeDeletion(
        ProjectedRow projected)
    {
        var decision = MigrationSessionDeletionPolicy.Evaluate(
            projected.DeletionInput);
        if (decision.CanDelete)
        {
            var storage = MigrationSessionTargetStorageInspector.Inspect(
                ResolveDataRoot(),
                projected.Row.MigrationId,
                projected.PackageRevisionIds);
            decision = MigrationSessionDeletionPolicy.Evaluate(
                projected.DeletionInput with
                {
                    TargetStorageVerified = storage.Verified,
                    TargetPackageMaterialPresent = storage.HasPackageMaterial,
                    TargetDecryptedPackageMaterialPresent = storage.HasDecryptedPackageMaterial,
                });
        }

        return projected.Row with
        {
            Capabilities = projected.Row.Capabilities with
            {
                CanDelete = decision.CanDelete,
                DeleteBlockedReason = decision.BlockedReason,
            },
        };
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    private static RevisionRow? ResolveAuthoritativeRevision(
        IReadOnlyList<RevisionRow> revisions)
    {
        var activeFinal = revisions.SingleOrDefault(x =>
            x.Purpose == MigrationPackageRevisionAuthority.FinalPurpose &&
            x.ActivePurposeKey is not null);
        if (activeFinal is not null)
        {
            if (activeFinal.Status == MigrationPackageRevisionAuthority.ValidatedStatus)
            {
                return IsValidatedFinal(activeFinal) ? activeFinal : null;
            }

            if (activeFinal.Status != MigrationPackageRevisionAuthority.AwaitingPackageStatus)
            {
                return null;
            }
        }

        return revisions
            .Where(x =>
                x.Purpose == MigrationPackageRevisionAuthority.PreviewPurpose &&
                x.ActivePurposeKey is not null &&
                IsValidatedPreview(x))
            .OrderByDescending(x => x.RevisionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static bool IsValidatedPreview(RevisionRow revision) =>
        revision.Status == MigrationPackageRevisionAuthority.ValidatedStatus &&
        revision.CaptureKind == MigrationPackageRevisionAuthority.PreviewPurpose &&
        revision.SourceFrozen is false &&
        revision.RehearsalOnly is true &&
        !string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256);

    private static bool IsValidatedFinal(RevisionRow revision) =>
        revision.Status == MigrationPackageRevisionAuthority.ValidatedStatus &&
        revision.CaptureKind == MigrationPackageRevisionAuthority.FinalPurpose &&
        revision.SourceFrozen is true &&
        revision.RehearsalOnly is false &&
        !string.IsNullOrWhiteSpace(revision.DecryptedArchiveSha256);

    private static RevisionRow? ResolveSourceRevision(
        IReadOnlyList<RevisionRow> revisions) =>
        ResolveAuthoritativeRevision(revisions) ??
        revisions
            .Where(x => !string.IsNullOrWhiteSpace(x.ArchiveSourceProduct))
            .OrderByDescending(x => x.RevisionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

    private static string ResolveEffectiveIntakeStatus(
        RootRow root,
        IReadOnlyList<RevisionRow> revisions,
        DateTime now)
    {
        if (string.Equals(
                root.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Cancelled,
                StringComparison.Ordinal) ||
            root.CancelledAtUtc.HasValue)
        {
            return "cancelled";
        }

        var current = revisions
            .Where(x => x.ActivePurposeKey is not null)
            .OrderByDescending(x =>
                x.Purpose == MigrationPackageRevisionAuthority.FinalPurpose)
            .ThenByDescending(x => x.RevisionNumber)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault() ??
            revisions
                .OrderByDescending(x => x.RevisionNumber)
                .ThenByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

        return current is null
            ? "package-unavailable"
            : ResolveEffectiveRevisionStatus(current, now);
    }


    private static string? ResolveEffectiveAdoptionStatus(
        AdoptionRow? adoption,
        RevisionRow? revision,
        CandidateRow? candidate,
        StagingRow? staging,
        AuthorityRow? authority,
        bool accepted)
    {
        if (adoption is null)
        {
            return null;
        }

        if (accepted)
        {
            return adoption.Status;
        }

        var bindingCurrent =
            revision is not null &&
            candidate is not null &&
            staging is not null &&
            adoption.PackageRevisionId == revision.Id &&
            adoption.CandidateId == candidate.Id &&
            adoption.StagingRunId == staging.Id &&
            string.Equals(
                staging.Status,
                "verified",
                StringComparison.OrdinalIgnoreCase) &&
            staging.ActiveMigrationKey is not null;

        if (authority is not null)
        {
            bindingCurrent =
                bindingCurrent &&
                authority.PackageRevisionId == adoption.PackageRevisionId &&
                authority.CandidateId == adoption.CandidateId &&
                authority.StagingRunId == adoption.StagingRunId;
        }

        return bindingCurrent ? adoption.Status : "stale";
    }

    private static string ResolveLifecycle(
        RootRow root,
        IReadOnlyList<RevisionRow> revisions,
        BaselineRow? baseline,
        DateTime now)
    {
        if (root.ArchivedAtUtc.HasValue)
        {
            return NormalizeLifecycle(root.LifecycleStatus);
        }

        if (string.Equals(
                root.LifecycleStatus,
                MigrationSessionLifecycleStatuses.Cancelled,
                StringComparison.Ordinal) ||
            root.CancelledAtUtc.HasValue)
        {
            return MigrationSessionLifecycleStatuses.Cancelled;
        }

        if (ResolveEffectiveIntakeStatus(root, revisions, now) == "expired")
        {
            return MigrationSessionLifecycleStatuses.Closed;
        }

        if (string.Equals(
                baseline?.Status,
                "created",
                StringComparison.OrdinalIgnoreCase))
        {
            return MigrationSessionLifecycleStatuses.Completed;
        }

        return NormalizeLifecycle(root.LifecycleStatus);
    }

    private static string NormalizeLifecycle(string? value) =>
        MigrationSessionLifecycleStatuses.All.Contains(value ?? string.Empty)
            ? value!
            : MigrationSessionLifecycleStatuses.Active;

    private static SourceIdentity ResolveSource(
        RootRow root,
        IReadOnlyList<SourceRow> sources,
        IReadOnlyList<RevisionRow> revisions)
    {
        var source = sources.FirstOrDefault();
        var revision = ResolveSourceRevision(revisions);
        var product = source?.Product ?? revision?.ArchiveSourceProduct;
        var version = source?.ProductVersion ?? revision?.ArchiveSourceVersion;

        if (IsMemV010(product, version))
        {
            return new SourceIdentity(
                "mem-v010",
                "Message Easy Mode 0.1.0",
                product,
                version);
        }

        if (!string.IsNullOrWhiteSpace(product))
        {
            return new SourceIdentity(
                "unknown",
                string.IsNullOrWhiteSpace(version)
                    ? product
                    : $"{product} {version}",
                product,
                version);
        }

        return new SourceIdentity(
            "pending",
            "Package not yet received",
            null,
            null);
    }


    private static bool IsMemV010(string? product, string? version)
    {
        if (!string.Equals(
                version,
                "0.1.0",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(product))
        {
            return false;
        }

        var normalized = new string(product
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return normalized is
            "matrixeasymode" or
            "messageeasymode" or
            "mem";
    }

    private static DateTime ResolveUpdatedAtUtc(
        RootRow root,
        IReadOnlyList<SourceRow> sources,
        IReadOnlyList<RevisionRow> revisions,
        IReadOnlyList<ConversionRow> conversions,
        IReadOnlyList<StagingRow> stagings,
        IReadOnlyList<AuthorityRow> authorities,
        AdoptionRow? adoption,
        AcceptanceRow? acceptance,
        BaselineRow? baseline,
        DateTime now)
    {
        var values = new List<DateTime>
        {
            root.CreatedAtUtc,
            root.UpdatedAtUtc == default
                ? root.CreatedAtUtc
                : root.UpdatedAtUtc,
        };

        Add(values, root.CancelledAtUtc);
        Add(values, root.ClosedAtUtc);
        Add(values, root.ArchivedAtUtc);

        foreach (var source in sources)
        {
            Add(values, source.CapturedAtUtc);
        }


        foreach (var revision in revisions)
        {
            values.Add(revision.CreatedAtUtc);
            Add(values, revision.UploadedAtUtc);
            Add(values, revision.ValidatedAtUtc);
            Add(values, revision.SupersededAtUtc);
            Add(values, revision.RetiredAtUtc);
            if (revision.ExpiresAtUtc is { } revisionExpiry &&
                revisionExpiry <= now)
            {
                values.Add(revisionExpiry);
            }
        }

        foreach (var conversion in conversions)
        {
            values.Add(conversion.CreatedAtUtc);
            values.Add(conversion.UpdatedAtUtc);
            Add(values, conversion.StartedAtUtc);
            Add(values, conversion.CompletedAtUtc);
            if (conversion.Candidate is { } candidate)
            {
                values.Add(candidate.CreatedAtUtc);
                Add(values, candidate.VerifiedAtUtc);
                Add(values, candidate.RetiredAtUtc);
            }
        }

        foreach (var staging in stagings)
        {
            values.Add(staging.CreatedAtUtc);
            values.Add(staging.UpdatedAtUtc);
            Add(values, staging.StartedAtUtc);
            Add(values, staging.CompletedAtUtc);
            Add(values, staging.DestroyedAtUtc);
        }

        foreach (var authority in authorities)
        {
            values.Add(authority.CreatedAtUtc);
            Add(values, authority.SupersededAtUtc);
            Add(values, authority.RevokedAtUtc);
        }

        if (adoption is not null)
        {
            values.Add(adoption.CreatedAtUtc);
            values.Add(adoption.UpdatedAtUtc);
            values.Add(adoption.PreparedAtUtc);
            Add(values, adoption.MaterializationStartedAtUtc);
            Add(values, adoption.MaterializationCompletedAtUtc);
            Add(values, adoption.CutoverPreviewCreatedAtUtc);
            Add(values, adoption.CutoverStartedAtUtc);
            Add(values, adoption.CutoverCompletedAtUtc);
            Add(values, adoption.TargetPublicAtUtc);
            Add(values, adoption.ProductionVerificationStartedAtUtc);
            Add(values, adoption.ProductionVerificationCompletedAtUtc);
            Add(values, adoption.RollbackStartedAtUtc);
            Add(values, adoption.RollbackCompletedAtUtc);
        }

        if (acceptance is not null)
        {
            values.Add(acceptance.AcceptedAtUtc);
            values.Add(acceptance.PublicCutoverAtUtc);
        }

        if (baseline is not null)
        {
            values.Add(baseline.CreatedAtUtc);
            values.Add(baseline.UpdatedAtUtc);
            Add(values, baseline.StartedAtUtc);
            Add(values, baseline.CompletedAtUtc);
            Add(values, baseline.BackupCreatedAtUtc);
        }

        return values.Max();
    }

    private static void Add(ICollection<DateTime> values, DateTime? value)
    {
        if (value.HasValue)
        {
            values.Add(value.Value);
        }
    }

    private static string SearchText(
        RootRow root,
        SourceIdentity source,
        IReadOnlyList<SourceRow> sources,
        AdoptionRow? adoption,
        BaselineRow? baseline) =>
        string.Join(
            "\n",
            new[]
            {
                root.MigrationId,
                root.DisplayName,
                source.Display,
                source.Product,
                source.Version,
                adoption?.TargetStackSlug,
                adoption?.TargetDisplayName,
                adoption?.MatrixServerName,
                adoption?.MatrixPublicHost,
                adoption?.ElementPublicHost,
                baseline?.TargetStackSlug,
            }
            .Concat(sources.SelectMany(x => new[]
            {
                x.SourceId,
                x.Product,
                x.ProductVersion,
                x.SourceFingerprint,
            }))
            .Where(x => !string.IsNullOrWhiteSpace(x)));

    private static bool MatchesSearch(
        ProjectedRow row,
        string? search) =>
        search is null ||
        row.SearchText.Contains(
            search,
            StringComparison.OrdinalIgnoreCase);

    private static bool MatchesTarget(
        ProjectedRow row,
        string? target) =>
        target is null ||
        string.Equals(
            row.Row.TargetStackSlug,
            target,
            StringComparison.OrdinalIgnoreCase);

    private static ProjectedRow[] Order(
        IReadOnlyCollection<ProjectedRow> rows,
        MigrationSessionInventoryQuery query)
    {
        IOrderedEnumerable<ProjectedRow> ordered =
            (query.SortBy, query.SortDirection) switch
            {
                ("created", "asc") => rows.OrderBy(x => x.Row.CreatedAtUtc),
                ("created", _) => rows.OrderByDescending(x => x.Row.CreatedAtUtc),
                ("name", "asc") => rows.OrderBy(
                    x => x.Row.DisplayName,
                    StringComparer.OrdinalIgnoreCase),
                ("name", _) => rows.OrderByDescending(
                    x => x.Row.DisplayName,
                    StringComparer.OrdinalIgnoreCase),
                ("stage", "asc") => rows
                    .OrderBy(x => StageOrder(x.Row.CurrentStageCode))
                    .ThenByDescending(x => x.Row.UpdatedAtUtc),
                ("stage", _) => rows
                    .OrderByDescending(x => StageOrder(x.Row.CurrentStageCode))
                    .ThenByDescending(x => x.Row.UpdatedAtUtc),
                ("updated", "asc") => rows.OrderBy(x => x.Row.UpdatedAtUtc),
                _ => rows.OrderByDescending(x => x.Row.UpdatedAtUtc),
            };

        return ordered
            .ThenByDescending(x => x.Row.CreatedAtUtc)
            .ThenBy(x => x.Row.MigrationId, StringComparer.Ordinal)
            .ToArray();
    }

    private static int StageOrder(string stageCode)
    {
        for (var index = 0;
             index < MigrationWorkspaceStageCodes.Ordered.Count;
             index++)
        {
            if (MigrationWorkspaceStageCodes.Ordered[index] == stageCode)
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private static MigrationSessionInventoryResponse Empty(
        MigrationSessionInventoryQuery query) =>
        new(
            1,
            ToDto(query),
            new MigrationSessionInventorySummaryDto(0, 0, 0, 0, 0, 0, 0),
            0,
            1,
            query.PageSize,
            0,
            false,
            false,
            [],
            [],
            []);

    private static MigrationSessionInventoryQueryDto ToDto(
        MigrationSessionInventoryQuery query) =>
        new(
            query.Search,
            query.Lifecycle,
            query.Action,
            query.Stage,
            query.TargetStack,
            query.SortBy,
            query.SortDirection,
            query.IncludeArchived);

    private sealed record RootRow(
        Guid Id,
        string MigrationId,
        string DisplayName,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? CancelledAtUtc,
        string LifecycleStatus,
        DateTime? ClosedAtUtc,
        string? ClosureKind,
        DateTime? ArchivedAtUtc,
        long StateVersion);

    private sealed record SourceRow(
        Guid IntakeId,
        string SourceId,
        string Product,
        string? ProductVersion,
        string SourceFingerprint,
        DateTime? CapturedAtUtc);

    private sealed record RevisionRow(
        Guid Id,
        string PackageRevisionId,
        Guid IntakeId,
        int RevisionNumber,
        string Purpose,
        string Status,
        string RetentionState,
        string? ActivePurposeKey,
        long? PackageSizeBytes,
        string? EncryptedPackageSha256,
        string? DecryptedArchiveSha256,
        string? CaptureKind,
        bool? SourceFrozen,
        bool? RehearsalOnly,
        string? ArchiveMigrationId,
        string? ArchiveSourceProduct,
        string? ArchiveSourceVersion,
        int? ArchiveStackCount,
        DateTime CreatedAtUtc,
        DateTime? ExpiresAtUtc,
        DateTime? UploadedAtUtc,
        DateTime? ValidatedAtUtc,
        DateTime? SupersededAtUtc,
        DateTime? RetiredAtUtc);

    private sealed record CandidateRow(
        Guid Id,
        string VerificationStatus,
        string RetentionState,
        string SourcePackageSha256,
        DateTime CreatedAtUtc,
        DateTime? VerifiedAtUtc,
        DateTime? RetiredAtUtc);

    private sealed record CandidateAttemptRow(
        Guid AttemptId,
        CandidateRow Candidate);

    private sealed record ConversionRow(
        Guid Id,
        Guid IntakeId,
        Guid? PackageRevisionId,
        string? ActiveMigrationKey,
        string Status,
        string? FailureCode,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        CandidateRow? Candidate);

    private sealed record StagingRow(
        Guid Id,
        Guid IntakeId,
        Guid CandidateId,
        string Status,
        string? ActiveMigrationKey,
        bool PublicRoutesCreated,
        string? FailureCode,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        DateTime? DestroyedAtUtc);

    private sealed record AuthorityRow(
        Guid IntakeId,
        Guid PackageRevisionId,
        Guid CandidateId,
        Guid StagingRunId,
        string Status,
        string? ActiveMigrationKey,
        DateTime CreatedAtUtc,
        DateTime? SupersededAtUtc,
        DateTime? RevokedAtUtc);

    private sealed record AdoptionRow(
        Guid IntakeId,
        Guid PackageRevisionId,
        Guid CandidateId,
        Guid StagingRunId,
        string Status,
        string TargetStackSlug,
        string TargetDisplayName,
        string MatrixServerName,
        string MatrixPublicHost,
        string ElementPublicHost,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime PreparedAtUtc,
        DateTime? MaterializationStartedAtUtc,
        DateTime? MaterializationCompletedAtUtc,
        string? MaterializationFailureCode,
        string? CutoverPreviewStatus,
        DateTime? CutoverPreviewCreatedAtUtc,
        string? CutoverStatus,
        DateTime? CutoverStartedAtUtc,
        DateTime? CutoverCompletedAtUtc,
        DateTime? TargetPublicAtUtc,
        bool PublicRoutesCreated,
        bool RouteCompensationAttempted,
        bool RouteCompensationCompleted,
        string? CutoverFailureCode,
        string? ProductionVerificationStatus,
        DateTime? ProductionVerificationStartedAtUtc,
        DateTime? ProductionVerificationCompletedAtUtc,
        string? ProductionVerificationFailureCode,
        string? RollbackStatus,
        DateTime? RollbackStartedAtUtc,
        DateTime? RollbackCompletedAtUtc,
        string? RollbackCompletionStatus,
        string? RollbackFailureCode);

    private sealed record AcceptanceRow(
        Guid IntakeId,
        DateTime AcceptedAtUtc,
        DateTime PublicCutoverAtUtc);

    private sealed record BaselineRow(
        Guid IntakeId,
        string Status,
        string TargetStackSlug,
        DateTime CreatedAtUtc,
        DateTime UpdatedAtUtc,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        DateTime? BackupCreatedAtUtc);

    private sealed record SourceIdentity(
        string Adapter,
        string Display,
        string? Product,
        string? Version);

    private sealed record ProjectedRow(
        MigrationSessionInventoryRowDto Row,
        string SearchText,
        MigrationSessionDeletionPolicyInput DeletionInput,
        IReadOnlyList<string> PackageRevisionIds);
}
