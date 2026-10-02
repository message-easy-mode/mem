using Mem.Migrate.Core.Conversion;

namespace Mem.Migrate.UnitTests;

public sealed class ConversionOptionsTests
{
    [Fact]
    public void Normalize_requires_archive_and_bounds_batch_size()
    {
        Assert.Throws<ArgumentException>(() => new ConversionOptions().Normalize());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversionOptions
        {
            ArchivePath = "archive.zip",
            BatchSize = 0
        }.Normalize());
    }

    [Fact]
    public void Normalize_preserves_explicit_safe_identifiers_and_paths()
    {
        var options = new ConversionOptions
        {
            ArchivePath = "archive.zip",
            WorkspacePath = "work",
            OutputPath = "out",
            ConversionId = "mm04-proof_01",
            StackId = Guid.Parse("9230081f-ba21-4e53-8fbe-b3cc7bd1b441")
        }.Normalize();

        Assert.Equal("mm04-proof_01", options.ConversionId);
        Assert.True(Path.IsPathFullyQualified(options.ArchivePath));
        Assert.True(Path.IsPathFullyQualified(options.WorkspacePath));
        Assert.True(Path.IsPathFullyQualified(options.OutputPath));
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("../escape")]
    [InlineData("x")]
    public void Normalize_rejects_unsafe_conversion_identifiers(string id)
    {
        Assert.Throws<ArgumentException>(() => new ConversionOptions
        {
            ArchivePath = "archive.zip",
            ConversionId = id
        }.Normalize());
    }
}
