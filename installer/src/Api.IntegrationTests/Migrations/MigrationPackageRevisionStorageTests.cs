using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationPackageRevisionStorageTests
{
    [Fact]
    public void Preview_revision_may_use_the_legacy_archive_during_compatibility_transition()
    {
        using var fixture = new StorageFixture();
        var legacy = MigrationPackageRevisionStorage.ResolveLegacyDecryptedArchivePath(
            fixture.Root,
            "mig_preview");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "preview");

        var resolved = MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
            fixture.Root,
            "mig_preview",
            "mpr_preview",
            allowLegacyPreviewFallback: true);

        Assert.Equal(legacy, resolved);
    }

    [Fact]
    public void Final_revision_never_falls_back_to_the_legacy_preview_archive()
    {
        using var fixture = new StorageFixture();
        var legacy = MigrationPackageRevisionStorage.ResolveLegacyDecryptedArchivePath(
            fixture.Root,
            "mig_final");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "preview");

        var resolved = MigrationPackageRevisionStorage.ResolveExistingDecryptedArchivePath(
            fixture.Root,
            "mig_final",
            "mpr_final",
            allowLegacyPreviewFallback: false);

        Assert.Equal(
            MigrationPackageRevisionStorage.ResolveDecryptedArchivePath(
                fixture.Root,
                "mig_final",
                "mpr_final"),
            resolved);
        Assert.False(File.Exists(resolved));
    }

    private sealed class StorageFixture : IDisposable
    {
        public StorageFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                $"mem-package-revision-storage-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
