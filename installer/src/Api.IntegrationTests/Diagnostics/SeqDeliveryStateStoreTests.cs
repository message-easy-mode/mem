using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Api.IntegrationTests.Runtime;
using Shared.ControlPlane.Runtime;
using Modules.Integrations.Seq.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqDeliveryStateStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Desired_state_is_persisted_without_secret_or_runtime_authority()
    {
        using var directory = new TemporaryDirectory();
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            DeliveryStatePath = "data/diagnostics/seq-delivery.json"
        };
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            new TestHostEnvironment(directory.Path));

        var state = await store.SetDesiredAsync(true, CancellationToken.None);
        var reloaded = store.GetState();
        var json = await File.ReadAllTextAsync(
            System.IO.Path.Combine(
                directory.Path,
                "data",
                "diagnostics",
                "seq-delivery.json"));

        Assert.False(state.EffectiveEnabled);
        Assert.True(state.DesiredEnabled);
        Assert.True(state.RestartRequired);
        Assert.Equal(Now, state.UpdatedAtUtc);
        Assert.Equal(state, reloaded);
        Assert.Contains("\"enabled\": true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("container", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/data/seq", json, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Desired_disablement_turns_off_a_previously_effective_sink_after_restart()
    {
        using var directory = new TemporaryDirectory();
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = true,
            DeliveryStatePath = "data/diagnostics/seq-delivery.json"
        };
        var environment = new TestHostEnvironment(directory.Path);
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            environment);
        await store.SetDesiredAsync(false, CancellationToken.None);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "true",
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath
        });

        SeqDeliveryStateStore.ApplyConfigurationOverride(
            configuration,
            directory.Path,
            TestRuntimeContext.Create(
                directory.Path,
                MemRuntimeModes.LocalDevelopment));

        Assert.False(configuration.GetValue<bool>(
            $"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"));
    }

    [Theory]
    [InlineData(
        MemRuntimeModes.LocalDevelopment,
        "http://127.0.0.1:25341/",
        "http://127.0.0.1:25341/")]
    [InlineData(
        MemRuntimeModes.ContainerizedDevelopment,
        "http://seq-dev:5341/",
        "http://seq-dev/")]
    [InlineData(
        MemRuntimeModes.ContainerizedProduction,
        "http://seq:5341/",
        "http://seq/")]
    public async Task Staged_preference_is_applied_before_logging_configuration_is_consumed(
        string runtimeMode,
        string expectedIngestionUrl,
        string expectedHealthUrl)
    {
        using var directory = new TemporaryDirectory();
        var secretRoot = System.IO.Path.Combine(directory.Path, "seq-secrets");
        Directory.CreateDirectory(secretRoot);
        var apiKeyPath = System.IO.Path.Combine(secretRoot, "ingestion-api-key");
        await File.WriteAllTextAsync(apiKeyPath, "guided-api-key");
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            ApiKeyEnvironmentVariableName = string.Empty,
            ApiKeyFilePath = apiKeyPath,
            AdminPasswordHashEnvironmentVariableName = string.Empty,
            AdminPasswordHashFilePath = null,
            SecretRootPath = secretRoot,
            DeliveryStatePath = "data/diagnostics/seq-delivery.json",
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json"
        };
        var environment = new TestHostEnvironment(directory.Path);
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            environment);
        await store.SetDesiredAsync(true, CancellationToken.None);
        await new SeqBootstrapStateStore(options, environment).WriteAsync(
            new SeqBootstrapState(
                ManagementEnabled: true,
                SelectedHostPort: 25341,
                RuntimeVerifiedAtUtc: Now.AddMinutes(-2),
                IngestionCredentialState: "available",
                DeliveryVerifiedAtUtc: Now.AddMinutes(-1)),
            CancellationToken.None);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "false",
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                options.BootstrapStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyEnvironmentVariableName"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyFilePath"] =
                apiKeyPath,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashEnvironmentVariableName"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashFilePath"] =
                null,
            [$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"] =
                secretRoot
        });

        var containerized = MemRuntimeModes.IsContainerized(runtimeMode);
        SeqDeliveryStateStore.ApplyConfigurationOverride(
            configuration,
            directory.Path,
            TestRuntimeContext.Create(
                directory.Path,
                runtimeMode,
                runningInContainer: containerized,
                containerName: runtimeMode switch
            {
                MemRuntimeModes.ContainerizedDevelopment =>
                    Shared.ControlPlane.MemControlPlaneIdentity.DevelopmentContainerName,
                MemRuntimeModes.ContainerizedProduction =>
                    Shared.ControlPlane.MemControlPlaneIdentity.CanonicalContainerName,
                _ => null
            },
                uiDeliveryMode: containerized
                    ? MemUiDeliveryModes.EmbeddedSpa
                    : MemUiDeliveryModes.Vite));

        Assert.True(configuration.GetValue<bool>(
            $"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"));
        Assert.Equal(
            expectedIngestionUrl,
            configuration[$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"]);
        Assert.Equal(
            expectedHealthUrl,
            configuration[$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"]);
    }


    [Fact]
    public async Task Missing_ingestion_key_keeps_guided_delivery_disabled_after_restart()
    {
        using var directory = new TemporaryDirectory();
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            ApiKeyEnvironmentVariableName = string.Empty,
            ApiKeyFilePath = System.IO.Path.Combine(
                directory.Path,
                "missing-ingestion-api-key"),
            AdminPasswordHashEnvironmentVariableName = string.Empty,
            AdminPasswordHashFilePath = null,
            SecretRootPath = directory.Path,
            DeliveryStatePath = "data/diagnostics/seq-delivery.json",
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json"
        };
        var environment = new TestHostEnvironment(directory.Path);
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            environment);
        await store.SetDesiredAsync(true, CancellationToken.None);
        await new SeqBootstrapStateStore(options, environment).WriteAsync(
            new SeqBootstrapState(
                ManagementEnabled: true,
                SelectedHostPort: 25341,
                RuntimeVerifiedAtUtc: Now.AddMinutes(-2),
                IngestionCredentialState: "available",
                DeliveryVerifiedAtUtc: Now.AddMinutes(-1)),
            CancellationToken.None);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "false",
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                options.BootstrapStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyEnvironmentVariableName"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyFilePath"] =
                options.ApiKeyFilePath,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashEnvironmentVariableName"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashFilePath"] =
                null,
            [$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"] =
                options.SecretRootPath
        });

        SeqDeliveryStateStore.ApplyConfigurationOverride(
            configuration,
            directory.Path,
            TestRuntimeContext.Create(
                directory.Path,
                MemRuntimeModes.LocalDevelopment));

        Assert.False(configuration.GetValue<bool>(
            $"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"));
        Assert.Equal(
            "seq_delivery_startup_prerequisites_unavailable",
            configuration[$"{SeqDiagnosticsOptions.SectionName}:DeliveryStartupWarningCode"]);
    }

    [Fact]
    public async Task Existing_manual_sink_configuration_remains_compatible_without_guided_bootstrap_state()
    {
        using var directory = new TemporaryDirectory();
        var secretRoot = System.IO.Path.Combine(directory.Path, "seq-secrets");
        Directory.CreateDirectory(secretRoot);
        var apiKeyPath = System.IO.Path.Combine(secretRoot, "ingestion-api-key");
        await File.WriteAllTextAsync(apiKeyPath, "manual-api-key");
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = true,
            IngestionUrl = "http://manual-seq.internal:5341/",
            HealthUrl = "http://manual-seq.internal:80/",
            ApiKeyEnvironmentVariableName = string.Empty,
            ApiKeyFilePath = apiKeyPath,
            AdminPasswordHashEnvironmentVariableName = string.Empty,
            AdminPasswordHashFilePath = null,
            SecretRootPath = secretRoot,
            DeliveryStatePath = System.IO.Path.Combine(
                directory.Path,
                "data",
                "diagnostics",
                "seq-delivery.json"),
            BootstrapStatePath = System.IO.Path.Combine(
                directory.Path,
                "data",
                "diagnostics",
                "seq-bootstrap.json"),
            HostDataPath = System.IO.Path.Combine(directory.Path, "seq-data")
        };
        var environment = new TestHostEnvironment(directory.Path);
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            environment);
        await store.SetDesiredAsync(true, CancellationToken.None);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "true",
            [$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"] =
                options.IngestionUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"] =
                options.HealthUrl,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyEnvironmentVariableName"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:ApiKeyFilePath"] =
                apiKeyPath,
            [$"{SeqDiagnosticsOptions.SectionName}:SecretRootPath"] =
                secretRoot,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashEnvironmentVariableName"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:AdminPasswordHashFilePath"] =
                string.Empty,
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                options.BootstrapStatePath
        });

        SeqDeliveryStateStore.ApplyConfigurationOverride(
            configuration,
            directory.Path,
            TestRuntimeContext.Create(
                directory.Path,
                MemRuntimeModes.LocalDevelopment));

        Assert.True(configuration.GetValue<bool>(
            $"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"));
        Assert.Equal(
            options.IngestionUrl,
            configuration[$"{SeqDiagnosticsOptions.SectionName}:IngestionUrl"]);
        Assert.Equal(
            options.HealthUrl,
            configuration[$"{SeqDiagnosticsOptions.SectionName}:HealthUrl"]);
        Assert.Null(configuration[
            $"{SeqDiagnosticsOptions.SectionName}:DeliveryStartupWarningCode"]);
    }

    [Fact]
    public async Task Missing_verified_bootstrap_state_keeps_the_sink_disabled_and_reports_pending_activation()
    {
        using var directory = new TemporaryDirectory();
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            DeliveryStatePath = "data/diagnostics/seq-delivery.json",
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json"
        };
        var environment = new TestHostEnvironment(directory.Path);
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            environment);
        await store.SetDesiredAsync(true, CancellationToken.None);

        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"] = "false",
            [$"{SeqDiagnosticsOptions.SectionName}:DeliveryStatePath"] =
                options.DeliveryStatePath,
            [$"{SeqDiagnosticsOptions.SectionName}:BootstrapStatePath"] =
                options.BootstrapStatePath
        });

        SeqDeliveryStateStore.ApplyConfigurationOverride(
            configuration,
            directory.Path,
            TestRuntimeContext.Create(
                directory.Path,
                MemRuntimeModes.LocalDevelopment));

        Assert.False(configuration.GetValue<bool>(
            $"{SeqDiagnosticsOptions.SectionName}:SinkEnabled"));
        Assert.Equal(
            "seq_delivery_startup_prerequisites_unavailable",
            configuration[$"{SeqDiagnosticsOptions.SectionName}:DeliveryStartupWarningCode"]);
    }

    [Fact]
    public async Task Invalid_preference_is_reported_without_changing_the_effective_state()
    {
        using var directory = new TemporaryDirectory();
        var relativePath = System.IO.Path.Combine(
            "data",
            "diagnostics",
            "seq-delivery.json");
        var fullPath = System.IO.Path.Combine(directory.Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "{not-json");
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = true,
            DeliveryStatePath = relativePath
        };
        var store = new SeqDeliveryStateStore(
            options,
            new FixedTimeProvider(Now),
            new TestHostEnvironment(directory.Path));

        var state = store.GetState();

        Assert.True(state.EffectiveEnabled);
        Assert.True(state.DesiredEnabled);
        Assert.False(state.RestartRequired);
        Assert.Equal("seq_delivery_state_invalid", state.WarningCode);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mem-seq-delivery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
