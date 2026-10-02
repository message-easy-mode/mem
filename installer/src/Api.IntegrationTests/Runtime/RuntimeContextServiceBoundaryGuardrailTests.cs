using Core.RuntimeDefinition;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeContextServiceBoundaryGuardrailTests
{
    [Fact]
    public void Managed_resource_labels_include_canonical_owner_and_legacy_compatibility()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-labels-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var context = TestRuntimeContext.Create(root);
            var labels = ManagedContainerLabels.ForService(
                ManagedServiceNames.Seq,
                context);

            Assert.Equal("true", labels[ManagedContainerLabels.CanonicalManagedKey]);
            Assert.Equal("seq", labels[ManagedContainerLabels.CanonicalServiceKey]);
            Assert.Equal(
                context.ControlPlaneInstanceId.ToString("D"),
                labels[ManagedContainerLabels.ControlPlaneInstanceKey]);
            Assert.Equal(context.RuntimeMode, labels[ManagedContainerLabels.RuntimeModeKey]);
            Assert.Equal("true", labels[ManagedContainerLabels.ManagedKey]);
            Assert.Equal("seq", labels[ManagedContainerLabels.ServiceKey]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Feature_implementations_do_not_invent_localhost_or_restart_commands()
    {
        Assert.Empty(FindFeatureFilesContaining("127.0.0.1"));
        Assert.Empty(FindFeatureFilesContaining("http://localhost:"));
        Assert.Empty(FindFeatureFilesContaining("sudo docker restart"));
    }

    [Fact]
    public void Seq_feature_implementations_do_not_hard_code_the_Docker_service_authority()
    {
        Assert.Empty(FindFeatureFilesContaining("http://seq:"));
    }

    private static IReadOnlyList<string> FindFeatureFilesContaining(string needle)
    {
        var repositoryRoot = FindRepositoryRoot();
        var roots = new[]
        {
            Path.Combine(repositoryRoot, "installer", "src", "Modules", "Modules", "Integrations"),
            Path.Combine(repositoryRoot, "installer", "src", "Modules", "Modules", "Operator", "Diagnostics"),
            Path.Combine(repositoryRoot, "installer", "src", "Web", "src", "features", "operator", "diagnostics"),
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(IsImplementationFile)
            .Where(path => !IsExcluded(path))
            .Where(path => ContainsUnapprovedNeedle(repositoryRoot, path, needle))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsImplementationFile(string path) =>
        Path.GetExtension(path) is ".cs" or ".ts" or ".tsx";

    private static bool ContainsUnapprovedNeedle(
        string repositoryRoot,
        string path,
        string needle)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/');
        var source = File.ReadAllText(path);

        // This exact URL belongs to a Node readiness script executed through
        // docker exec *inside the managed NPM container itself*. It is a reviewed
        // container-internal self-probe, not a Control Plane -> NPM service route,
        // so IMemManagedServiceAuthorityResolver is not the relevant authority.
        // Remove only this exact reviewed literal from the guardrail input. Any
        // additional loopback literal in this file (or any other feature file)
        // remains visible to the runtime-context guardrail.
        if (string.Equals(needle, "127.0.0.1", StringComparison.Ordinal) &&
            string.Equals(
                relativePath,
                "installer/src/Modules/Modules/Integrations/Npm/Services/NpmCertificateLogSecretBoundaryService.cs",
                StringComparison.Ordinal))
        {
            source = source.Replace(
                "http://127.0.0.1:81/api",
                "<approved-npm-container-self-probe>",
                StringComparison.Ordinal);
        }

        return source.Contains(needle, StringComparison.Ordinal);
    }

    private static bool IsExcluded(string path)
    {
        var normalized = path.Replace('\\', '/');
        var name = Path.GetFileName(normalized);
        return name.Contains(".test.", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Options", StringComparison.Ordinal) ||
               name.Contains("Contracts", StringComparison.Ordinal) ||
               normalized.EndsWith("/diagnostics.types.ts", StringComparison.Ordinal) ||
               normalized.Contains("/README", StringComparison.OrdinalIgnoreCase);
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
