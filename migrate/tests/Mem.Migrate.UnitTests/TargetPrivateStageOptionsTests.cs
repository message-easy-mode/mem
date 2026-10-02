using Mem.Migrate.Core.Target;

namespace Mem.Migrate.UnitTests;

public sealed class TargetPrivateStageOptionsTests
{
    [Fact]
    public void Normalize_requires_completed_import_attempt_and_profile()
    {
        var options = new TargetPrivateStageOptions();
        Assert.Throws<ArgumentException>(() => options.Normalize());
    }

    [Fact]
    public void Normalize_preserves_private_cleanup_choice()
    {
        var options = new TargetPrivateStageOptions
        {
            ImportAttemptId = "mm05c-live",
            ProfileName = "mm05c-target",
            WorkspacePath = ".",
            OutputPath = ".",
            StageAttemptId = "mm05d-live",
            DestroyAfterVerification = true
        }.Normalize();

        Assert.Equal("mm05c-live", options.ImportAttemptId);
        Assert.Equal("mm05c-target", options.ProfileName);
        Assert.Equal("mm05d-live", options.StageAttemptId);
        Assert.True(options.DestroyAfterVerification);
        Assert.True(Path.IsPathFullyQualified(options.WorkspacePath));
        Assert.True(Path.IsPathFullyQualified(options.OutputPath));
    }
}
