using Mem.Migrate.Cli;

namespace Mem.Migrate.UnitTests;

public sealed class CutoverSourceRestorationCommandLineTests
{
    [Fact]
    public void Parser_accepts_source_restoration_command()
    {
        var parsed = CommandLineParser.Parse([
            "cutover", "rollback-source",
            "--freeze-report", "freeze.json",
            "--source-handoff", "handoff.json",
            "--expected-handoff-sha256", new string('a', 64),
            "--workspace", "work",
            "--output", "output",
            "--restoration-attempt-id", "mm01cb-proof",
            "--readiness-timeout-seconds", "240",
            "--development-external-control-plane-ready",
            "--resume"
        ]);

        Assert.Equal("cutover-rollback-source", parsed.Name);
        Assert.False(parsed.ShowHelp);
        var options = Assert.IsType<Mem.Migrate.Core.Cutover.SourceRestorationOptions>(
            parsed.SourceRestorationOptions);
        Assert.Equal("freeze.json", options.FreezeReportPath);
        Assert.Equal("handoff.json", options.SourceHandoffPath);
        Assert.Equal("mm01cb-proof", options.RestorationAttemptId);
        Assert.Equal(240, options.ReadinessTimeoutSeconds);
        Assert.True(options.DevelopmentExternalControlPlaneReady);
        Assert.True(options.Resume);
    }
}
