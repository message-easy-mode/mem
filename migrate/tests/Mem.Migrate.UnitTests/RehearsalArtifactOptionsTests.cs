using Mem.Migrate.Core.Rehearsal;

namespace Mem.Migrate.UnitTests;

public sealed class RehearsalArtifactOptionsTests
{
    [Fact]
    public void Normalize_resolves_paths_and_keeps_explicit_artifact_id()
    {
        var options = new RehearsalArtifactOptions
        {
            ArchivePath = "archive.zip",
            ConversionReportPath = "conversion-report.json",
            OutputPath = "output",
            ArtifactId = "mm05a-proof"
        }.Normalize();

        Assert.Equal("mm05a-proof", options.ArtifactId);
        Assert.True(Path.IsPathFullyQualified(options.ArchivePath));
        Assert.True(Path.IsPathFullyQualified(options.ConversionReportPath));
        Assert.True(Path.IsPathFullyQualified(options.OutputPath));
        Assert.Equal("docker", options.DockerCommand);
        Assert.Equal(300, options.CommandTimeoutSeconds);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("bad id")]
    [InlineData("bad/path")]
    public void Normalize_rejects_unsafe_artifact_ids(string artifactId)
    {
        var options = new RehearsalArtifactOptions
        {
            ArchivePath = "archive.zip",
            ConversionReportPath = "conversion-report.json",
            ArtifactId = artifactId
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }

    [Fact]
    public void Normalize_rejects_non_positive_command_timeout()
    {
        var options = new RehearsalArtifactOptions
        {
            ArchivePath = "archive.zip",
            ConversionReportPath = "conversion-report.json",
            CommandTimeoutSeconds = 0
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Normalize());
    }
}
