using Mem.Migrate.Cli;

namespace Mem.Migrate.UnitTests;

public sealed class CutoverActivationReadinessCommandLineTests
{
    [Fact]
    public void Parser_accepts_activation_readiness_command()
    {
        var parsed = CommandLineParser.Parse([
            "cutover", "activation-readiness",
            "--final-stage-report", "final.json",
            "--freeze-report", "freeze.json",
            "--profile", "target",
            "--candidate-id", "candidate-1",
            "--preview-id", "preview-1",
            "--confirmation-id", "confirmation-1",
            "--attempt-id", "mm06e-readiness-1"
        ]);

        Assert.Equal("cutover-activation-readiness", parsed.Name);
        Assert.NotNull(parsed.ActivationReadinessOptions);
        Assert.Equal("candidate-1", parsed.ActivationReadinessOptions.CandidateId);
    }
}
