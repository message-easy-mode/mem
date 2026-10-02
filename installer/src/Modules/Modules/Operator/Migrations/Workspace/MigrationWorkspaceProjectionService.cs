using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Migrations;

namespace Modules.Operator.Migrations.Workspace;

/// <summary>
/// Composes existing durable migration records into the six-stage guided
/// workspace. The service is read-only and deliberately excludes host paths,
/// protected age identities, raw manifests, evidence JSON, and credentials.
/// </summary>
public sealed class MigrationWorkspaceProjectionService
{
    private readonly MemDbContext _db;
    private readonly MigrationSessionProjectionService _sessionProjection;
    private readonly TimeProvider _timeProvider;

    public MigrationWorkspaceProjectionService(
        MemDbContext db,
        MigrationSessionProjectionService sessionProjection,
        TimeProvider timeProvider)
    {
        _db = db;
        _sessionProjection = sessionProjection;
        _timeProvider = timeProvider;
    }

    public async Task<MigrationWorkspaceResponse?> GetAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        // All split reads, including the session detail, share one database
        // snapshot. A deferred SQLite transaction is read-only here: no WAL
        // renegotiation, writer reservation, SaveChanges, or StateVersion bump.
        if (_db.Database.CurrentTransaction is not null)
            return await GetSnapshotAsync(migrationId, cancellationToken);

        if (_db.Database.GetDbConnection() is SqliteConnection connection)
        {
            var openedHere = connection.State != ConnectionState.Open;
            if (openedHere) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var native = connection.BeginTransaction(IsolationLevel.Serializable, deferred: true);
                await using var transaction = await _db.Database.UseTransactionAsync(native, cancellationToken);
                return await GetSnapshotAsync(migrationId, cancellationToken);
            }
            finally
            {
                if (openedHere) await connection.CloseAsync();
            }
        }

        await using var snapshot = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        return await GetSnapshotAsync(migrationId, cancellationToken);
    }

    private async Task<MigrationWorkspaceResponse?> GetSnapshotAsync(
        string migrationId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            return null;
        }

        var detail = await _sessionProjection.GetAsync(migrationId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var intake = await _db.MigrationIntakes
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Sources)
            .Include(x => x.PackageRevisions)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.PackageRevision)
            .Include(x => x.ConversionAttempts)
                .ThenInclude(x => x.CandidateArtifact)
            .Include(x => x.StagingRuns)
            .Include(x => x.ProductionAuthorities)
            .Include(x => x.ProductionAdoption)
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.LegacyRetentionRecord)
            .Include(x => x.Acceptance)
                .ThenInclude(x => x!.BaselineBackupHandoff)
            .Include(x => x.LegacyRetentionRecord)
            .Include(x => x.BaselineBackupHandoff)
            .Include(x => x.TwoServerQualification)
            .SingleOrDefaultAsync(x => x.IntakeId == migrationId, cancellationToken);

        if (intake is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var authoritativeRevision = ResolveAuthoritativePackageRevision(intake);
        var displayRevision = authoritativeRevision ??
            MigrationPackageRevisionAuthority.ResolveCurrentPackage(intake.PackageRevisions);
        var authoritativeAttempts = authoritativeRevision is null
            ? Array.Empty<MigrationConversionAttemptEntity>()
            : intake.ConversionAttempts
                .Where(x => x.MigrationPackageRevisionEntityId == authoritativeRevision.Id)
                .ToArray();

        var latestConversion = authoritativeAttempts
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var verifiedCandidate = authoritativeAttempts
            .Select(x => x.CandidateArtifact)
            .Where(x =>
                x is not null &&
                string.Equals(x.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.RetentionState, "active", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.SourcePackageSha256,
                    authoritativeRevision!.DecryptedArchiveSha256,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x!.CreatedAtUtc)
            .FirstOrDefault();

        var authoritativeCandidateIds = authoritativeAttempts
            .Where(x => x.CandidateArtifact is not null)
            .Select(x => x.CandidateArtifact!.Id)
            .ToHashSet();
        var latestStaging = intake.StagingRuns
            .Where(x => authoritativeCandidateIds.Contains(x.MigrationCandidateArtifactEntityId))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        var activeAuthority = intake.ProductionAuthorities
            .Where(x =>
                string.Equals(x.Status, MigrationProductionAuthorityStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
                x.ActiveMigrationKey is not null)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
        var adoption = intake.ProductionAdoption;
        var acceptance = intake.Acceptance;
        var baseline = intake.BaselineBackupHandoff ?? acceptance?.BaselineBackupHandoff;
        var retention = intake.LegacyRetentionRecord ?? acceptance?.LegacyRetentionRecord;
        var activeAuthorityUsable = activeAuthority is not null &&
            latestStaging is not null &&
            string.Equals(latestStaging.Status, "verified", StringComparison.OrdinalIgnoreCase) &&
            latestStaging.ActiveMigrationKey is not null &&
            activeAuthority.MigrationStagingRunEntityId == latestStaging.Id &&
            activeAuthority.MigrationCandidateArtifactEntityId == verifiedCandidate?.Id &&
            activeAuthority.MigrationPackageRevisionEntityId == authoritativeRevision?.Id;
        var effectiveAdoptionStatus = ResolveEffectiveAdoptionStatus(
            adoption,
            authoritativeRevision,
            verifiedCandidate,
            latestStaging,
            activeAuthority,
            acceptance is not null);

        var stateInput = new MigrationWorkspaceStateInput(
            IntakeStatus: ResolveEffectiveIntakeStatus(intake, now),
            BlockerCount: detail.Session.BlockerCount,
            LegacyCompatibility: false,
            LatestConversionStatus: latestConversion?.Status,
            ConversionFailureCode: latestConversion?.FailureCode,
            HasVerifiedCandidate: verifiedCandidate is not null,
            LatestStagingStatus: latestStaging?.Status,
            LatestStagingRetained: latestStaging?.ActiveMigrationKey is not null,
            StagingFailureCode: latestStaging?.FailureCode,
            HasActiveProductionAuthority: activeAuthorityUsable,
            HasProductionAdoption: adoption is not null,
            AdoptionStatus: effectiveAdoptionStatus == "cutover-preview-ready" &&
                adoption?.CutoverPreviewExpiresAtUtc <= now ? "private-runtime-ready" : effectiveAdoptionStatus,
            MaterializationFailureCode: adoption?.MaterializationFailureCode,
            CutoverPreviewStatus: effectiveAdoptionStatus == "stale" ? null :
                adoption?.CutoverPreviewStatus == "ready" && adoption.CutoverPreviewExpiresAtUtc <= now
                    ? "expired" : adoption?.CutoverPreviewStatus,
            CutoverStatus: effectiveAdoptionStatus == "stale" ? null : adoption?.CutoverStatus,
            PublicRoutesCreated: adoption?.PublicRoutesCreated ?? false,
            RouteCompensationAttempted: adoption?.RouteCompensationAttempted ?? false,
            RouteCompensationCompleted: adoption?.RouteCompensationCompleted ?? false,
            CutoverFailureCode: adoption?.CutoverFailureCode,
            ProductionVerificationStatus: effectiveAdoptionStatus == "stale" ? null : adoption?.ProductionVerificationStatus,
            ProductionVerificationFailureCode: adoption?.ProductionVerificationFailureCode,
            RollbackStatus: adoption?.RollbackStatus,
            RollbackCompletionStatus: adoption?.RollbackCompletionStatus,
            RollbackFailureCode: adoption?.RollbackFailureCode,
            Accepted: acceptance is not null,
            BaselineStatus: baseline?.Status,
            AcceptanceEligible: MigrationAcceptanceEligibility.BuildBlockers(
                adoption, now, effectiveAdoptionStatus == "stale" ? "stale" : null).Count == 0);

        var statePlan = MigrationWorkspaceStateMapper.Map(stateInput);
        var operationByStage = BuildOperationSummaries(
            latestConversion,
            latestStaging,
            adoption,
            baseline);
        var stageEvidence = BuildStageEvidence(
            intake,
            displayRevision,
            latestConversion,
            verifiedCandidate,
            latestStaging,
            activeAuthority,
            adoption,
            acceptance,
            retention,
            baseline,
            now);
        var completedAt = BuildStageCompletionTimes(
            displayRevision,
            latestConversion,
            latestStaging,
            adoption,
            acceptance,
            baseline);
        var problems = BuildStageProblems(detail, latestConversion, latestStaging, adoption, baseline);

        var stages = statePlan.Stages
            .Select(stage => stage with
            {
                CompletedAtUtc = completedAt.GetValueOrDefault(stage.Code),
                EvidenceSummary = stageEvidence.GetValueOrDefault(
                    stage.Code,
                    new MigrationWorkspaceStageEvidenceSummary(0, null, null)),
                OperationSummary = operationByStage.GetValueOrDefault(stage.Code),
                Problems = problems.GetValueOrDefault(
                    stage.Code,
                    Array.Empty<MigrationWorkspaceProblem>()),
            })
            .ToArray();

        var currentStage = stages[statePlan.CurrentStageIndex];
        var currentOperation = currentStage.OperationSummary is { } operation &&
            IsRunningStatus(operation.Status)
                ? operation
                : null;
        var sourceRecord = detail.Sources.FirstOrDefault();

        var response = new MigrationWorkspaceResponse(
            SchemaVersion: 2,
            Migration: new MigrationWorkspaceIdentity(
                detail.Session.MigrationId,
                detail.Session.DisplayName,
                detail.Session.Status,
                ToOffset(detail.Session.CreatedAtUtc),
                ToOffset(detail.Session.UpdatedAtUtc)),
            Source: new MigrationWorkspaceSource(
                detail.Session.SourceAdapter,
                detail.Session.SourceDisplay,
                sourceRecord?.Product ?? displayRevision?.ArchiveSourceProduct,
                sourceRecord?.ProductVersion ?? displayRevision?.ArchiveSourceVersion,
                adoption?.MatrixServerName ?? latestStaging?.MatrixServerName,
                adoption?.ExpectedUsersCount ?? latestStaging?.UsersCount,
                adoption?.ExpectedRoomsCount ?? latestStaging?.RoomsCount,
                adoption?.ExpectedEventsCount ?? latestStaging?.EventsCount,
                detail.Session.BlockerCount,
                detail.Session.WarningCount,
                detail.Session.AdvisoryCount),
            Target: new MigrationWorkspaceTarget(
                PlanPrepared: adoption is not null,
                Status: effectiveAdoptionStatus ?? "not-prepared",
                StackSlug: EmptyToNull(adoption?.TargetStackSlug),
                DisplayName: EmptyToNull(adoption?.TargetDisplayName),
                MatrixHost: EmptyToNull(adoption?.MatrixPublicHost),
                ElementHost: EmptyToNull(adoption?.ElementPublicHost)),
            OverallStatus: statePlan.OverallStatus,
            Stages: stages,
            CurrentOperation: currentOperation,
            Activity: BuildActivity(
                intake,
                displayRevision,
                latestConversion,
                verifiedCandidate,
                latestStaging,
                activeAuthority,
                adoption,
                acceptance,
                baseline),
            Verification: new MigrationWorkspaceVerification(
                Status: adoption?.ProductionVerificationStatus ?? "not-run",
                HasRun: adoption?.ProductionVerificationStartedAtUtc is not null ||
                    adoption?.ProductionVerificationCompletedAtUtc is not null,
                AllPassed: adoption?.ProductionVerificationStatus switch
                {
                    "passed" => true,
                    "failed" => false,
                    _ => null,
                },
                CheckCount: adoption?.ProductionVerificationCheckCount ?? 0,
                FailedCheckCount: adoption?.ProductionVerificationFailedCheckCount ?? 0,
                CheckedAtUtc: ToOffset(adoption?.ProductionVerificationCompletedAtUtc)),
            Retention: new MigrationWorkspaceRetention(
                Status: retention?.Status ?? "not-created",
                RetainUntilUtc: ToOffset(retention?.RetainUntilUtc),
                AutomaticDeletionAllowed: retention?.AutomaticDeletionAllowed),
            Evidence: BuildEvidenceSummary(
                intake,
                displayRevision,
                latestConversion,
                verifiedCandidate,
                latestStaging,
                activeAuthority,
                adoption,
                acceptance,
                retention,
                baseline,
                now),
            AdvancedTools: BuildAdvancedTools(
                intake,
                authoritativeRevision,
                latestStaging,
                adoption,
                acceptance),
            Warnings: BuildWarnings(detail, statePlan),
            Guided: new MigrationWorkspaceGuidedState(
                Revision: "",
                CurrentStageCode: currentStage.Code,
                StageState: currentStage.State,
                CurrentOperation: currentOperation,
                NextAction: currentStage.PrimaryAction,
                Blocker: currentStage.Problems.FirstOrDefault() ??
                    (currentStage.State is MigrationWorkspaceStageStates.Blocked or MigrationWorkspaceStageStates.Failed
                        ? new MigrationWorkspaceProblem(currentStage.SummaryCode, statePlan.OverallStatus.Severity, null)
                        : null),
                UncertaintyState: statePlan.OverallStatus.Code == "migration.workspace.unknown-state"
                    ? "unknown" : "known",
                Detail: detail,
                ProductionAuthorized: activeAuthorityUsable || acceptance is not null ||
                    authoritativeRevision is { Purpose: "final", CaptureKind: "final", SourceFrozen: true, RehearsalOnly: false },
                ProductionAuthorityType: activeAuthorityUsable ? activeAuthority!.AuthorityType :
                    authoritativeRevision is { Purpose: "final", CaptureKind: "final", SourceFrozen: true, RehearsalOnly: false }
                        ? "final-frozen" : null,
                ConversionAttemptId: latestConversion?.ConversionAttemptId,
                ConversionStatus: latestConversion?.Status,
                HasVerifiedCandidate: verifiedCandidate is not null,
                StagingRunId: latestStaging?.StagingRunId,
                StagingStatus: latestStaging?.Status,
                StagingRetained: latestStaging?.ActiveMigrationKey is not null,
                AdoptionPlanId: adoption?.AdoptionPlanId,
                PrivateRuntimeReady: effectiveAdoptionStatus != "stale" && HasPrivateRuntime(adoption),
                CutoverPreviewId: adoption?.CutoverPreviewId,
                CutoverPreviewStatus: stateInput.CutoverPreviewStatus,
                PublicRoutesCreated: adoption?.PublicRoutesCreated ?? false,
                RuntimePromotionCompleted: adoption?.RuntimePromotionCompleted ?? false,
                Accepted: acceptance is not null,
                AcceptanceId: acceptance?.AcceptanceId,
                BaselineBackupStatus: baseline?.Status ?? "not-created",
                BaselineBackupId: baseline?.HandoffId,
                BaselineCatalogEntryId: baseline?.CatalogEntryId,
                OperationRevisions: new Dictionary<string, string>
                {
                    // Only actual cancellation changes this receipt. Upload,
                    // expiry, unrelated StateVersion changes and later operations
                    // must not be mistaken for acceptance of a lost cancel POST.
                    ["cancel-migration"] = Token(
                        intake.LifecycleStatus == MigrationSessionLifecycleStatuses.Cancelled,
                        intake.CancelledAtUtc),
                    ["upload-package"] = Token(displayRevision?.PackageRevisionId, displayRevision?.Status,
                        displayRevision?.EncryptedPackageSha256, displayRevision?.ValidationCode),
                    ["conversion"] = Token(authoritativeRevision?.PackageRevisionId, latestConversion?.ConversionAttemptId,
                        latestConversion?.Status, latestConversion?.CompletedAtUtc),
                    ["private-test"] = Token(verifiedCandidate?.CandidateArtifactId, latestStaging?.StagingRunId,
                        latestStaging?.Status, latestStaging?.ActiveMigrationKey),
                    ["create-new-server"] = Token(adoption?.AdoptionPlanId, adoption?.MaterializationId,
                        adoption?.MaterializationStatus, adoption?.MaterializationCompletedAtUtc),
                    ["review-go-live"] = Token(adoption?.AdoptionPlanId, adoption?.CutoverPreviewId,
                        adoption?.CutoverPreviewStatus, adoption?.CutoverPreviewSha256),
                    ["make-server-live"] = Token(adoption?.AdoptionPlanId, adoption?.CutoverExecutionId,
                        adoption?.CutoverStatus, adoption?.ProductionVerificationId, adoption?.ProductionVerificationStatus),
                    ["finish-migration"] = Token(acceptance?.AcceptanceId, baseline?.HandoffId, baseline?.Status),
                    ["baseline-backup"] = Token(acceptance?.AcceptanceId, baseline?.HandoffId,
                        baseline?.AttemptCount, baseline?.Status, baseline?.CompletedAtUtc),
                },
                Cancellation: MigrationSessionLifecycleService.BuildCancellation(intake, now)));

        return response with { Guided = response.Guided with { Revision = Token(response) } };
    }

    private static string Token(params object?[] values) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values))).ToLowerInvariant();

    private static bool HasPrivateRuntime(MigrationProductionAdoptionEntity? adoption) =>
        adoption is { ProductionDatabaseImported: true, MatrixProductionContainerStarted: true,
            MatrixProductionHealthPassed: true, ElementProductionContainerStarted: true,
            ElementProductionHealthPassed: true, RuntimeManifestSaved: true,
            DatabaseOwnershipSaved: true, RuntimeRecordsCreated: true };

    private static IReadOnlyDictionary<string, MigrationWorkspaceOperationSummary?> BuildOperationSummaries(
        MigrationConversionAttemptEntity? conversion,
        MigrationStagingRunEntity? staging,
        MigrationProductionAdoptionEntity? adoption,
        MigrationBaselineBackupHandoffEntity? baseline)
    {
        var result = new Dictionary<string, MigrationWorkspaceOperationSummary?>();

        if (conversion is not null)
        {
            result[MigrationWorkspaceStageCodes.PrepareAndTest] = new MigrationWorkspaceOperationSummary(
                conversion.ConversionAttemptId,
                "prepare-migration-data",
                conversion.Status,
                EmptyToNull(conversion.CurrentStep),
                ToOffset(conversion.CreatedAtUtc),
                ToOffset(conversion.StartedAtUtc),
                ToOffset(conversion.CompletedAtUtc));
        }

        if (staging is not null &&
            (conversion is null || staging.CreatedAtUtc >= conversion.CreatedAtUtc))
        {
            result[MigrationWorkspaceStageCodes.PrepareAndTest] = new MigrationWorkspaceOperationSummary(
                staging.StagingRunId,
                "private-test",
                staging.Status,
                EmptyToNull(staging.CurrentStep),
                ToOffset(staging.CreatedAtUtc),
                ToOffset(staging.StartedAtUtc),
                ToOffset(staging.CompletedAtUtc ?? staging.DestroyedAtUtc));
        }

        if (adoption is not null)
        {
            if (adoption.Status is "prepared" or "blocked" or "materializing" or "materialization-failed")
            {
                result[MigrationWorkspaceStageCodes.CreateNewServer] = new MigrationWorkspaceOperationSummary(
                    adoption.MaterializationId ?? adoption.AdoptionPlanId,
                    adoption.Status is "prepared" or "blocked"
                        ? "prepare-new-server"
                        : "create-new-server",
                    adoption.MaterializationStatus ?? adoption.Status,
                    adoption.Status,
                    ToOffset(adoption.CreatedAtUtc),
                    ToOffset(adoption.MaterializationStartedAtUtc),
                    ToOffset(adoption.MaterializationCompletedAtUtc));
            }

            if (adoption.Status is not "prepared" and not "blocked" and not "materializing" and not "materialization-failed")
            {
                var operation = adoption.RollbackStatus is not null
                    ? "migration-rollback"
                    : adoption.ProductionVerificationStatus is not null
                        ? "live-verification"
                        : "make-server-live";
                var operationId = adoption.RollbackExecutionId ??
                    adoption.ProductionVerificationId ??
                    adoption.CutoverExecutionId ??
                    adoption.CutoverPreviewId ??
                    adoption.AdoptionPlanId;
                var status = adoption.RollbackStatus ??
                    adoption.ProductionVerificationStatus ??
                    adoption.CutoverStatus ??
                    adoption.Status;
                var started = adoption.RollbackStartedAtUtc ??
                    adoption.ProductionVerificationStartedAtUtc ??
                    adoption.CutoverStartedAtUtc ??
                    adoption.CutoverPreviewCreatedAtUtc;
                var completed = adoption.RollbackCompletedAtUtc ??
                    adoption.ProductionVerificationCompletedAtUtc ??
                    adoption.CutoverCompletedAtUtc;

                result[MigrationWorkspaceStageCodes.MakeNewServerLive] = new MigrationWorkspaceOperationSummary(
                    operationId,
                    operation,
                    status,
                    adoption.Status,
                    ToOffset(adoption.PreparedAtUtc),
                    ToOffset(started),
                    ToOffset(completed));
            }
        }

        if (baseline is not null)
        {
            result[MigrationWorkspaceStageCodes.FinishMigration] = new MigrationWorkspaceOperationSummary(
                baseline.HandoffId,
                "baseline-backup",
                baseline.Status,
                baseline.Status,
                ToOffset(baseline.CreatedAtUtc),
                ToOffset(baseline.StartedAtUtc),
                ToOffset(baseline.CompletedAtUtc));
        }

        return result;
    }

    private static IReadOnlyDictionary<string, DateTimeOffset?> BuildStageCompletionTimes(
        MigrationPackageRevisionEntity? packageRevision,
        MigrationConversionAttemptEntity? conversion,
        MigrationStagingRunEntity? staging,
        MigrationProductionAdoptionEntity? adoption,
        MigrationAcceptanceEntity? acceptance,
        MigrationBaselineBackupHandoffEntity? baseline) =>
        new Dictionary<string, DateTimeOffset?>
        {
            [MigrationWorkspaceStageCodes.CreateAndUploadPackage] =
                ToOffset(packageRevision?.ValidatedAtUtc),
            [MigrationWorkspaceStageCodes.ReviewOldServer] =
                ToOffset(conversion?.StartedAtUtc ?? conversion?.CreatedAtUtc),
            [MigrationWorkspaceStageCodes.PrepareAndTest] =
                string.Equals(staging?.Status, "verified", StringComparison.OrdinalIgnoreCase)
                    ? ToOffset(staging.CompletedAtUtc ?? staging.UpdatedAtUtc)
                    : null,
            [MigrationWorkspaceStageCodes.CreateNewServer] =
                ToOffset(adoption?.MaterializationCompletedAtUtc),
            [MigrationWorkspaceStageCodes.MakeNewServerLive] =
                string.Equals(adoption?.ProductionVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase)
                    ? ToOffset(adoption.ProductionVerificationCompletedAtUtc)
                    : null,
            [MigrationWorkspaceStageCodes.FinishMigration] =
                string.Equals(baseline?.Status, "created", StringComparison.OrdinalIgnoreCase)
                    ? ToOffset(baseline.CompletedAtUtc ?? baseline.BackupCreatedAtUtc)
                    : ToOffset(acceptance?.AcceptedAtUtc),
        };

    private static IReadOnlyDictionary<string, MigrationWorkspaceStageEvidenceSummary> BuildStageEvidence(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity? packageRevision,
        MigrationConversionAttemptEntity? conversion,
        MigrationCandidateArtifactEntity? candidate,
        MigrationStagingRunEntity? staging,
        MigrationProductionAuthorityEntity? authority,
        MigrationProductionAdoptionEntity? adoption,
        MigrationAcceptanceEntity? acceptance,
        LegacyRetentionRecordEntity? retention,
        MigrationBaselineBackupHandoffEntity? baseline,
        DateTime now)
    {
        var routeEvidenceCount = adoption is null
            ? 0
            : CountPresent(
                adoption.CutoverPreviewId,
                adoption.CutoverExecutionId,
                adoption.ProductionVerificationId,
                adoption.RollbackExecutionId);

        return new Dictionary<string, MigrationWorkspaceStageEvidenceSummary>
        {
            [MigrationWorkspaceStageCodes.CreateAndUploadPackage] = new(
                ItemCount: intake.PackageRevisions.Count,
                LatestOccurredAtUtc: Latest(
                    packageRevision?.ValidatedAtUtc,
                    packageRevision?.UploadedAtUtc,
                    packageRevision?.CreatedAtUtc),
                LatestStatus: ResolveEffectiveIntakeStatus(intake, now)),
            [MigrationWorkspaceStageCodes.ReviewOldServer] = new(
                ItemCount: intake.Sources.Count,
                LatestOccurredAtUtc: Latest(
                    intake.Sources.MaxBy(x => x.CapturedAtUtc)?.CapturedAtUtc,
                    packageRevision?.ValidatedAtUtc,
                    packageRevision?.CreatedAtUtc),
                LatestStatus: ResolveEffectiveIntakeStatus(intake, now)),
            [MigrationWorkspaceStageCodes.PrepareAndTest] = new(
                ItemCount: CountPresent(conversion?.ConversionAttemptId, candidate?.CandidateArtifactId, staging?.StagingRunId),
                LatestOccurredAtUtc: Latest(
                    conversion?.CompletedAtUtc ?? conversion?.UpdatedAtUtc,
                    candidate?.VerifiedAtUtc ?? candidate?.CreatedAtUtc,
                    staging?.DestroyedAtUtc ?? staging?.CompletedAtUtc ?? staging?.UpdatedAtUtc),
                LatestStatus: staging?.Status ?? candidate?.VerificationStatus ?? conversion?.Status),
            [MigrationWorkspaceStageCodes.CreateNewServer] = new(
                ItemCount: CountPresent(authority?.ProductionAuthorityId, adoption?.AdoptionPlanId, adoption?.MaterializationId),
                LatestOccurredAtUtc: Latest(
                    authority?.CreatedAtUtc,
                    adoption?.MaterializationCompletedAtUtc,
                    adoption?.MaterializationStartedAtUtc,
                    adoption?.PreparedAtUtc),
                LatestStatus: adoption?.MaterializationStatus ?? adoption?.Status ?? authority?.Status),
            [MigrationWorkspaceStageCodes.MakeNewServerLive] = new(
                ItemCount: routeEvidenceCount,
                LatestOccurredAtUtc: Latest(
                    adoption?.RollbackCompletedAtUtc,
                    adoption?.ProductionVerificationCompletedAtUtc,
                    adoption?.CutoverCompletedAtUtc,
                    adoption?.CutoverPreviewCreatedAtUtc),
                LatestStatus: adoption?.RollbackStatus ??
                    adoption?.ProductionVerificationStatus ??
                    adoption?.CutoverStatus ??
                    adoption?.Status),
            [MigrationWorkspaceStageCodes.FinishMigration] = new(
                ItemCount: CountPresent(acceptance?.AcceptanceId, retention?.RetentionRecordId, baseline?.HandoffId),
                LatestOccurredAtUtc: Latest(
                    baseline?.CompletedAtUtc ?? baseline?.UpdatedAtUtc,
                    retention?.CleanupCompletedAtUtc ?? retention?.CreatedAtUtc,
                    acceptance?.AcceptedAtUtc),
                LatestStatus: baseline?.Status ?? retention?.Status ?? (acceptance is null ? null : "accepted")),
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<MigrationWorkspaceProblem>> BuildStageProblems(
        MigrationSessionDetailDto detail,
        MigrationConversionAttemptEntity? conversion,
        MigrationStagingRunEntity? staging,
        MigrationProductionAdoptionEntity? adoption,
        MigrationBaselineBackupHandoffEntity? baseline)
    {
        var result = new Dictionary<string, IReadOnlyList<MigrationWorkspaceProblem>>();

        var sourceProblems = detail.Findings
            .Where(x => x.Severity is "blocker" or "warning")
            .Take(50)
            .Select(x => new MigrationWorkspaceProblem(x.Code, x.Severity, x.Message))
            .ToArray();
        if (sourceProblems.Length > 0)
        {
            result[MigrationWorkspaceStageCodes.ReviewOldServer] = sourceProblems;
        }

        var preparationProblems = new List<MigrationWorkspaceProblem>();
        AddProblem(preparationProblems, conversion?.FailureCode, "error");
        AddProblem(preparationProblems, staging?.FailureCode, "error");
        if (preparationProblems.Count > 0)
        {
            result[MigrationWorkspaceStageCodes.PrepareAndTest] = preparationProblems;
        }

        var createProblems = new List<MigrationWorkspaceProblem>();
        AddProblem(createProblems, adoption?.MaterializationFailureCode, "error");
        if (!string.IsNullOrWhiteSpace(adoption?.BlockerSummary))
        {
            createProblems.Add(new MigrationWorkspaceProblem(
                "migration-production-plan-blocked",
                "warning",
                null));
        }
        if (createProblems.Count > 0)
        {
            result[MigrationWorkspaceStageCodes.CreateNewServer] = createProblems;
        }

        var liveProblems = new List<MigrationWorkspaceProblem>();
        AddProblem(liveProblems, adoption?.CutoverFailureCode, "error");
        AddProblem(liveProblems, adoption?.ProductionVerificationFailureCode, "error");
        AddProblem(liveProblems, adoption?.RollbackFailureCode, "error");
        if (liveProblems.Count > 0)
        {
            result[MigrationWorkspaceStageCodes.MakeNewServerLive] = liveProblems;
        }

        if (!string.IsNullOrWhiteSpace(baseline?.FailureCode))
        {
            result[MigrationWorkspaceStageCodes.FinishMigration] =
            [
                new MigrationWorkspaceProblem(baseline.FailureCode, "error", null),
            ];
        }

        return result;
    }

    private static IReadOnlyList<MigrationWorkspaceActivityItem> BuildActivity(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity? packageRevision,
        MigrationConversionAttemptEntity? conversion,
        MigrationCandidateArtifactEntity? candidate,
        MigrationStagingRunEntity? staging,
        MigrationProductionAuthorityEntity? authority,
        MigrationProductionAdoptionEntity? adoption,
        MigrationAcceptanceEntity? acceptance,
        MigrationBaselineBackupHandoffEntity? baseline)
    {
        var activity = new List<MigrationWorkspaceActivityItem>
        {
            Activity("migration-created", "completed", intake.CreatedAtUtc, intake.IntakeId),
        };

        AddActivity(
            activity,
            "package-uploaded",
            packageRevision?.Status,
            packageRevision?.UploadedAtUtc,
            packageRevision?.PackageRevisionId);
        AddActivity(
            activity,
            "package-verified",
            packageRevision?.Status,
            packageRevision?.ValidatedAtUtc,
            packageRevision?.PackageRevisionId);

        if (conversion is not null)
        {
            AddActivity(activity, "preparation-started", conversion.Status, conversion.StartedAtUtc ?? conversion.CreatedAtUtc, conversion.ConversionAttemptId);
            AddActivity(activity, "preparation-finished", conversion.Status, conversion.CompletedAtUtc, conversion.ConversionAttemptId);
        }

        AddActivity(activity, "candidate-verified", candidate?.VerificationStatus, candidate?.VerifiedAtUtc, candidate?.CandidateArtifactId);

        if (staging is not null)
        {
            AddActivity(activity, "private-test-started", staging.Status, staging.StartedAtUtc ?? staging.CreatedAtUtc, staging.StagingRunId);
            AddActivity(activity, "private-test-finished", staging.Status, staging.CompletedAtUtc, staging.StagingRunId);
            AddActivity(activity, "private-test-destroyed", staging.Status, staging.DestroyedAtUtc, staging.StagingRunId);
        }

        AddActivity(activity, "tested-snapshot-confirmed", authority?.Status, authority?.CreatedAtUtc, authority?.ProductionAuthorityId);

        if (adoption is not null)
        {
            AddActivity(activity, "new-server-details-prepared", adoption.Status, adoption.PreparedAtUtc, adoption.AdoptionPlanId);
            AddActivity(activity, "new-server-creation-started", adoption.MaterializationStatus, adoption.MaterializationStartedAtUtc, adoption.MaterializationId);
            AddActivity(activity, "new-server-created-privately", adoption.MaterializationStatus, adoption.MaterializationCompletedAtUtc, adoption.MaterializationId);
            AddActivity(activity, "go-live-review-prepared", adoption.CutoverPreviewStatus, adoption.CutoverPreviewCreatedAtUtc, adoption.CutoverPreviewId);
            AddActivity(activity, "go-live-started", adoption.CutoverStatus, adoption.CutoverStartedAtUtc, adoption.CutoverExecutionId);
            AddActivity(activity, "go-live-finished", adoption.CutoverStatus, adoption.CutoverCompletedAtUtc, adoption.CutoverExecutionId);
            AddActivity(activity, "live-verification-started", adoption.ProductionVerificationStatus, adoption.ProductionVerificationStartedAtUtc, adoption.ProductionVerificationId);
            AddActivity(activity, "live-verification-finished", adoption.ProductionVerificationStatus, adoption.ProductionVerificationCompletedAtUtc, adoption.ProductionVerificationId);
            AddActivity(activity, "rollback-started", adoption.RollbackStatus, adoption.RollbackStartedAtUtc, adoption.RollbackExecutionId);
            AddActivity(activity, "rollback-finished", adoption.RollbackStatus, adoption.RollbackCompletedAtUtc, adoption.RollbackExecutionId);
        }

        AddActivity(activity, "migration-accepted", "completed", acceptance?.AcceptedAtUtc, acceptance?.AcceptanceId);
        if (baseline is not null)
        {
            AddActivity(activity, "baseline-backup-started", baseline.Status, baseline.StartedAtUtc ?? baseline.CreatedAtUtc, baseline.HandoffId);
            AddActivity(activity, "baseline-backup-finished", baseline.Status, baseline.CompletedAtUtc, baseline.HandoffId);
        }

        return activity
            .Where(x => x.OccurredAtUtc != default)
            .OrderBy(x => x.OccurredAtUtc)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .TakeLast(100)
            .ToArray();
    }

    private static MigrationWorkspaceEvidenceSummary BuildEvidenceSummary(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity? packageRevision,
        MigrationConversionAttemptEntity? conversion,
        MigrationCandidateArtifactEntity? candidate,
        MigrationStagingRunEntity? staging,
        MigrationProductionAuthorityEntity? authority,
        MigrationProductionAdoptionEntity? adoption,
        MigrationAcceptanceEntity? acceptance,
        LegacyRetentionRecordEntity? retention,
        MigrationBaselineBackupHandoffEntity? baseline,
        DateTime now)
    {
        var categories = new[]
        {
            Category(
                "package-and-source",
                ResolveEffectiveIntakeStatus(intake, now),
                intake.PackageRevisions.Count + intake.Sources.Count,
                Latest(
                    packageRevision?.ValidatedAtUtc,
                    packageRevision?.UploadedAtUtc,
                    packageRevision?.CreatedAtUtc)),
            Category(
                "preparation-and-candidate",
                candidate?.VerificationStatus ?? conversion?.Status ?? "not-started",
                CountPresent(conversion?.ConversionAttemptId, candidate?.CandidateArtifactId),
                Latest(conversion?.CompletedAtUtc ?? conversion?.UpdatedAtUtc, candidate?.VerifiedAtUtc ?? candidate?.CreatedAtUtc)),
            Category(
                "private-test",
                staging?.Status ?? "not-started",
                staging is null ? 0 : 1,
                Latest(staging?.DestroyedAtUtc, staging?.CompletedAtUtc, staging?.UpdatedAtUtc)),
            Category(
                "production-authority",
                authority?.Status ?? "not-created",
                authority is null ? 0 : 1,
                ToOffset(authority?.CreatedAtUtc)),
            Category(
                "new-server-creation",
                adoption?.MaterializationStatus ?? adoption?.Status ?? "not-started",
                CountPresent(adoption?.AdoptionPlanId, adoption?.MaterializationId),
                Latest(adoption?.MaterializationCompletedAtUtc, adoption?.MaterializationStartedAtUtc, adoption?.PreparedAtUtc)),
            Category(
                "route-change-and-verification",
                adoption?.ProductionVerificationStatus ?? adoption?.CutoverStatus ?? "not-started",
                CountPresent(adoption?.CutoverPreviewId, adoption?.CutoverExecutionId, adoption?.ProductionVerificationId, adoption?.RollbackExecutionId),
                Latest(adoption?.RollbackCompletedAtUtc, adoption?.ProductionVerificationCompletedAtUtc, adoption?.CutoverCompletedAtUtc)),
            Category(
                "acceptance-and-retention",
                acceptance is null ? "not-started" : retention?.Status ?? "accepted",
                CountPresent(acceptance?.AcceptanceId, retention?.RetentionRecordId),
                Latest(retention?.CleanupCompletedAtUtc ?? retention?.CreatedAtUtc, acceptance?.AcceptedAtUtc)),
            Category(
                "baseline-backup",
                baseline?.Status ?? "not-started",
                baseline is null ? 0 : 1,
                Latest(baseline?.CompletedAtUtc, baseline?.UpdatedAtUtc)),
        };

        return new MigrationWorkspaceEvidenceSummary(categories);
    }

    private static IReadOnlyList<MigrationWorkspaceAdvancedTool> BuildAdvancedTools(
        MigrationIntakeEntity intake,
        MigrationPackageRevisionEntity? authoritativeRevision,
        MigrationStagingRunEntity? staging,
        MigrationProductionAdoptionEntity? adoption,
        MigrationAcceptanceEntity? acceptance)
    {
        var finalRevision = intake.PackageRevisions
            .FirstOrDefault(x =>
                x.Purpose == MigrationPackageRevisionAuthority.FinalPurpose &&
                x.ActivePurposeKey is not null);
        var finalFrozenAvailable = acceptance is null &&
            finalRevision is not null &&
            string.Equals(
                finalRevision.Status,
                MigrationPackageRevisionAuthority.ValidatedStatus,
                StringComparison.OrdinalIgnoreCase);

        return
        [
            new MigrationWorkspaceAdvancedTool(
                "final-frozen-assurance",
                finalFrozenAvailable ? "available" : "unavailable",
                finalFrozenAvailable ? null : "migration.workspace.advanced.final-frozen-unavailable",
                MigrationWorkspaceStageCodes.CreateNewServer),
            new MigrationWorkspaceAdvancedTool(
                "private-test-recovery",
                staging is not null &&
                    (staging.Status is "failed" or "failed-cleaned" or "destroyed")
                        ? "available"
                        : "unavailable",
                staging is not null &&
                    (staging.Status is "failed" or "failed-cleaned" or "destroyed")
                        ? null
                        : "migration.workspace.advanced.private-test-recovery-unavailable",
                MigrationWorkspaceStageCodes.PrepareAndTest),
            new MigrationWorkspaceAdvancedTool(
                "production-recovery",
                adoption?.Status is "materialization-failed" or "cutover-failed" or
                    "production-verification-failed" or "rollback-failed" or
                    "target-rolled-back-awaiting-source"
                        ? "available"
                        : "unavailable",
                adoption?.Status is "materialization-failed" or "cutover-failed" or
                    "production-verification-failed" or "rollback-failed" or
                    "target-rolled-back-awaiting-source"
                        ? null
                        : "migration.workspace.advanced.production-recovery-unavailable",
                MigrationWorkspaceStageCodes.MakeNewServerLive),
            new MigrationWorkspaceAdvancedTool(
                "two-server-qualification",
                acceptance is not null ? "available" : "unavailable",
                acceptance is not null ? null : "migration.workspace.advanced.qualification-requires-acceptance",
                MigrationWorkspaceStageCodes.FinishMigration),
            new MigrationWorkspaceAdvancedTool(
                "completion-report",
                acceptance is not null ? "available" : "unavailable",
                acceptance is not null ? null : "migration.workspace.advanced.report-requires-acceptance",
                MigrationWorkspaceStageCodes.FinishMigration),
            new MigrationWorkspaceAdvancedTool(
                "authoritative-package-details",
                authoritativeRevision is not null || finalRevision is not null ? "available" : "unavailable",
                authoritativeRevision is not null || finalRevision is not null
                    ? null
                    : "migration.workspace.advanced.package-details-unavailable",
                MigrationWorkspaceStageCodes.CreateAndUploadPackage),
        ];
    }

    private static IReadOnlyList<MigrationWorkspaceWarning> BuildWarnings(
        MigrationSessionDetailDto detail,
        MigrationWorkspaceStatePlan statePlan)
    {
        var warnings = detail.Findings
            .Where(x => x.Severity is "warning" or "advisory")
            .Take(50)
            .Select(x => new MigrationWorkspaceWarning(x.Code, x.Severity, x.Message))
            .ToList();

        if (string.Equals(
                statePlan.OverallStatus.Code,
                "migration.workspace.unknown-state",
                StringComparison.Ordinal))
        {
            warnings.Add(new MigrationWorkspaceWarning(
                "migration_workspace_unknown_state",
                "warning",
                null));
        }

        return warnings
            .GroupBy(x => new { x.Code, x.Severity, x.Detail })
            .Select(x => x.First())
            .ToArray();
    }

    private static string? ResolveEffectiveAdoptionStatus(
        MigrationProductionAdoptionEntity? adoption,
        MigrationPackageRevisionEntity? authoritativeRevision,
        MigrationCandidateArtifactEntity? verifiedCandidate,
        MigrationStagingRunEntity? latestStaging,
        MigrationProductionAuthorityEntity? activeAuthority,
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

        var bindingCurrent = authoritativeRevision is not null &&
            verifiedCandidate is not null &&
            latestStaging is not null &&
            adoption.MigrationPackageRevisionEntityId == authoritativeRevision.Id &&
            adoption.MigrationCandidateArtifactEntityId == verifiedCandidate.Id &&
            adoption.MigrationStagingRunEntityId == latestStaging.Id &&
            string.Equals(latestStaging.Status, "verified", StringComparison.OrdinalIgnoreCase) &&
            latestStaging.ActiveMigrationKey is not null;

        if (activeAuthority is not null)
        {
            bindingCurrent = bindingCurrent &&
                activeAuthority.MigrationPackageRevisionEntityId == adoption.MigrationPackageRevisionEntityId &&
                activeAuthority.MigrationCandidateArtifactEntityId == adoption.MigrationCandidateArtifactEntityId &&
                activeAuthority.MigrationStagingRunEntityId == adoption.MigrationStagingRunEntityId;
        }

        return bindingCurrent ? adoption.Status : "stale";
    }

    private static MigrationPackageRevisionEntity? ResolveAuthoritativePackageRevision(
        MigrationIntakeEntity intake) =>
        MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            intake.PackageRevisions);

    private static string ResolveEffectiveIntakeStatus(
        MigrationIntakeEntity intake,
        DateTime now) =>
        MigrationPackageRevisionAuthority.ResolveEffectiveSessionStatus(
            intake,
            now);

    private static MigrationWorkspaceEvidenceCategory Category(
        string code,
        string status,
        int itemCount,
        DateTimeOffset? latestOccurredAtUtc) =>
        new(code, status, itemCount, latestOccurredAtUtc);

    private static MigrationWorkspaceActivityItem Activity(
        string code,
        string status,
        DateTime occurredAtUtc,
        string? relatedObjectId) =>
        new(code, status, ToOffset(occurredAtUtc), relatedObjectId);

    private static void AddActivity(
        ICollection<MigrationWorkspaceActivityItem> activity,
        string code,
        string? status,
        DateTime? occurredAtUtc,
        string? relatedObjectId)
    {
        if (occurredAtUtc.HasValue)
        {
            activity.Add(new MigrationWorkspaceActivityItem(
                code,
                status ?? "recorded",
                ToOffset(occurredAtUtc.Value),
                EmptyToNull(relatedObjectId)));
        }
    }

    private static void AddProblem(
        ICollection<MigrationWorkspaceProblem> problems,
        string? code,
        string severity)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            problems.Add(new MigrationWorkspaceProblem(code, severity, null));
        }
    }

    private static int CountPresent(params string?[] values) =>
        values.Count(x => !string.IsNullOrWhiteSpace(x));

    private static DateTimeOffset? Latest(params DateTime?[] values)
    {
        var latest = values
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty()
            .Max();
        return latest == default ? null : ToOffset(latest);
    }

    private static DateTimeOffset? Latest(params DateTimeOffset?[] values)
    {
        var latest = values
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty()
            .Max();
        return latest == default ? null : latest;
    }

    private static bool IsRunningStatus(string? status) =>
        status is "pending" or "running" or "materializing" or "executing";

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;
}
