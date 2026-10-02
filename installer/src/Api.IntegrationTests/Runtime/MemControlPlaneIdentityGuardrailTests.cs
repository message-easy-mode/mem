using Shared.ControlPlane;

namespace Api.IntegrationTests.Runtime;

/// <summary>
/// Freezes the remaining reviewed compatibility locations after the canonical
/// Control Plane runtime rename. Later slices should shrink or reclassify these
/// sets rather than introducing new legacy runtime assumptions.
/// </summary>
public sealed class MemControlPlaneIdentityGuardrailTests
{
    [Fact]
    public void Runtime_facing_legacy_container_name_is_confined_to_reviewed_transition_files()
    {
        var actual = FindProductionFilesContaining(MemControlPlaneIdentity.Legacy.ContainerName);

        Assert.Equal(
            new[]
            {
                "bootstrap/lib/installer.sh",
                "installer/src/Api/Runtime/MemControlPlaneSqlitePathResolver.cs",
                "installer/src/Api/appsettings.json",
                "installer/src/Modules/Modules/Auth/Endpoints/InstallerAuthEndpoints.cs",
                "installer/src/Shared/Shared/ControlPlane/MemControlPlaneIdentity.cs",
            },
            actual);
    }

    [Fact]
    public void Legacy_bootstrap_environment_prefix_is_confined_to_reviewed_compatibility_files()
    {
        var actual = FindProductionFilesContainingAny(
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupToken,
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.SetupTokenPath,
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.Channel,
            MemControlPlaneIdentity.Legacy.EnvironmentVariables.PublicPort);

        Assert.Equal(
            new[]
            {
                "bootstrap/lib/installer.sh",
                "installer/src/Api/appsettings.json",
                "installer/src/Shared/Shared/ControlPlane/MemControlPlaneIdentity.cs",
            },
            actual);
    }

    [Fact]
    public void Legacy_runtime_display_names_are_confined_to_reviewed_transition_or_auth_files()
    {
        var actual = FindProductionFilesContainingAny(
            "MEM Installer",
            "Matrix Easy Mode Installer");

        Assert.Empty(actual);
    }

    [Fact]
    public void Former_project_namespace_is_confined_to_reviewed_legacy_or_cryptographic_compatibility_files()
    {
        var actual = FindProductionFilesContaining(
            MemControlPlaneIdentity.Legacy.FormerPublisherNamespace);

        Assert.Equal(
            new[]
            {
                "bootstrap/lib/installer.sh",
                "installer/src/Api/appsettings.json",
                "installer/src/Modules/Modules/Auth/Configuration/MemDataProtectionOptions.cs",
                "installer/src/Shared/Shared/ControlPlane/MemControlPlaneIdentity.cs",
            },
            actual);
    }

    private static IReadOnlyList<string> FindProductionFilesContainingAny(params string[] needles)
    {
        var matches = needles
            .SelectMany(FindProductionFilesContaining)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return matches;
    }

    private static IReadOnlyList<string> FindProductionFilesContaining(string needle)
    {
        var repositoryRoot = FindRepositoryRoot();
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "bootstrap"),
            Path.Combine(repositoryRoot, "installer", "src", "Api"),
            Path.Combine(repositoryRoot, "installer", "src", "HostAgent"),
            Path.Combine(repositoryRoot, "installer", "src", "Infrastructure"),
            Path.Combine(repositoryRoot, "installer", "src", "Modules"),
            Path.Combine(repositoryRoot, "installer", "src", "Shared"),
            Path.Combine(repositoryRoot, "installer", "src", "Web", "src"),
        };

        var matches = new List<string>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!IsReviewedSourceFile(file) || IsExcludedPath(file))
                {
                    continue;
                }

                var source = File.ReadAllText(file);
                if (!source.Contains(needle, StringComparison.Ordinal))
                {
                    continue;
                }

                matches.Add(Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/'));
            }
        }

        return matches
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsReviewedSourceFile(string path) =>
        Path.GetExtension(path) switch
        {
            ".cs" or ".json" or ".resx" or ".sh" or ".ts" or ".tsx" => true,
            _ => false,
        };

    private static bool IsExcludedPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        // Local development overrides are ignored/untracked and are not part of the
        // committed release-source identity contract.
        return fileName.Equals("appsettings.Development.json", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
               fileName.Contains(".spec.", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/__tests__/", StringComparison.Ordinal) ||
               normalized.Contains("/bin/", StringComparison.Ordinal) ||
               normalized.Contains("/obj/", StringComparison.Ordinal) ||
               normalized.Contains("/node_modules/", StringComparison.Ordinal) ||
               normalized.Contains("/dist/", StringComparison.Ordinal) ||
               normalized.Contains("/test-results/", StringComparison.Ordinal) ||
               normalized.Contains("/coverage/", StringComparison.Ordinal) ||
               normalized.Contains("/Api.IntegrationTests/", StringComparison.Ordinal) ||
               normalized.Contains("/HostAgent.Tests/", StringComparison.Ordinal) ||
               normalized.Contains("/Web/tests/", StringComparison.Ordinal) ||
               normalized.Contains("/Web/src/features/operator/docs/release-pack/", StringComparison.Ordinal) ||
               normalized.Contains("/Docs/", StringComparison.Ordinal) ||
               normalized.Contains("/docs/", StringComparison.Ordinal) ||
               normalized.Contains("/Migrations/", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "installer", "src", "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }
}
