using Microsoft.Extensions.Configuration;
using Modules.Integrations.Seq.Services;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqRuntimeContextProfileTests
{
    [Fact]
    public void Local_source_development_gets_its_own_Seq_identity_storage_and_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-seq-profile", "local");
        var options = DevelopmentOptions(root);

        var profile = SeqRuntimeContextProfile.Create(
            RuntimeContext(MemRuntimeModes.LocalDevelopment),
            options);

        Assert.True(profile.ContextScoped);
        Assert.Equal("mem-seq-local", profile.ContainerName);
        Assert.Equal("seq-local", profile.DockerNetworkAlias);
        Assert.Equal(
            SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort,
            profile.PreferredHostPort);
        Assert.Equal(Path.Combine(root, "seq-local"), profile.HostDataPath);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-local"),
            profile.SecretRootPath);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-local", "ingestion-api-key"),
            profile.ApiKeyFilePath);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-local", "admin-password-hash"),
            profile.AdminPasswordHashFilePath);
        Assert.Equal(
            Path.Combine(root, "diagnostics", "seq-bootstrap-local.json"),
            profile.BootstrapStatePath);
        Assert.Equal(
            Path.Combine(root, "diagnostics", "seq-delivery-local.json"),
            profile.DeliveryStatePath);
    }

    [Fact]
    public void Containerized_development_gets_a_non_conflicting_Seq_identity_storage_and_state()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-seq-profile", "container");
        var options = DevelopmentOptions(root);

        var profile = SeqRuntimeContextProfile.Create(
            RuntimeContext(MemRuntimeModes.ContainerizedDevelopment),
            options);

        Assert.True(profile.ContextScoped);
        Assert.Equal("mem-seq-dev", profile.ContainerName);
        Assert.Equal("seq-dev", profile.DockerNetworkAlias);
        Assert.Equal(
            SeqRuntimeContextProfile.ContainerizedDevelopmentPreferredHostPort,
            profile.PreferredHostPort);
        Assert.Equal(Path.Combine(root, "seq-dev"), profile.HostDataPath);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-dev"),
            profile.SecretRootPath);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-dev", "ingestion-api-key"),
            profile.ApiKeyFilePath);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-dev", "admin-password-hash"),
            profile.AdminPasswordHashFilePath);
        Assert.Equal(
            Path.Combine(root, "diagnostics", "seq-bootstrap-dev.json"),
            profile.BootstrapStatePath);
        Assert.Equal(
            Path.Combine(root, "diagnostics", "seq-delivery-dev.json"),
            profile.DeliveryStatePath);
    }

    [Fact]
    public void Production_keeps_the_existing_canonical_Seq_identity_and_paths()
    {
        var options = new SeqDiagnosticsOptions();

        var profile = SeqRuntimeContextProfile.Create(
            RuntimeContext(MemRuntimeModes.ContainerizedProduction),
            options);

        Assert.False(profile.ContextScoped);
        Assert.Equal("mem-seq", profile.ContainerName);
        Assert.Equal("seq", profile.DockerNetworkAlias);
        Assert.Equal(SeqDiagnosticsOptions.DefaultPreferredHostPort, profile.PreferredHostPort);
        Assert.Equal(options.HostDataPath, profile.HostDataPath);
        Assert.Equal(options.SecretRootPath, profile.SecretRootPath);
        Assert.Equal(options.ApiKeyFilePath, profile.ApiKeyFilePath);
        Assert.Equal(options.AdminPasswordHashFilePath, profile.AdminPasswordHashFilePath);
        Assert.Equal(options.BootstrapStatePath, profile.BootstrapStatePath);
        Assert.Equal(options.DeliveryStatePath, profile.DeliveryStatePath);
    }

    [Fact]
    public void Automated_test_identity_is_unchanged_until_the_future_E2E_isolation_slice()
    {
        var options = new SeqDiagnosticsOptions();

        var profile = SeqRuntimeContextProfile.Create(
            RuntimeContext(MemRuntimeModes.AutomatedTest),
            options);

        Assert.False(profile.ContextScoped);
        Assert.Equal("mem-seq", profile.ContainerName);
        Assert.Equal("seq", profile.DockerNetworkAlias);
        Assert.Equal(SeqDiagnosticsOptions.DefaultPreferredHostPort, profile.PreferredHostPort);
        Assert.Equal(options.HostDataPath, profile.HostDataPath);
        Assert.Equal(options.DeliveryStatePath, profile.DeliveryStatePath);
    }

    [Theory]
    [InlineData(MemRuntimeModes.LocalDevelopment)]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment)]
    public void Explicit_non_default_host_port_remains_operator_controlled(
        string runtimeMode)
    {
        var options = new SeqDiagnosticsOptions
        {
            PreferredHostPort = 19441
        };

        var profile = SeqRuntimeContextProfile.Create(
            RuntimeContext(runtimeMode),
            options);

        Assert.Equal(19441, profile.PreferredHostPort);
    }

    [Fact]
    public void Applying_development_configuration_scope_is_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-seq-profile", "idempotent");
        var options = DevelopmentOptions(root);
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:PreferredHostPort"] =
                options.PreferredHostPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"] = options.IngestionUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"] = options.HealthUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HostDataPath"] = options.HostDataPath,
            [$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"] = options.SecretRootPath,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyFilePath"] = options.ApiKeyFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashFilePath"] =
                options.AdminPasswordHashFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                options.BootstrapStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath
        });
        var context = RuntimeContext(MemRuntimeModes.LocalDevelopment);

        SeqRuntimeContextProfile.ApplyConfigurationOverrides(configuration, context);
        SeqRuntimeContextProfile.ApplyConfigurationOverrides(configuration, context);

        Assert.Equal(
            SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort,
            configuration.GetValue<int>(
                $"{SeqDiagnosticsOptions.SectionName}:PreferredHostPort"));
        Assert.Equal(
            $"http://127.0.0.1:{SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort}/",
            configuration[$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"]);
        Assert.Equal(
            $"http://127.0.0.1:{SeqRuntimeContextProfile.LocalDevelopmentPreferredHostPort}/",
            configuration[$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"]);
        Assert.Equal(
            Path.Combine(root, "seq-local"),
            configuration[$"{SeqDiagnosticsOptions.SectionName}:HostDataPath"]);
        Assert.Equal(
            Path.Combine(root, "secrets", "seq-local"),
            configuration[$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"]);
        Assert.Equal(
            Path.Combine(root, "diagnostics", "seq-bootstrap-local.json"),
            configuration[$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"]);
        Assert.Equal(
            Path.Combine(root, "diagnostics", "seq-delivery-local.json"),
            configuration[$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"]);
    }

    [Fact]
    public void Containerized_development_rewrites_canonical_internal_authorities_to_its_network_alias()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-seq-profile", "container-authority");
        var options = DevelopmentOptions(root);
        var configuration = Configuration(options);

        SeqRuntimeContextProfile.ApplyConfigurationOverrides(
            configuration,
            RuntimeContext(MemRuntimeModes.ContainerizedDevelopment));

        Assert.Equal(
            "http://seq-dev:5341/",
            configuration[$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"]);
        Assert.Equal(
            "http://seq-dev/",
            configuration[$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"]);
    }

    [Fact]
    public void Explicit_custom_internal_authorities_are_not_rewritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "mem-seq-profile", "custom-authority");
        var options = DevelopmentOptions(root);
        options.IngestionUrl = "http://custom-seq.internal:7441";
        options.HealthUrl = "http://custom-seq.internal:7442";
        var configuration = Configuration(options);

        SeqRuntimeContextProfile.ApplyConfigurationOverrides(
            configuration,
            RuntimeContext(MemRuntimeModes.ContainerizedDevelopment));

        Assert.Equal(
            options.IngestionUrl,
            configuration[$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"]);
        Assert.Equal(
            options.HealthUrl,
            configuration[$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"]);
    }

    private static ConfigurationManager Configuration(SeqDiagnosticsOptions options)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:PreferredHostPort"] =
                options.PreferredHostPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"] = options.IngestionUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"] = options.HealthUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HostDataPath"] = options.HostDataPath,
            [$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"] = options.SecretRootPath,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyFilePath"] = options.ApiKeyFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashFilePath"] =
                options.AdminPasswordHashFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                options.BootstrapStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath
        });
        return configuration;
    }

    private static SeqDiagnosticsOptions DevelopmentOptions(string root) => new()
    {
        HostDataPath = Path.Combine(root, "seq"),
        SecretRootPath = Path.Combine(root, "secrets", "seq"),
        ApiKeyFilePath = Path.Combine(root, "secrets", "seq", "ingestion-api-key"),
        AdminPasswordHashFilePath = Path.Combine(
            root,
            "secrets",
            "seq",
            "admin-password-hash"),
        BootstrapStatePath = Path.Combine(root, "diagnostics", "seq-bootstrap.json"),
        DeliveryStatePath = Path.Combine(root, "diagnostics", "seq-delivery.json")
    };

    private static MemControlPlaneRuntimeContext RuntimeContext(string runtimeMode) => new(
        SchemaVersion: 1,
        RuntimeMode: runtimeMode,
        ControlPlaneInstanceId: Guid.NewGuid(),
        ApiProcessInstanceId: Guid.NewGuid(),
        EnvironmentName: runtimeMode == MemRuntimeModes.ContainerizedProduction
            ? "Production"
            : "Development",
        RunningInContainer: MemRuntimeModes.IsContainerized(runtimeMode),
        ContentRootPath: "/app",
        ContentRootKind: "test",
        StateRootPath: "/data",
        StateRootKind: "test",
        StateRootProfile: "test",
        UiDeliveryMode: MemRuntimeModes.IsContainerized(runtimeMode)
            ? MemUiDeliveryModes.EmbeddedSpa
            : MemUiDeliveryModes.Vite,
        DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
        DockerEndpointKind: "local-unix-socket",
        ConfiguredContainerName: null,
        ApplicationName: "MEM Control Plane",
        Version: "0.2.0-test",
        Commit: "test",
        ValidationState: MemRuntimeValidationStates.Valid,
        MutationsAllowed: true,
        ShowDevelopmentBanner: runtimeMode != MemRuntimeModes.ContainerizedProduction,
        Warnings: []);
}
