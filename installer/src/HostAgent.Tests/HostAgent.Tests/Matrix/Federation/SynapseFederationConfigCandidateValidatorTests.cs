using HostAgent.Matrix.Federation;

namespace HostAgent.Tests.Matrix.Federation;

public sealed class SynapseFederationConfigCandidateValidatorTests
{
    [Fact]
    public void Sanitises_secret_bearing_validation_lines_and_bounds_the_tail()
    {
        var logs = new string('x', 2500) +
            "\nnormal validation line\nregistration_shared_secret: do-not-leak\n";

        var result = SynapseFederationConfigCandidateValidator.SanitiseLogTail(logs);

        Assert.Contains("normal validation line", result, StringComparison.Ordinal);
        Assert.DoesNotContain("do-not-leak", result, StringComparison.Ordinal);
        Assert.Contains("[redacted sensitive Synapse config validation line]", result, StringComparison.Ordinal);
        Assert.True(result.Length <= 2000);
    }
}
