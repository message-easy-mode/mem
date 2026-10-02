using Mem.Migrate.Core.Cutover;

namespace Mem.Migrate.UnitTests;

public sealed class SourceRestorationOptionsTests
{
    [Fact]
    public void Normalize_requires_freeze_handoff_and_reviewed_hash()
    {
        Assert.Throws<ArgumentException>(() =>
            new SourceRestorationOptions().Normalize());
    }

    [Fact]
    public void Normalize_creates_private_attempt_paths()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-source-restoration-options-" + Guid.NewGuid().ToString("N"));
        var options = new SourceRestorationOptions
        {
            WorkspacePath = Path.Combine(root, "work"),
            OutputPath = Path.Combine(root, "output"),
            FreezeReportPath = Path.Combine(root, "freeze.json"),
            SourceHandoffPath = Path.Combine(root, "handoff.json"),
            ExpectedHandoffSha256 = new string('a', 64),
            RestorationAttemptId = "mm01cb-proof"
        }.Normalize();

        Assert.Equal("mm01cb-proof", options.RestorationAttemptId);
        Assert.Equal(new string('a', 64), options.ExpectedHandoffSha256);
        Assert.EndsWith(
            Path.Combine(
                "source-restorations",
                "mm01cb-proof",
                "source-verification-work"),
            options.Assessment.WorkspacePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Resume_requires_explicit_attempt_id()
    {
        var options = new SourceRestorationOptions
        {
            FreezeReportPath = "freeze.json",
            SourceHandoffPath = "handoff.json",
            ExpectedHandoffSha256 = new string('b', 64),
            Resume = true
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }
}
