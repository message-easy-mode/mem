using HostAgent.Options;
using HostAgent.Planning;
using HostAgent.Runtime.ServiceRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Modules.Shared.Storage;

namespace Api.IntegrationTests.Runtime;

public sealed class ProductionStorageContractTests
{
    [Fact]
    public void Production_data_root_requires_explicit_server_owned_configuration()
    {
        var missing = BuildConfiguration(new Dictionary<string, string?>
        {
            ["MemRuntime:Mode"] = "containerized-production"
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDataRootResolver.Resolve(missing));

        Assert.Contains("MEM_DATA_ROOT must be explicitly configured", exception.Message, StringComparison.Ordinal);

        var configured = BuildConfiguration(new Dictionary<string, string?>
        {
            ["MemRuntime:Mode"] = "containerized-production",
            ["MEM_DATA_ROOT"] = "/srv/mem/mem-data"
        });

        Assert.Equal("/srv/mem/mem-data", MemDataRootResolver.Resolve(configured));
    }

    [Theory]
    [InlineData("relative/mem-data", "absolute server-owned path")]
    [InlineData("/home/operator/mem-data", "login-user home directory")]
    [InlineData("/root/mem-data", "login-user home directory")]
    public void Production_data_root_rejects_unsafe_root_shapes(
        string configuredRoot,
        string expectedMessage)
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["MemRuntime:Mode"] = "containerized-production",
            ["MEM_DATA_ROOT"] = configuredRoot
        });

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MemDataRootResolver.Resolve(configuration));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_production_retains_bounded_user_profile_compatibility_fallback()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["MemRuntime:Mode"] = "automated-test"
        });

        var expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "mem-data"));

        Assert.Equal(expected, MemDataRootResolver.Resolve(configuration));
    }

    [Fact]
    public void Instance_planner_requires_explicit_instance_root_and_uses_it_for_matrix_and_element()
    {
        var missingPlanner = new ChatStackRuntimePlanner(
            Options.Create(new InstanceStorageOptions()),
            Options.Create(new RuntimeNetworkOptions()));

        var missing = Assert.Throws<InvalidOperationException>(() =>
            missingPlanner.Plan(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "demo"));
        Assert.Contains("Provisioning:InstanceDataRoot", missing.Message, StringComparison.Ordinal);

        var root = Path.Combine(Path.GetTempPath(), "mem-production-storage-tests", Guid.NewGuid().ToString("N"));
        var stackId = Guid.NewGuid();
        var matrixId = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        var planner = new ChatStackRuntimePlanner(
            Options.Create(new InstanceStorageOptions { InstanceDataRoot = root }),
            Options.Create(new RuntimeNetworkOptions()));

        var plan = planner.Plan(stackId, matrixId, elementId, "demo");

        Assert.Equal(
            Path.Combine(Path.GetFullPath(root), stackId.ToString("N"), $"matrix-{matrixId:N}"),
            plan.MatrixDataPath);
        Assert.Equal(
            Path.Combine(Path.GetFullPath(root), stackId.ToString("N"), $"element-{elementId:N}"),
            plan.ElementDataPath);
    }

    [Fact]
    public void Production_configuration_contains_no_developer_home_paths_and_uses_server_owned_service_roots()
    {
        var root = FindRepositoryRoot();
        var appsettings = Read(root, "installer/src/Api/appsettings.json");
        // appsettings.Development.json is intentionally ignored local configuration,
        // so production/source contracts must not depend on it being present.
        var instanceOptions = Read(root, "installer/src/HostAgent/Options/InstanceStorageOptions.cs");
        var planner = Read(root, "installer/src/HostAgent/Planning/ChatStackRuntimePlanner.cs");

        Assert.DoesNotContain("/home/master", appsettings, StringComparison.Ordinal);
        Assert.DoesNotContain("/home/master", instanceOptions, StringComparison.Ordinal);
        Assert.DoesNotContain("/opt/mem/instances", planner, StringComparison.Ordinal);

        Assert.Contains(
            "\"StorageRootPath\": \"/var/lib/message-easy-mode/platform/coturn\"",
            appsettings,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"HostDataPath\": \"/var/lib/message-easy-mode/seq\"",
            appsettings,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Containerized_host_data_parity_is_registered_as_a_startup_gate()
    {
        var root = FindRepositoryRoot();
        var registration = Read(
            root,
            "installer/src/HostAgent/DependencyInjection/HostAgentServiceCollectionExtensions.cs");

        Assert.Contains(
            "services.AddScoped<HostDataPathParityValidator>();",
            registration,
            StringComparison.Ordinal);
        Assert.Contains(
            "services.AddHostedService<HostDataPathParityStartupValidator>();",
            registration,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Distributed_runtime_services_no_longer_own_user_profile_mem_data_fallbacks()
    {
        var root = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            Path.Combine(root, "installer", "src", "HostAgent"),
            Path.Combine(root, "installer", "src", "Modules", "Modules")
        };

        var offending = sourceRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(
                Path.Combine("Shared", "Storage", "MemDataRootResolver.cs"),
                StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(
                "SpecialFolder.UserProfile",
                StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(root, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(offending);
    }

    private static IConfiguration BuildConfiguration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath));

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
