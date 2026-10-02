using Modules.Operator.Migrations.Workspace;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationWorkspaceStateMapperTests
{
    public static IEnumerable<object[]> StateCases()
    {
        yield return Case(Input(intakeStatus: "awaiting-package"), 0, "ready", "upload-package");
        yield return Case(Input(intakeStatus: "expired"), 0, "failed", "create-new-migration");
        yield return Case(Input(intakeStatus: "cancelled"), 0, "closed", null);
        yield return Case(Input(), 1, "ready", "start-conversion");
        yield return Case(Input(blockerCount: 2), 1, "blocked", "review-source-issues");
        yield return Case(Input(latestConversionStatus: "running"), 2, "running", null);
        yield return Case(Input(latestConversionStatus: "failed"), 2, "failed", "retry-conversion");
        yield return Case(Input(hasVerifiedCandidate: true), 2, "ready", "start-private-test");
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "retiring", hasActiveProductionAuthority: true), 2, "running", null);
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "retirement-needs-attention", hasActiveProductionAuthority: true), 2, "action-required", "review-staging-retirement");
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "running"), 2, "running", null);
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "verified"), 3, "ready", "confirm-tested-data");
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "destroyed"), 2, "action-required", "recreate-private-test");
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "failed", latestStagingRetained: true), 2, "failed", "review-staging-retirement");
        yield return Case(Input(hasVerifiedCandidate: true, latestStagingStatus: "failed-cleaned"), 2, "failed", "retry-private-test");
        yield return Case(Input(hasActiveProductionAuthority: true), 3, "ready", "prepare-new-server");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "prepared"), 3, "ready", "create-new-server");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "blocked"), 3, "blocked", "edit-new-server-details");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "materializing"), 3, "running", null);
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "materialization-failed"), 3, "failed", "open-materialization-recovery");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "private-runtime-ready"), 4, "ready", "review-go-live");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "cutover-preview-ready", cutoverPreviewStatus: "ready"), 4, "ready", "make-server-live");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "cutover-executing", cutoverStatus: "executing"), 4, "running", null);
        yield return Case(Input(
            hasProductionAdoption: true,
            adoptionStatus: "cutover-failed",
            cutoverStatus: "failed",
            routeCompensationAttempted: true,
            routeCompensationCompleted: true), 4, "failed", "review-go-live-again");
        yield return Case(Input(
            hasProductionAdoption: true,
            adoptionStatus: "cutover-failed",
            cutoverStatus: "failed",
            routeCompensationAttempted: true,
            routeCompensationCompleted: false), 4, "failed", "open-cutover-recovery");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "public-awaiting-verification"), 4, "ready", "run-live-checks");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "production-verification-running", productionVerificationStatus: "running"), 4, "running", null);
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "production-verification-failed", productionVerificationStatus: "failed"), 4, "failed", "rerun-live-checks");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "production-verification-passed", productionVerificationStatus: "passed"), 5, "ready", "finish-migration");
        yield return Case(Input(accepted: true, baselineStatus: "pending"), 5, "running", null);
        yield return Case(Input(accepted: true, baselineStatus: "failed"), 5, "action-required", "retry-baseline-backup");
        yield return Case(Input(accepted: true, baselineStatus: "created"), 5, "completed", "open-baseline-backup");
        yield return Case(Input(hasProductionAdoption: true, adoptionStatus: "rollback-failed", rollbackStatus: "failed"), 4, "failed", "open-rollback-recovery");
        yield return Case(Input(legacyCompatibility: true), 0, "action-required", "open-technical-details");
        yield return Case(Input(intakeStatus: "unexpected-state"), 0, "action-required", "open-technical-details");
    }

    [Theory]
    [MemberData(nameof(StateCases))]
    public void Maps_durable_state_to_one_canonical_visible_stage(
        MigrationWorkspaceStateInput input,
        int expectedStageIndex,
        string expectedState,
        string? expectedAction)
    {
        var result = MigrationWorkspaceStateMapper.Map(input);

        Assert.Equal(6, result.Stages.Count);
        Assert.Equal(
            MigrationWorkspaceStageCodes.Ordered.ToArray(),
            result.Stages.Select(x => x.Code).ToArray());
        Assert.Equal(expectedStageIndex, result.CurrentStageIndex);

        var current = result.Stages[expectedStageIndex];
        Assert.Equal(expectedState, current.State);
        Assert.Equal(expectedAction, current.PrimaryAction?.Code);
        Assert.Equal(current.Code, result.OverallStatus.CurrentStageCode);
        Assert.Equal(expectedAction, result.OverallStatus.NextAction?.Code);

        Assert.All(result.Stages.Take(expectedStageIndex), stage =>
        {
            Assert.Equal(MigrationWorkspaceStageStates.Completed, stage.State);
            Assert.True(stage.Unlocked);
            Assert.Null(stage.PrimaryAction);
        });

        Assert.All(result.Stages.Skip(expectedStageIndex + 1), stage =>
        {
            Assert.Equal(MigrationWorkspaceStageStates.NotStarted, stage.State);
            Assert.False(stage.Unlocked);
            Assert.Null(stage.PrimaryAction);
        });
    }

    [Fact]
    public void Compensated_and_uncompensated_cutover_failures_are_not_conflated()
    {
        var compensated = MigrationWorkspaceStateMapper.Map(Input(
            hasProductionAdoption: true,
            adoptionStatus: "cutover-failed",
            cutoverStatus: "failed",
            publicRoutesCreated: true,
            routeCompensationAttempted: true,
            routeCompensationCompleted: true));
        var uncompensated = MigrationWorkspaceStateMapper.Map(Input(
            hasProductionAdoption: true,
            adoptionStatus: "cutover-failed",
            cutoverStatus: "failed",
            publicRoutesCreated: true,
            routeCompensationAttempted: true,
            routeCompensationCompleted: false));

        var compensatedFailure = compensated.Stages[4].FailureOutcome;
        var uncompensatedFailure = uncompensated.Stages[4].FailureOutcome;

        Assert.NotNull(compensatedFailure);
        Assert.True(compensatedFailure!.SafeToRetry);
        Assert.Equal("completed", compensatedFailure.CompensationState);
        Assert.Equal("previous-route-state-restored", compensatedFailure.ObservedCurrentState);

        Assert.NotNull(uncompensatedFailure);
        Assert.False(uncompensatedFailure!.SafeToRetry);
        Assert.Equal("incomplete", uncompensatedFailure.CompensationState);
        Assert.Equal("public-route-state-requires-review", uncompensatedFailure.ObservedCurrentState);
    }

    [Fact]
    public void Stale_adoption_cannot_advance_using_historical_success()
    {
        var result = MigrationWorkspaceStateMapper.Map(Input(
            hasProductionAdoption: true, adoptionStatus: "stale",
            cutoverPreviewStatus: "ready", productionVerificationStatus: "passed"));
        Assert.Equal(3, result.CurrentStageIndex);
        Assert.Equal("open-technical-details", result.OverallStatus.NextAction?.Code);
        Assert.Equal("action-required", result.Stages[3].State);
    }

    [Fact]
    public void Blocked_preview_is_settled_and_only_offers_explicit_review()
    {
        var result = MigrationWorkspaceStateMapper.Map(Input(
            hasProductionAdoption: true, adoptionStatus: "private-runtime-ready",
            cutoverPreviewStatus: "blocked"));
        Assert.Equal(4, result.CurrentStageIndex);
        Assert.Equal("blocked", result.Stages[4].State);
        Assert.Equal("review-go-live", result.OverallStatus.NextAction?.Code);
        Assert.Null(result.Stages[4].OperationSummary);
    }

    [Fact]
    public void A_passed_label_without_acceptance_evidence_does_not_enable_finish()
    {
        var result = MigrationWorkspaceStateMapper.Map(Input(
            hasProductionAdoption: true, adoptionStatus: "production-verification-passed",
            productionVerificationStatus: "passed", acceptanceEligible: false));
        Assert.Equal(5, result.CurrentStageIndex);
        Assert.Equal("blocked", result.Stages[5].State);
        Assert.Equal("open-technical-details", result.OverallStatus.NextAction?.Code);
    }

    private static object[] Case(
        MigrationWorkspaceStateInput input,
        int stageIndex,
        string state,
        string? action) =>
        [input, stageIndex, state, action];

    private static MigrationWorkspaceStateInput Input(
        string intakeStatus = "package-validated",
        int blockerCount = 0,
        bool legacyCompatibility = false,
        string? latestConversionStatus = null,
        string? conversionFailureCode = null,
        bool hasVerifiedCandidate = false,
        string? latestStagingStatus = null,
        bool latestStagingRetained = false,
        string? stagingFailureCode = null,
        bool hasActiveProductionAuthority = false,
        bool hasProductionAdoption = false,
        string? adoptionStatus = null,
        string? materializationFailureCode = null,
        string? cutoverPreviewStatus = null,
        string? cutoverStatus = null,
        bool publicRoutesCreated = false,
        bool routeCompensationAttempted = false,
        bool routeCompensationCompleted = false,
        string? cutoverFailureCode = null,
        string? productionVerificationStatus = null,
        string? productionVerificationFailureCode = null,
        string? rollbackStatus = null,
        string? rollbackCompletionStatus = null,
        string? rollbackFailureCode = null,
        bool accepted = false,
        string? baselineStatus = null,
        bool acceptanceEligible = true) =>
        new(
            intakeStatus,
            blockerCount,
            legacyCompatibility,
            latestConversionStatus,
            conversionFailureCode,
            hasVerifiedCandidate,
            latestStagingStatus,
            latestStagingRetained,
            stagingFailureCode,
            hasActiveProductionAuthority,
            hasProductionAdoption,
            adoptionStatus,
            materializationFailureCode,
            cutoverPreviewStatus,
            cutoverStatus,
            publicRoutesCreated,
            routeCompensationAttempted,
            routeCompensationCompleted,
            cutoverFailureCode,
            productionVerificationStatus,
            productionVerificationFailureCode,
            rollbackStatus,
            rollbackCompletionStatus,
            rollbackFailureCode,
            accepted,
            baselineStatus,
            acceptanceEligible);
}
