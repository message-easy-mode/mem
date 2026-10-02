namespace Modules.Operator.Migrations.Workspace;

/// <summary>
/// Pure state mapper for the six-stage Migration Workspace. Unknown durable
/// states fail closed into technical review instead of being guessed into a
/// happy-path action.
/// </summary>
public static class MigrationWorkspaceStateMapper
{
    public static MigrationWorkspaceStatePlan Map(MigrationWorkspaceStateInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.LegacyCompatibility)
        {
            return Plan(
                stageIndex: 0,
                state: MigrationWorkspaceStageStates.ActionRequired,
                summaryCode: "migration.workspace.historical-compatibility",
                actionCode: "open-technical-details",
                severity: "info");
        }

        if (input.LatestStagingStatus == "retiring")
            return Plan(2, MigrationWorkspaceStageStates.Running,
                "migration.workspace.staging-retiring", actionCode: null, severity: "info");
        if (input.LatestStagingStatus == "retirement-needs-attention")
            return Plan(2, MigrationWorkspaceStageStates.ActionRequired,
                "migration.workspace.staging-retirement-needs-attention", "review-staging-retirement", "warning");

        if (input.Accepted)
        {
            return input.BaselineStatus switch
            {
                "created" => Plan(
                    5,
                    MigrationWorkspaceStageStates.Completed,
                    "migration.workspace.completed",
                    "open-baseline-backup",
                    "success"),
                "failed" => Plan(
                    5,
                    MigrationWorkspaceStageStates.ActionRequired,
                    "migration.workspace.accepted-baseline-backup-failed",
                    "retry-baseline-backup",
                    "warning",
                    Failure(
                        "baseline-backup-failed",
                        "baseline-backup",
                        "completed",
                        "not-required",
                        "migration-accepted",
                        safeToRetry: true,
                        safeStateSummaryCode: "migration.workspace.safe.accepted-backup-retry",
                        actionCode: "retry-baseline-backup",
                        stageCode: MigrationWorkspaceStageCodes.FinishMigration)),
                _ => Plan(
                    5,
                    MigrationWorkspaceStageStates.Running,
                    "migration.workspace.accepted-baseline-backup-pending",
                    actionCode: null,
                    severity: "info"),
            };
        }

        if (string.Equals(input.RollbackStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "rollback-failed", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Failed,
                "migration.workspace.rollback-failed",
                "open-rollback-recovery",
                "error",
                Failure(
                    input.RollbackFailureCode ?? "rollback-failed",
                    "rollback",
                    "occurred",
                    "failed",
                    "manual-recovery-required",
                    safeToRetry: false,
                    safeStateSummaryCode: "migration.workspace.safe.rollback-manual-recovery",
                    actionCode: "open-rollback-recovery",
                    stageCode: MigrationWorkspaceStageCodes.MakeNewServerLive));
        }

        if (string.Equals(input.RollbackStatus, "executing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.RollbackStatus, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.RollbackCompletionStatus, "coordinated-rollback-complete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "rollback-executing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "target-rolled-back-awaiting-source", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "coordinated-rollback-complete", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.ActionRequired,
                "migration.workspace.rollback-in-progress",
                "open-rollback-recovery",
                "warning");
        }

        if (string.Equals(input.AdoptionStatus, "stale", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.ActionRequired,
                "migration.workspace.new-server-plan-stale",
                "open-technical-details",
                "warning");
        }

        if (string.Equals(input.ProductionVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "production-verification-passed", StringComparison.OrdinalIgnoreCase))
        {
            if (!input.AcceptanceEligible)
                return Plan(5, MigrationWorkspaceStageStates.Blocked,
                    "migration.workspace.finish-blocked", "open-technical-details", "warning");

            return Plan(
                5,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.ready-to-finish",
                "finish-migration",
                "success");
        }

        if (string.Equals(input.ProductionVerificationStatus, "running", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "production-verification-running", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Running,
                "migration.workspace.live-verification-running",
                actionCode: null,
                severity: "info");
        }

        if (string.Equals(input.ProductionVerificationStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "production-verification-failed", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Failed,
                "migration.workspace.live-verification-failed",
                "rerun-live-checks",
                "error",
                Failure(
                    input.ProductionVerificationFailureCode ?? "production-verification-failed",
                    "production-verification",
                    "completed",
                    "not-attempted",
                    input.PublicRoutesCreated ? "new-server-public-unverified" : "public-state-unknown",
                    safeToRetry: true,
                    safeStateSummaryCode: "migration.workspace.safe.public-verification-retry",
                    actionCode: "rerun-live-checks",
                    stageCode: MigrationWorkspaceStageCodes.MakeNewServerLive));
        }

        if (string.Equals(input.AdoptionStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.CutoverStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.public-awaiting-verification",
                "run-live-checks",
                "warning");
        }

        if (string.Equals(input.AdoptionStatus, "cutover-executing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.CutoverStatus, "executing", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Running,
                "migration.workspace.cutover-running",
                actionCode: null,
                severity: "info");
        }

        if (string.Equals(input.AdoptionStatus, "cutover-failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.CutoverStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var compensated = input.RouteCompensationAttempted && input.RouteCompensationCompleted;
            var action = compensated ? "review-go-live-again" : "open-cutover-recovery";
            return Plan(
                4,
                MigrationWorkspaceStageStates.Failed,
                compensated
                    ? "migration.workspace.cutover-failed-compensated"
                    : "migration.workspace.cutover-failed-recovery-required",
                action,
                compensated ? "warning" : "error",
                Failure(
                    input.CutoverFailureCode ?? "cutover-failed",
                    "public-route-cutover",
                    input.PublicRoutesCreated || input.RouteCompensationAttempted ? "occurred" : "not-confirmed",
                    compensated ? "completed" : input.RouteCompensationAttempted ? "incomplete" : "not-attempted",
                    compensated ? "previous-route-state-restored" : "public-route-state-requires-review",
                    safeToRetry: compensated,
                    safeStateSummaryCode: compensated
                        ? "migration.workspace.safe.cutover-restored"
                        : "migration.workspace.safe.cutover-review-required",
                    actionCode: action,
                    stageCode: MigrationWorkspaceStageCodes.MakeNewServerLive));
        }

        if (string.Equals(input.CutoverPreviewStatus, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(4, MigrationWorkspaceStageStates.Blocked,
                "migration.workspace.go-live-blocked", "review-go-live", "warning");
        }

        if (string.Equals(input.CutoverPreviewStatus, "ready", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.AdoptionStatus, "cutover-preview-ready", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.go-live-ready",
                "make-server-live",
                "info");
        }

        if (string.Equals(input.AdoptionStatus, "private-runtime-ready", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                4,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.review-go-live",
                "review-go-live",
                "info");
        }

        if (string.Equals(input.AdoptionStatus, "materializing", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.Running,
                "migration.workspace.private-creation-running",
                actionCode: null,
                severity: "info");
        }

        if (string.Equals(input.AdoptionStatus, "materialization-failed", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.Failed,
                "migration.workspace.private-creation-failed",
                "open-materialization-recovery",
                "error",
                Failure(
                    input.MaterializationFailureCode ?? "materialization-failed",
                    "private-runtime-materialization",
                    "possible-partial-mutation",
                    "not-confirmed",
                    "private-runtime-state-requires-review",
                    safeToRetry: false,
                    safeStateSummaryCode: "migration.workspace.safe.materialization-review-required",
                    actionCode: "open-materialization-recovery",
                    stageCode: MigrationWorkspaceStageCodes.CreateNewServer));
        }

        if (string.Equals(input.AdoptionStatus, "prepared", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.new-server-ready",
                "create-new-server",
                "info");
        }

        if (string.Equals(input.AdoptionStatus, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.Blocked,
                "migration.workspace.new-server-details-blocked",
                "edit-new-server-details",
                "warning");
        }

        if (input.HasActiveProductionAuthority)
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.prepare-new-server",
                "prepare-new-server",
                "info");
        }

        if (string.Equals(input.LatestStagingStatus, "verified", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                3,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.confirm-tested-data",
                "confirm-tested-data",
                "info");
        }

        if (string.Equals(input.LatestStagingStatus, "pending", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.LatestStagingStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                2,
                MigrationWorkspaceStageStates.Running,
                "migration.workspace.private-test-running",
                actionCode: null,
                severity: "info");
        }

        if (string.Equals(input.LatestStagingStatus, "destroyed", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                2,
                MigrationWorkspaceStageStates.ActionRequired,
                "migration.workspace.private-test-destroyed",
                "recreate-private-test",
                "warning");
        }

        if (string.Equals(input.LatestStagingStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.LatestStagingStatus, "failed-cleaned", StringComparison.OrdinalIgnoreCase))
        {
            var retained = input.LatestStagingRetained;
            var action = retained ? "review-staging-retirement" : "retry-private-test";
            return Plan(
                2,
                MigrationWorkspaceStageStates.Failed,
                retained
                    ? "migration.workspace.private-test-failed-retained"
                    : "migration.workspace.private-test-failed-cleaned",
                action,
                retained ? "error" : "warning",
                Failure(
                    input.StagingFailureCode ?? "private-test-failed",
                    "private-test",
                    retained ? "private-runtime-retained" : "private-runtime-cleaned",
                    retained ? "cleanup-required" : "completed",
                    retained ? "private-test-resources-retained" : "no-active-private-test-runtime",
                    safeToRetry: !retained,
                    safeStateSummaryCode: retained
                        ? "migration.workspace.safe.private-test-cleanup-required"
                        : "migration.workspace.safe.private-test-retry",
                    actionCode: action,
                    stageCode: MigrationWorkspaceStageCodes.PrepareAndTest));
        }

        if (input.HasVerifiedCandidate)
        {
            return Plan(
                2,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.private-test-ready",
                "start-private-test",
                "info");
        }

        if (string.Equals(input.LatestConversionStatus, "pending", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.LatestConversionStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                2,
                MigrationWorkspaceStageStates.Running,
                "migration.workspace.preparation-running",
                actionCode: null,
                severity: "info");
        }

        if (string.Equals(input.LatestConversionStatus, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(input.LatestConversionStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                2,
                MigrationWorkspaceStageStates.Failed,
                "migration.workspace.preparation-failed",
                "retry-conversion",
                "error",
                Failure(
                    input.ConversionFailureCode ?? "conversion-failed",
                    "migration-data-preparation",
                    "no-verified-candidate",
                    "not-required",
                    "source-package-retained",
                    safeToRetry: true,
                    safeStateSummaryCode: "migration.workspace.safe.preparation-retry",
                    actionCode: "retry-conversion",
                    stageCode: MigrationWorkspaceStageCodes.PrepareAndTest));
        }

        if (string.Equals(input.IntakeStatus, "package-validated", StringComparison.OrdinalIgnoreCase))
        {
            if (input.BlockerCount > 0)
            {
                return Plan(
                    1,
                    MigrationWorkspaceStageStates.Blocked,
                    "migration.workspace.source-blocked",
                    "review-source-issues",
                    "warning");
            }

            return Plan(
                1,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.source-ready",
                "start-conversion",
                "info");
        }

        if (string.Equals(input.IntakeStatus, "awaiting-package", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                0,
                MigrationWorkspaceStageStates.Ready,
                "migration.workspace.awaiting-package",
                "upload-package",
                "info");
        }

        if (string.Equals(input.IntakeStatus, "expired", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                0,
                MigrationWorkspaceStageStates.Failed,
                "migration.workspace.package-request-expired",
                "create-new-migration",
                "error");
        }

        if (string.Equals(input.IntakeStatus, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return Plan(
                0,
                MigrationWorkspaceStageStates.Closed,
                "migration.workspace.cancelled",
                actionCode: null,
                severity: "info");
        }

        var nearestStage = input.HasProductionAdoption || !string.IsNullOrWhiteSpace(input.AdoptionStatus)
            ? 3
            : input.HasVerifiedCandidate || !string.IsNullOrWhiteSpace(input.LatestConversionStatus) ||
              !string.IsNullOrWhiteSpace(input.LatestStagingStatus)
                ? 2
                : string.Equals(input.IntakeStatus, "package-validated", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 0;

        return Plan(
            nearestStage,
            MigrationWorkspaceStageStates.ActionRequired,
            "migration.workspace.unknown-state",
            "open-technical-details",
            "warning");
    }

    private static MigrationWorkspaceStatePlan Plan(
        int stageIndex,
        string state,
        string summaryCode,
        string? actionCode,
        string severity,
        MigrationWorkspaceFailureOutcome? failureOutcome = null)
    {
        var stageCode = MigrationWorkspaceStageCodes.Ordered[stageIndex];
        var action = actionCode is null
            ? null
            : new MigrationWorkspaceAction(actionCode, true, stageCode);

        var stages = MigrationWorkspaceStageCodes.Ordered
            .Select((code, index) => new MigrationWorkspaceStage(
                Code: code,
                State: index < stageIndex
                    ? MigrationWorkspaceStageStates.Completed
                    : index == stageIndex
                        ? state
                        : MigrationWorkspaceStageStates.NotStarted,
                Unlocked: index <= stageIndex,
                CompletedAtUtc: null,
                PrimaryAction: index == stageIndex ? action : null,
                SecondaryActions: Array.Empty<MigrationWorkspaceAction>(),
                SummaryCode: index == stageIndex
                    ? summaryCode
                    : index < stageIndex
                        ? $"migration.workspace.stage.{code}.completed"
                        : $"migration.workspace.stage.{code}.not-started",
                Problems: Array.Empty<MigrationWorkspaceProblem>(),
                EvidenceSummary: new MigrationWorkspaceStageEvidenceSummary(0, null, null),
                OperationSummary: null,
                FailureOutcome: index == stageIndex ? failureOutcome : null))
            .ToArray();

        return new MigrationWorkspaceStatePlan(
            CurrentStageIndex: stageIndex,
            OverallStatus: new MigrationWorkspaceOverallStatus(
                Code: summaryCode,
                Severity: severity,
                CurrentStageCode: stageCode,
                NextAction: action),
            Stages: stages);
    }

    private static MigrationWorkspaceFailureOutcome Failure(
        string failureCode,
        string failedComponent,
        string mutationState,
        string compensationState,
        string observedCurrentState,
        bool safeToRetry,
        string safeStateSummaryCode,
        string actionCode,
        string stageCode) =>
        new(
            FailureCode: failureCode,
            FailedComponent: failedComponent,
            MutationState: mutationState,
            CompensationState: compensationState,
            ObservedCurrentState: observedCurrentState,
            SafeToRetry: safeToRetry,
            SafeStateSummaryCode: safeStateSummaryCode,
            NextAction: new MigrationWorkspaceAction(actionCode, true, stageCode));
}

public sealed record MigrationWorkspaceStateInput(
    string IntakeStatus,
    int BlockerCount,
    bool LegacyCompatibility,
    string? LatestConversionStatus,
    string? ConversionFailureCode,
    bool HasVerifiedCandidate,
    string? LatestStagingStatus,
    bool LatestStagingRetained,
    string? StagingFailureCode,
    bool HasActiveProductionAuthority,
    bool HasProductionAdoption,
    string? AdoptionStatus,
    string? MaterializationFailureCode,
    string? CutoverPreviewStatus,
    string? CutoverStatus,
    bool PublicRoutesCreated,
    bool RouteCompensationAttempted,
    bool RouteCompensationCompleted,
    string? CutoverFailureCode,
    string? ProductionVerificationStatus,
    string? ProductionVerificationFailureCode,
    string? RollbackStatus,
    string? RollbackCompletionStatus,
    string? RollbackFailureCode,
    bool Accepted,
    string? BaselineStatus,
    bool AcceptanceEligible = false);

public sealed record MigrationWorkspaceStatePlan(
    int CurrentStageIndex,
    MigrationWorkspaceOverallStatus OverallStatus,
    IReadOnlyList<MigrationWorkspaceStage> Stages);
