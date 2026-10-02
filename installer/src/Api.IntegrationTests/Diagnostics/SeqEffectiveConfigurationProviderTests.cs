using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Services;
using Api.IntegrationTests.Runtime;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqEffectiveConfigurationProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mem-seq-effective-{Guid.NewGuid():N}");

    [Fact]
    public async Task Guided_state_enables_management_without_rewriting_static_configuration()
    {
        var options = new SeqDiagnosticsOptions
        {
            ManagementEnabled = false,
            EulaAccepted = false,
            UiUrl = null,
            PreferredHostPort = 15341,
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json"
        };
        var store = new SeqBootstrapStateStore(
            options,
            new TestHostEnvironment(_root));
        await store.WriteAsync(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 16341,
            PrivateUiUrl: "https://seq.example.test/"),
            CancellationToken.None);
        var provider = new SeqEffectiveConfigurationProvider(options, store);

        var effective = provider.Get();
        var effectiveOptions = provider.CreateEffectiveOptions();

        Assert.True(effective.ManagementEnabled);
        Assert.True(effective.EulaAccepted);
        Assert.Equal(16341, effective.PreferredHostPort);
        Assert.Equal("https://seq.example.test/", effective.UiUrl);
        Assert.True(effectiveOptions.ManagementEnabled);
        Assert.True(effectiveOptions.EulaAccepted);
        Assert.Equal(16341, effectiveOptions.PreferredHostPort);
        Assert.Equal("https://seq.example.test/", effectiveOptions.UiUrl);
        Assert.Equal(options.ApprovedImageReference, effectiveOptions.ApprovedImageReference);
        Assert.False(effectiveOptions.AllowOperationalPull);
        Assert.False(options.ManagementEnabled);
        Assert.False(options.EulaAccepted);
    }

    [Theory]
    [InlineData(MemRuntimeModes.LocalDevelopment, false, null, "http://127.0.0.1:16341/")]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment, true, null, "http://127.0.0.1:16341/")]
    [InlineData(MemRuntimeModes.ContainerizedProduction, true, "192.168.50.10", "http://192.168.50.10:16341/")]
    public async Task Verified_guided_runtime_derives_a_safe_browser_authority_when_no_override_exists(
        string runtimeMode,
        bool runningInContainer,
        string? hostAccessIpv4,
        string expectedUrl)
    {
        var options = new SeqDiagnosticsOptions
        {
            UiUrl = null,
            BootstrapStatePath = "data/diagnostics/seq-bootstrap-auto.json"
        };
        var store = new SeqBootstrapStateStore(
            options,
            new TestHostEnvironment(_root));
        await store.WriteAsync(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 16341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);
        var context = TestRuntimeContext.Create(
            Path.Combine(_root, $"runtime-{Guid.NewGuid():N}"),
            mode: runtimeMode,
            runningInContainer: runningInContainer,
            containerName: runtimeMode switch
            {
                MemRuntimeModes.ContainerizedDevelopment => "mem-control-plane-dev",
                MemRuntimeModes.ContainerizedProduction => "mem-control-plane",
                _ => null
            },
            uiDeliveryMode: runtimeMode == MemRuntimeModes.LocalDevelopment
                ? MemUiDeliveryModes.Vite
                : MemUiDeliveryModes.EmbeddedSpa,
            hostAccessIpv4: hostAccessIpv4);
        var provider = new SeqEffectiveConfigurationProvider(options, store, context);

        var effective = provider.Get();

        Assert.Equal(expectedUrl, effective.UiUrl);
    }

    [Fact]
    public async Task Empty_static_ui_setting_does_not_block_the_runtime_generated_browser_authority()
    {
        var options = new SeqDiagnosticsOptions
        {
            UiUrl = string.Empty,
            BootstrapStatePath = "data/diagnostics/seq-bootstrap-empty-static.json"
        };
        var store = new SeqBootstrapStateStore(
            options,
            new TestHostEnvironment(_root));
        await store.WriteAsync(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 15341,
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);
        var context = TestRuntimeContext.Create(
            Path.Combine(_root, $"runtime-{Guid.NewGuid():N}"),
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite);
        var provider = new SeqEffectiveConfigurationProvider(options, store, context);

        var effective = provider.Get();

        Assert.Equal("http://127.0.0.1:15341/", effective.UiUrl);
    }

    [Fact]
    public async Task Explicit_guided_browser_authority_overrides_the_runtime_generated_candidate()
    {
        var options = new SeqDiagnosticsOptions
        {
            UiUrl = null,
            BootstrapStatePath = "data/diagnostics/seq-bootstrap-explicit.json"
        };
        var store = new SeqBootstrapStateStore(
            options,
            new TestHostEnvironment(_root));
        await store.WriteAsync(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 16341,
            PrivateUiUrl: "https://seq.private.example/",
            RuntimeVerifiedAtUtc: DateTimeOffset.UtcNow),
            CancellationToken.None);
        var context = TestRuntimeContext.Create(
            Path.Combine(_root, $"runtime-{Guid.NewGuid():N}"),
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            hostAccessIpv4: "192.168.50.10");
        var provider = new SeqEffectiveConfigurationProvider(options, store, context);

        var effective = provider.Get();

        Assert.Equal("https://seq.private.example/", effective.UiUrl);
    }

    [Fact]
    public async Task Unverified_guided_state_does_not_advertise_an_automatic_browser_authority()
    {
        var options = new SeqDiagnosticsOptions
        {
            UiUrl = null,
            BootstrapStatePath = "data/diagnostics/seq-bootstrap-unverified.json"
        };
        var store = new SeqBootstrapStateStore(
            options,
            new TestHostEnvironment(_root));
        await store.WriteAsync(new SeqBootstrapState(
            ManagementEnabled: true,
            EulaAccepted: true,
            SelectedHostPort: 16341),
            CancellationToken.None);
        var context = TestRuntimeContext.Create(
            Path.Combine(_root, $"runtime-{Guid.NewGuid():N}"),
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite);
        var provider = new SeqEffectiveConfigurationProvider(options, store, context);

        var effective = provider.Get();

        Assert.Null(effective.UiUrl);
    }

    [Fact]
    public async Task Invalid_guided_state_falls_back_to_static_policy_with_warning()
    {
        var options = new SeqDiagnosticsOptions
        {
            ManagementEnabled = false,
            BootstrapStatePath = "data/diagnostics/seq-bootstrap.json"
        };
        var path = Path.Combine(_root, "data", "diagnostics", "seq-bootstrap.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "invalid");
        var provider = new SeqEffectiveConfigurationProvider(
            options,
            new SeqBootstrapStateStore(options, new TestHostEnvironment(_root)));

        var effective = provider.Get();

        Assert.False(effective.ManagementEnabled);
        Assert.Equal("seq_bootstrap_state_invalid", effective.WarningCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
