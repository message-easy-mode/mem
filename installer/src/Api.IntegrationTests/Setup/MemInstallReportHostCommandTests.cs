using Api.Support;

namespace Api.IntegrationTests.Setup;

public sealed class MemInstallReportHostCommandTests
{
    [Fact]
    public void STARTUP_INSTALL_REL_01F_host_command_defaults_to_latest_json_with_docker_evidence()
    {
        Assert.True(MemInstallReportHostCommand.TryParse(
            ["support", "install-report"],
            out var parsed,
            out var error));

        Assert.Null(error);
        Assert.True(parsed.UseLatest);
        Assert.Null(parsed.InstallationId);
        Assert.Null(parsed.TraceId);
        Assert.Equal("json", parsed.Format);
        Assert.True(parsed.IncludeDockerEvidence);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01F_host_command_accepts_known_installation_and_text_without_docker_evidence()
    {
        var installationId = Guid.NewGuid();
        Assert.True(MemInstallReportHostCommand.TryParse(
            [
                "support",
                "install-report",
                "--installation-id",
                installationId.ToString(),
                "--format",
                "text",
                "--no-docker-evidence"
            ],
            out var parsed,
            out var error));

        Assert.Null(error);
        Assert.Equal(installationId, parsed.InstallationId);
        Assert.False(parsed.UseLatest);
        Assert.Equal("text", parsed.Format);
        Assert.False(parsed.IncludeDockerEvidence);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01F_host_command_rejects_conflicting_selectors()
    {
        Assert.False(MemInstallReportHostCommand.TryParse(
            ["support", "install-report", "--latest", "--trace-id", "trace-123"],
            out _,
            out var error));

        Assert.Contains("only one", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void STARTUP_INSTALL_REL_01F_host_command_registers_command_scoped_data_protection_before_diagnostics()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "installer",
            "src",
            "Api",
            "Support",
            "MemInstallReportHostCommand.cs");
        var source = File.ReadAllText(sourcePath);

        var dataProtectionIndex = source.IndexOf(
            "new EphemeralDataProtectionProvider()",
            StringComparison.Ordinal);
        var hostAgentIndex = source.IndexOf(
            "builder.Services.AddHostAgent(builder.Configuration)",
            StringComparison.Ordinal);

        Assert.True(dataProtectionIndex >= 0);
        Assert.True(hostAgentIndex >= 0);
        Assert.True(
            dataProtectionIndex < hostAgentIndex,
            "The host report command must register IDataProtectionProvider before Diagnostics/HostAgent services are resolved.");
    }

    [Fact]
    public void DEF_013_host_command_resolves_the_active_api_process_before_registering_runtime_context()
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourcePath = Path.Combine(
            repositoryRoot,
            "installer",
            "src",
            "Api",
            "Support",
            "MemInstallReportHostCommand.cs");
        var source = File.ReadAllText(sourcePath);

        var bootstrapIndex = source.IndexOf(
            "MemRuntimeContextBootstrap.Create(",
            StringComparison.Ordinal);
        var activeIdentityIndex = source.IndexOf(
            "MemActiveApiProcessStateStore.ResolveForHostCommand(",
            StringComparison.Ordinal);
        var registrationIndex = source.IndexOf(
            "builder.Services.AddSingleton(runtimeContext)",
            StringComparison.Ordinal);

        Assert.True(bootstrapIndex >= 0);
        Assert.True(activeIdentityIndex > bootstrapIndex);
        Assert.True(registrationIndex > activeIdentityIndex);
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

        throw new InvalidOperationException("Could not locate the MEM repository root from the test runtime.");
    }
}
