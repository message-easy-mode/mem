using Mem.Migrate.Core.Qualification;

namespace Mem.Migrate.UnitTests;

public sealed class SourceQualificationOptionsTests
{
    [Fact]
    public void Normalize_requires_complete_evidence_chain_and_reviewed_package_hash()
    {
        Assert.Throws<ArgumentException>(() =>
            new SourceQualificationOptions().Normalize());
    }

    [Fact]
    public void Normalize_resolves_paths_and_preserves_explicit_attempt_id()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-source-qualification-options-" + Guid.NewGuid().ToString("N"));
        var options = new SourceQualificationOptions
        {
            CutoverPlanReportPath = Path.Combine(root, "plan.json"),
            FreezeReportPath = Path.Combine(root, "freeze.json"),
            CaptureReportPath = Path.Combine(root, "capture.json"),
            PackageReportPath = Path.Combine(root, "package.json"),
            ExpectedEncryptedPackageSha256 = new string('A', 64),
            OutputPath = Path.Combine(root, "output"),
            QualificationAttemptId = "mm01e-source-proof"
        }.Normalize();

        Assert.Equal("mm01e-source-proof", options.QualificationAttemptId);
        Assert.Equal(new string('a', 64), options.ExpectedEncryptedPackageSha256);
        Assert.True(Path.IsPathFullyQualified(options.PackageReportPath));
        Assert.True(Path.IsPathFullyQualified(options.OutputPath));
    }
}
