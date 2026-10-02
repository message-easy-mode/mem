using HostAgent.Runtime.Migrations.ProductionAdoption;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionVerificationTests
{
    [Theory]
    [InlineData("public-awaiting-verification")]
    [InlineData("production-verification-running")]
    [InlineData("production-verification-passed")]
    [InlineData("production-verification-failed")]
    public void Verification_allows_unaccepted_public_adoption_states(string status)
    {
        var plan = CreatePlan(status);

        var blockers = MigrationProductionVerificationService.BuildEligibilityBlockers(
            plan,
            accepted: false,
            staleReason: null);

        Assert.Empty(blockers);
    }

    [Fact]
    public void Verification_rejects_accepted_stale_or_rolled_back_adoption()
    {
        var plan = CreatePlan("production-verification-passed");
        plan.RollbackExecutionId = "mpre_test";
        plan.RollbackStatus = "target-rolled-back-awaiting-source";

        var blockers = MigrationProductionVerificationService.BuildEligibilityBlockers(
            plan,
            accepted: true,
            staleReason: "The bound final package is stale.");

        Assert.Contains(blockers, value => value.Contains("after acceptance", StringComparison.Ordinal));
        Assert.Contains("The bound final package is stale.", blockers);
        Assert.Contains(blockers, value => value.Contains("rollback lifecycle", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4)]
    [InlineData(30)]
    [InlineData(121)]
    public void Legacy_freshness_request_is_retained_only_for_wire_compatibility(int? requested)
    {
        Assert.Equal(
            requested,
            MigrationProductionVerificationService.NormalizeRetainedFreshnessRequest(requested));
    }

    [Fact]
    public void Database_count_check_requires_exact_staging_parity_when_expected_is_known()
    {
        var passed = MigrationProductionVerificationService.BuildCountCheck("users", 12, 12);
        var failed = MigrationProductionVerificationService.BuildCountCheck("users", 12, 11);

        Assert.True(passed.Success);
        Assert.False(failed.Success);
        Assert.Equal("migration-production.database.users-count", failed.Code);
    }

    [Fact]
    public void Evidence_hash_is_deterministic_and_content_bound()
    {
        const string first = "{\"verificationId\":\"mpvf_1\",\"passed\":true}";
        const string changed = "{\"verificationId\":\"mpvf_1\",\"passed\":false}";

        Assert.Equal(
            MigrationProductionVerificationService.ComputeEvidenceSha256(first),
            MigrationProductionVerificationService.ComputeEvidenceSha256(first));
        Assert.NotEqual(
            MigrationProductionVerificationService.ComputeEvidenceSha256(first),
            MigrationProductionVerificationService.ComputeEvidenceSha256(changed));
    }

    private static MigrationProductionAdoptionEntity CreatePlan(string status) =>
        new()
        {
            Status = status,
            CutoverStatus = "public-awaiting-verification",
            PublicRoutesCreated = true,
            RuntimePromotionCompleted = true,
            CutoverExecutionId = "mpce_test",
            TargetPublicAtUtc = DateTime.UtcNow,
        };
}
