using Mem.Migrate.Core.Security;

namespace Mem.Migrate.ArchiveTests;

public sealed class PrivateFilePermissionsTests
{
    [Fact]
    public void EnsureDirectory_rejects_a_symbolic_link_ancestor()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-private-path-tests",
            Guid.NewGuid().ToString("N"));
        var real = Path.Combine(root, "real");
        var link = Path.Combine(root, "linked");

        try
        {
            Directory.CreateDirectory(real);
            Directory.CreateSymbolicLink(link, real);

            Assert.Throws<IOException>(() =>
                PrivateFilePermissions.EnsureDirectory(
                    Path.Combine(link, "capture")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
