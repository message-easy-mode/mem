using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.ArchiveTests;

public sealed class ArchivePathPolicyTests
{
    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/drive")]
    [InlineData("mem-migration/../escape")]
    [InlineData("mem-migration//empty")]
    [InlineData("mem-migration\\windows")]
    [InlineData("other-root/file")]
    public void Normalize_rejects_unsafe_or_noncanonical_paths(string path)
    {
        Assert.Throws<InvalidDataException>(() =>
            ArchivePathPolicy.Normalize(path));
    }

    [Fact]
    public void EnsureUnique_rejects_case_collisions()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            ArchivePathPolicy.EnsureUnique(
            [
                "mem-migration/stacks/a/file",
                "mem-migration/stacks/A/file"
            ]));

        Assert.Contains("collision", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_accepts_a_canonical_migration_path()
    {
        const string path = "mem-migration/stacks/abc/synapse/homeserver.db";
        Assert.Equal(path, ArchivePathPolicy.Normalize(path));
    }
}
