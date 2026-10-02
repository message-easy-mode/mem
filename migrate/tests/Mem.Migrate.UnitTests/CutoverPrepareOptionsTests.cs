using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.UnitTests;

public sealed class CutoverPrepareOptionsTests
{
    [Fact]
    public void Normalize_requires_stage_and_reviewed_fingerprint()
    {
        var options = new CutoverPrepareOptions();

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }

    [Fact]
    public void Normalize_creates_isolated_fresh_assessment_paths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-mm06a-options-" + Guid.NewGuid().ToString("N"));
        var options = new CutoverPrepareOptions
        {
            Assessment = new AssessmentOptions
            {
                ApiUrl = "http://localhost:7000",
                PostgresContainer = "mem-v010-postgres"
            },
            WorkspacePath = Path.Combine(root, "work"),
            TargetWorkspacePath = Path.Combine(root, "target-work"),
            OutputPath = Path.Combine(root, "output"),
            StageAttemptId = "mm05d-proof",
            ExpectedSourceFingerprint = new string('a', 64),
            PlanId = "mm06a-proof",
            ProducerVersion = "test",
            DevelopmentExternalControlPlane = true
        }.Normalize();

        Assert.Equal("mm06a-proof", options.PlanId);
        Assert.Equal(new string('a', 64), options.ExpectedSourceFingerprint);
        Assert.True(options.DevelopmentExternalControlPlane);
        Assert.EndsWith(
            Path.Combine("cutover-plans", "mm06a-proof", "source-assessment-work"),
            options.Assessment.WorkspacePath,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("mm06a-proof", "source-assessment"),
            options.Assessment.OutputPath,
            StringComparison.Ordinal);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "target-work")),
            options.TargetWorkspacePath);
    }

    [Fact]
    public void Resume_requires_explicit_plan_id()
    {
        var options = new CutoverPrepareOptions
        {
            StageAttemptId = "mm05d-proof",
            ExpectedSourceFingerprint = new string('b', 64),
            ProducerVersion = "test",
            Resume = true
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }
}
