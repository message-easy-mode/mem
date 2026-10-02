using Mem.Migrate.Cli;

namespace Mem.Migrate.UnitTests;

public sealed class SourceQualificationCommandLineTests
{
    [Fact]
    public void Parser_accepts_two_server_source_evidence_command()
    {
        var parsed = CommandLineParser.Parse([
            "qualification", "source-evidence",
            "--cutover-plan-report", "plan.json",
            "--freeze-report", "freeze.json",
            "--capture-report", "capture.json",
            "--package-report", "package.json",
            "--expected-package-sha256", new string('a', 64),
            "--output", "output",
            "--qualification-attempt-id", "mm01e-source-proof",
            "--command-timeout-seconds", "45",
            "--json"
        ]);

        Assert.Equal("qualification-source-evidence", parsed.Name);
        Assert.False(parsed.ShowHelp);
        var options = Assert.IsType<Mem.Migrate.Core.Qualification.SourceQualificationOptions>(
            parsed.SourceQualificationOptions);
        Assert.Equal("plan.json", options.CutoverPlanReportPath);
        Assert.Equal("mm01e-source-proof", options.QualificationAttemptId);
        Assert.Equal(45, options.CommandTimeoutSeconds);
        Assert.True(options.JsonConsoleOutput);
    }
}
