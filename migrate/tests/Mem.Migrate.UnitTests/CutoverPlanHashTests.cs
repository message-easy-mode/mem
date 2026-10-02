using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.UnitTests;

public sealed class CutoverPlanHashTests
{
    [Fact]
    public void Hash_is_deterministic_and_changes_with_bound_plan_data()
    {
        var plan = CreatePlan("source-a");

        var first = CutoverPlanHash.Compute(plan);
        var second = CutoverPlanHash.Compute(plan);
        var changed = CutoverPlanHash.Compute(CreatePlan("source-b"));

        Assert.Equal(64, first.Length);
        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }


    [Fact]
    public void Input_binding_changes_for_development_external_control_plane_override()
    {
        var standard = new CutoverPrepareOptions
        {
            StageAttemptId = "mm05d-proof",
            ExpectedSourceFingerprint = new string('a', 64),
            PlanId = "mm06a-proof",
            ProducerVersion = "test"
        }.Normalize();
        var development = standard with
        {
            DevelopmentExternalControlPlane = true
        };

        Assert.NotEqual(
            CutoverPlanHash.ComputeInputBinding(standard),
            CutoverPlanHash.ComputeInputBinding(development));
    }

    private static CutoverPlanDocument CreatePlan(string fingerprint) =>
        new(
            "mem-cutover-plan",
            1,
            "mm06a-proof",
            "test",
            DateTimeOffset.Parse("2026-07-14T00:00:00Z"),
            DateTimeOffset.Parse("2026-07-14T00:30:00Z"),
            "assessment-1",
            fingerprint,
            AssessmentClassification.ConfirmedSupportedV010,
            false,
            false,
            [],
            [],
            [],
            [],
            new CutoverTargetEvidence(
                "stage-1",
                "import-1",
                "target",
                "http://127.0.0.1:7105",
                "bkp_1",
                "restore-1",
                "staging-1",
                DateTimeOffset.Parse("2026-07-13T23:00:00Z"),
                true,
                true,
                true,
                true,
                true),
            [],
            [],
            [],
            [],
            []);
}
