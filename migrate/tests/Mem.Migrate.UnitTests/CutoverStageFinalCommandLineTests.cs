using Mem.Migrate.Cli;

namespace Mem.Migrate.UnitTests;

public sealed class CutoverStageFinalCommandLineTests
{
    [Fact]
    public void Parse_recognizes_stage_final_and_preserves_all_required_options()
    {
        var command = CommandLineParser.Parse(
        [
            "cutover",
            "stage-final",
            "--archive", "final.memmigration.zip",
            "--freeze-report", "freeze.json",
            "--profile", "mm05c-target",
            "--workspace", "work",
            "--output", "output",
            "--attempt-id", "mm06d-final-live",
            "--synapse-image", "sha256:synapse",
            "--postgres-image", "postgres@sha256:postgres",
            "--command-timeout-seconds", "900",
            "--readiness-timeout-seconds", "180",
            "--http-timeout-seconds", "900",
            "--resume"
        ]);

        Assert.Equal("cutover-stage-final", command.Name);
        Assert.False(command.ShowHelp);

        var options = Assert.IsType<Mem.Migrate.Core.Target.FinalTargetStageOptions>(
            command.FinalTargetStageOptions);
        Assert.Equal("final.memmigration.zip", options.ArchivePath);
        Assert.Equal("freeze.json", options.FreezeReportPath);
        Assert.Equal("mm05c-target", options.ProfileName);
        Assert.Equal("mm06d-final-live", options.AttemptId);
        Assert.Equal("sha256:synapse", options.SynapseImage);
        Assert.Equal("postgres@sha256:postgres", options.PostgresImage);
        Assert.Equal(900, options.CommandTimeoutSeconds);
        Assert.Equal(180, options.ReadinessTimeoutSeconds);
        Assert.Equal(900, options.HttpTimeoutSeconds);
        Assert.True(options.Resume);
    }

    [Fact]
    public void Parse_preserves_development_external_control_plane_on_prepare()
    {
        var command = CommandLineParser.Parse(
        [
            "cutover",
            "prepare",
            "--workspace", "work",
            "--target-workspace", "target-work",
            "--output", "output",
            "--stage-attempt-id", "mm05d-stage",
            "--expected-source-fingerprint", new string('a', 64),
            "--plan-id", "mm06a-plan",
            "--development-external-control-plane"
        ]);

        var options = Assert.IsType<Mem.Migrate.Core.Cutover.CutoverPrepareOptions>(
            command.CutoverPrepareOptions);
        Assert.True(options.DevelopmentExternalControlPlane);
    }
}
