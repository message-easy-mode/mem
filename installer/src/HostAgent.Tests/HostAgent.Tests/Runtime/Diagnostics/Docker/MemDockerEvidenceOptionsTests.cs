using HostAgent.Runtime.Diagnostics.Docker;

namespace HostAgent.Tests.Runtime.Diagnostics.Docker;

public sealed class MemDockerEvidenceOptionsTests
{
    [Fact]
    public void Default_options_are_valid()
    {
        MemDockerEvidenceOptionsValidator.ThrowIfInvalid(new MemDockerEvidenceOptions());
    }

    [Fact]
    public void Rejects_a_frame_limit_larger_than_the_total_raw_limit()
    {
        var options = new MemDockerEvidenceOptions
        {
            MaximumRawBytes = 4096,
            MaximumFrameBytes = 8192
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDockerEvidenceOptionsValidator.ThrowIfInvalid(options));

        Assert.Contains("MaximumFrameBytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unbounded_tail_and_timeout_values()
    {
        var options = new MemDockerEvidenceOptions
        {
            DefaultTailLines = 0,
            TimeoutSeconds = 0
        };

        Assert.Throws<InvalidOperationException>(() =>
            MemDockerEvidenceOptionsValidator.ThrowIfInvalid(options));
    }
}
