using Modules.Integrations.Npm.Services;

namespace Api.IntegrationTests.Setup;

public sealed class NpmRuntimeReleaseTests
{
    [Fact]
    public void STARTUP_NPM_BOOTSTRAP_01E_uses_the_MEM_approved_NPM_2_15_1_release()
    {
        Assert.Equal("2.15.1", NpmRuntimeRelease.ApprovedVersion);
        Assert.Equal(
            "jc21/nginx-proxy-manager:2.15.1",
            NpmRuntimeRelease.ApprovedImage);

        Assert.True(NpmRuntimeRelease.IsApprovedImage(
            "jc21/nginx-proxy-manager:2.15.1"));
        Assert.True(NpmRuntimeRelease.IsApprovedImage(
            "docker.io/jc21/nginx-proxy-manager:2.15.1"));
        Assert.False(NpmRuntimeRelease.IsApprovedImage(
            "jc21/nginx-proxy-manager:2.14.0"));
        Assert.False(NpmRuntimeRelease.IsApprovedImage(
            "jc21/nginx-proxy-manager:latest"));
    }

    [Fact]
    public void STARTUP_NPM_BOOTSTRAP_01E_production_NPM_creation_paths_do_not_use_the_retired_or_floating_pin()
    {
        var root = FindRepositoryRoot();
        var modulesRoot = Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules");

        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(modulesRoot, "*.cs", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.DoesNotContain(
            "jc21/nginx-proxy-manager:2.14.0",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "jc21/nginx-proxy-manager:latest",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "NpmRuntimeRelease.ApprovedImage",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "installer",
                    "src",
                    "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }
}
