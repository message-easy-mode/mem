using HostAgent.Runtime.Migrations.ProductionAdoption;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationGoLiveTests
{
    [Fact]
    public void Every_user_level_go_live_confirmation_is_required()
    {
        var missing = MigrationGoLiveService.GetMissingConfirmations(
            new MakeMigrationServerLiveRequest(
                ConfirmMovePublicTraffic: false,
                ConfirmStopUsingOldServer: false,
                ConfirmRunLiveVerification: false));

        Assert.Equal(
        [
            "confirmMovePublicTraffic",
            "confirmStopUsingOldServer",
            "confirmRunLiveVerification",
        ], missing);
    }

    [Theory]
    [InlineData("private-runtime-ready", "not-started", false, false, false, false, "not-started", false, true, "Cutover")]
    [InlineData("cutover-failed", "failed", false, false, true, true, "not-started", false, true, "Cutover")]
    [InlineData("cutover-failed", "failed", true, false, true, false, "not-started", false, true, "RecoveryRequired")]
    [InlineData("public-awaiting-verification", "public-awaiting-verification", true, true, false, false, "not-started", false, true, "Verify")]
    [InlineData("production-verification-running", "public-awaiting-verification", true, true, false, false, "running", false, true, "AlreadyRunning")]
    [InlineData("production-verification-passed", "public-awaiting-verification", true, true, false, false, "passed", true, true, "Completed")]
    public void Durable_state_maps_to_one_safe_go_live_action(
        string status,
        string cutoverStatus,
        bool publicRoutesCreated,
        bool runtimePromotionCompleted,
        bool routeCompensationAttempted,
        bool routeCompensationCompleted,
        string verificationStatus,
        bool verificationPassed,
        bool privateRuntimeReady,
        string expected)
    {
        var actual = MigrationGoLiveService.DetermineAction(
            status,
            cutoverStatus,
            publicRoutesCreated,
            runtimePromotionCompleted,
            routeCompensationAttempted,
            routeCompensationCompleted,
            verificationStatus,
            verificationPassed,
            privateRuntimeReady);

        Assert.Equal(expected, actual.ToString());
    }

    [Fact]
    public void Cancelled_cutover_reports_verified_compensation_truthfully()
    {
        var compensated = MigrationProductionCutoverService.ClassifyFailure(
            new OperationCanceledException(),
            mutationStarted: true,
            compensationCompleted: true);
        var unresolved = MigrationProductionCutoverService.ClassifyFailure(
            new OperationCanceledException(),
            mutationStarted: true,
            compensationCompleted: false);

        Assert.Equal("migration_production_cutover_cancelled", compensated.Code);
        Assert.Contains("restored the previous public route state", compensated.Summary, StringComparison.Ordinal);
        Assert.Contains("could not verify restoration", unresolved.Summary, StringComparison.Ordinal);
    }
}
