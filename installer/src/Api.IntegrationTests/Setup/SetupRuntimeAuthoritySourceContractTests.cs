namespace Api.IntegrationTests.Setup;

public sealed class SetupRuntimeAuthoritySourceContractTests
{
    [Fact]
    public void STARTUP_INSTALL_REL_01C_host_checks_do_not_depend_on_container_local_host_utilities()
    {
        var root = FindRepositoryRoot();
        var checksRoot = Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "HostChecks",
            "Checks");

        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(checksRoot, "*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        foreach (var retiredUtility in new[]
                 {
                     "lsb_release",
                     "free -m",
                     "nproc",
                     "df -Pm",
                     "ss -ltn"
                 })
        {
            Assert.DoesNotContain(retiredUtility, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01C_install_time_port_and_NPM_checks_do_not_use_wrong_namespace_shell_probes()
    {
        var root = FindRepositoryRoot();
        var setupRoot = Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "InstallRuns");

        var dockerHelpers = File.ReadAllText(Path.Combine(
            setupRoot,
            "InstallStepExecutor.DockerHelpers.cs"));
        var npmIngress = File.ReadAllText(Path.Combine(
            setupRoot,
            "InstallStepExecutor.NpmIngress.cs"));
        var validation = File.ReadAllText(Path.Combine(
            setupRoot,
            "InstallStepExecutor.InstallPlanValidation.cs"));

        Assert.DoesNotContain("ss -", dockerHelpers, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", npmIngress, StringComparison.Ordinal);
        Assert.DoesNotContain("curl ", npmIngress, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"version\"", validation, StringComparison.Ordinal);
        Assert.Contains("GetSystemInfoAsync", validation, StringComparison.Ordinal);
        Assert.Contains("CheckRequiredPortsAvailableAsync", validation, StringComparison.Ordinal);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01E_CORR_02_platform_install_mutations_do_not_require_the_Docker_CLI()
    {
        var root = FindRepositoryRoot();
        var setupRoot = Path.Combine(
            root,
            "installer",
            "src",
            "Modules",
            "Modules",
            "Setup",
            "InstallRuns");

        var source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(setupRoot, "InstallStepExecutor*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));

        Assert.DoesNotContain("_commandRunner.RunAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dockerHost.EnsureNetworkAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dockerHost.EnsureVolumeAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dockerHost.CreateContainerAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dockerHost.CopyFileToContainerAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dockerHost.ExecAsync", source, StringComparison.Ordinal);
        Assert.Contains("_dockerHost.InspectByNameAsync", source, StringComparison.Ordinal);
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
