using Mem.Migrate.Core.Conversion;

namespace Mem.Migrate.UnitTests;

public sealed class CandidateOptionsTests
{
    [Fact]
    public void Normalize_requires_archive_and_conversion_report()
    {
        Assert.Throws<ArgumentException>(() => new CandidateOptions().Normalize());
    }

    [Fact]
    public void Normalize_rejects_unsafe_candidate_id()
    {
        var options = new CandidateOptions
        {
            ArchivePath = "archive.zip",
            ConversionReportPath = "conversion-report.json",
            CandidateId = "../escape"
        };

        Assert.Throws<ArgumentException>(() => options.Normalize());
    }

    [Fact]
    public void Normalize_canonicalises_paths_and_preserves_exact_images()
    {
        var options = new CandidateOptions
        {
            ArchivePath = "archive.zip",
            ConversionReportPath = "conversion-report.json",
            WorkspacePath = "work",
            OutputPath = "output",
            CandidateId = "mm04b-proof",
            SynapseImage = " matrixdotorg/synapse@sha256:test ",
            PostgresImage = " postgres@sha256:test "
        }.Normalize();

        Assert.True(Path.IsPathFullyQualified(options.ArchivePath));
        Assert.True(Path.IsPathFullyQualified(options.ConversionReportPath));
        Assert.Equal("mm04b-proof", options.CandidateId);
        Assert.Equal("matrixdotorg/synapse@sha256:test", options.SynapseImage);
        Assert.Equal("postgres@sha256:test", options.PostgresImage);
    }
}
