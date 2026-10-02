using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.UnitTests;

public sealed class CutoverFreezeOptionsTests
{
    [Fact]
    public void Normalize_requires_plan_and_reviewed_hash()
    {
        Assert.Throws<ArgumentException>(() => new CutoverFreezeOptions().Normalize());
    }

    [Fact]
    public void Normalize_creates_isolated_assessment_paths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-mm06b-options-" + Guid.NewGuid().ToString("N"));
        var options = new CutoverFreezeOptions
        {
            Assessment = new AssessmentOptions
            {
                ApiUrl = "http://localhost:7000",
                PostgresContainer = "mem-v010-postgres"
            },
            WorkspacePath = Path.Combine(root, "work"),
            OutputPath = Path.Combine(root, "output"),
            PlanPath = Path.Combine(root, "plan.json"),
            ExpectedPlanHash = new string('a', 64),
            FreezeAttemptId = "mm06b-proof"
        }.Normalize();

        Assert.Equal("mm06b-proof", options.FreezeAttemptId);
        Assert.Equal(new string('a', 64), options.ExpectedPlanHash);
        Assert.EndsWith(
            Path.Combine("cutover-freezes", "mm06b-proof", "source-assessment-work"),
            options.Assessment.WorkspacePath,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("mm06b-proof", "source-assessment"),
            options.Assessment.OutputPath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_requires_explicit_attempt_id()
    {
        var options = new CutoverFreezeOptions
        {
            PlanPath = "plan.json",
            ExpectedPlanHash = new string('b', 64),
            Resume = true
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }
}
