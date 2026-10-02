using Mem.Migrate.Core.Target;

namespace Mem.Migrate.UnitTests;

public sealed class ActivationReadinessOptionsTests
{
    [Fact]
    public void Normalize_requires_all_durable_target_ids()
    {
        var options = new ActivationReadinessOptions
        {
            FinalTargetStageReportPath = "final.json",
            FreezeReportPath = "freeze.json",
            ProfileName = "target",
            CandidateId = "candidate-1",
            PreviewId = "preview-1",
            ConfirmationId = "confirmation-1",
            AttemptId = "mm06e-readiness-1"
        }.Normalize();

        Assert.Equal("target", options.ProfileName);
        Assert.Equal("candidate-1", options.CandidateId);
        Assert.True(Path.IsPathFullyQualified(options.FinalTargetStageReportPath));
    }

    [Fact]
    public void Normalize_rejects_missing_confirmation()
    {
        var options = new ActivationReadinessOptions
        {
            FinalTargetStageReportPath = "final.json",
            FreezeReportPath = "freeze.json",
            ProfileName = "target",
            CandidateId = "candidate-1",
            PreviewId = "preview-1",
            AttemptId = "mm06e-readiness-1"
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }
}
