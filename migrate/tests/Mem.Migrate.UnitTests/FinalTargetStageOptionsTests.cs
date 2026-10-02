using Mem.Migrate.Core.Target;

namespace Mem.Migrate.UnitTests;

public sealed class FinalTargetStageOptionsTests
{
    [Fact]
    public void Normalize_requires_final_archive_freeze_report_and_profile()
    {
        Assert.Throws<ArgumentException>(() => new FinalTargetStageOptions().Normalize());
    }

    [Fact]
    public void Normalize_creates_absolute_paths_and_preserves_retained_candidate_defaults()
    {
        var options = new FinalTargetStageOptions
        {
            ArchivePath = "final.memmigration.zip",
            FreezeReportPath = "freeze.json",
            ProfileName = "mm05c-target",
            WorkspacePath = ".",
            OutputPath = ".",
            AttemptId = "mm06d-final-live"
        }.Normalize();

        Assert.True(Path.IsPathFullyQualified(options.ArchivePath));
        Assert.True(Path.IsPathFullyQualified(options.FreezeReportPath));
        Assert.True(Path.IsPathFullyQualified(options.WorkspacePath));
        Assert.True(Path.IsPathFullyQualified(options.OutputPath));
        Assert.Equal("mm05c-target", options.ProfileName);
        Assert.Equal("mm06d-final-live", options.AttemptId);
        Assert.False(options.Resume);
    }

    [Fact]
    public void Normalize_requires_explicit_attempt_for_resume()
    {
        var options = new FinalTargetStageOptions
        {
            ArchivePath = "final.memmigration.zip",
            FreezeReportPath = "freeze.json",
            ProfileName = "mm05c-target",
            Resume = true
        };

        var error = Assert.Throws<ArgumentException>(() => options.Normalize());
        Assert.Contains("explicit --attempt-id", error.Message, StringComparison.Ordinal);
    }
}
