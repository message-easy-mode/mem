using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

public sealed class ManagedServiceRuntimeContractsTests
{
    [Fact]
    public void Local_development_uses_the_server_owned_published_loopback_authority()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Administration,
            SeqSource(publishedHostPort: 15341));

        Assert.Equal("http://127.0.0.1:15341/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.HostPublishedLoopback, authority.RouteKind);
    }

    [Theory]
    [InlineData(MemManagedServicePurposes.Health, 80)]
    [InlineData(MemManagedServicePurposes.Administration, 80)]
    [InlineData(MemManagedServicePurposes.Ingestion, 5341)]
    public void Containerized_runtime_uses_purpose_specific_Docker_network_authorities(
        string purpose,
        int expectedPort)
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(purpose, SeqSource(publishedHostPort: 15341));

        Assert.Equal(Uri.UriSchemeHttp, authority.Authority.Scheme);
        Assert.Equal("seq", authority.Authority.Host);
        Assert.Equal(expectedPort, authority.Authority.Port);
        Assert.Equal("/", authority.Authority.AbsolutePath);
        Assert.Equal(MemManagedServiceRouteKinds.DockerNetwork, authority.RouteKind);
    }

    [Fact]
    public void Browser_authority_remains_separate_from_server_internal_routing()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(root.Path);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Browser,
            SeqSource(
                publishedHostPort: null,
                browserAuthority: new Uri("https://seq.private.example/")));

        Assert.Equal("https://seq.private.example/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.BrowserAuthority, authority.RouteKind);
    }

    [Fact]
    public void Local_development_derives_browser_authority_from_the_published_host_port()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Browser,
            SeqSource(publishedHostPort: 15341));

        Assert.Equal("http://127.0.0.1:15341/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.HostPublishedLoopback, authority.RouteKind);
    }

    [Fact]
    public void Containerized_development_derives_browser_authority_from_the_developer_host_loopback()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.ContainerizedDevelopment,
            runningInContainer: true,
            containerName: "mem-control-plane-dev",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Browser,
            SeqSource(publishedHostPort: 16341));

        Assert.Equal("http://127.0.0.1:16341/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.HostPublishedLoopback, authority.RouteKind);
    }

    [Theory]
    [InlineData("192.168.50.10")]
    [InlineData("10.20.30.40")]
    [InlineData("172.20.10.5")]
    [InlineData("100.100.20.10")]
    public void Containerized_production_derives_browser_authority_only_from_a_private_host_IP(
        string hostAccessIpv4)
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            hostAccessIpv4: hostAccessIpv4);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Browser,
            SeqSource(publishedHostPort: 15341));

        Assert.Equal($"http://{hostAccessIpv4}:15341/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.HostPublishedPrivate, authority.RouteKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("203.0.113.20")]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    public void Containerized_production_does_not_invent_a_browser_authority_from_an_unsafe_host_IP(
        string? hostAccessIpv4)
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            hostAccessIpv4: hostAccessIpv4);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var error = Assert.Throws<MemManagedServiceAuthorityException>(() =>
            resolver.Resolve(
                MemManagedServicePurposes.Browser,
                SeqSource(publishedHostPort: 15341)));

        Assert.Equal("managed_service_authority_unavailable", error.Code);
    }

    [Fact]
    public void Explicit_browser_authority_overrides_the_generated_runtime_candidate()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            hostAccessIpv4: "192.168.50.10");
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Browser,
            SeqSource(
                publishedHostPort: 15341,
                browserAuthority: new Uri("https://seq.private.example/")));

        Assert.Equal("https://seq.private.example/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.BrowserAuthority, authority.RouteKind);
    }

    [Fact]
    public void Local_development_without_a_published_port_fails_closed_even_when_a_Docker_only_configured_authority_exists()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var error = Assert.Throws<MemManagedServiceAuthorityException>(() =>
            resolver.Resolve(
                MemManagedServicePurposes.Health,
                SeqSource(
                    publishedHostPort: null,
                    configuredHealthAuthority: new Uri("http://seq:80/"))));

        Assert.Equal("managed_service_authority_unavailable", error.Code);
    }

    [Fact]
    public void Containerized_runtime_without_a_Docker_network_route_fails_closed_even_when_a_configured_authority_exists()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(
            root.Path,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var source = SeqSource(
            publishedHostPort: 15341,
            configuredHealthAuthority: new Uri("http://127.0.0.1:15341/")) with
        {
            DockerNetworkAlias = null
        };

        var error = Assert.Throws<MemManagedServiceAuthorityException>(() =>
            resolver.Resolve(MemManagedServicePurposes.Health, source));

        Assert.Equal("managed_service_authority_unavailable", error.Code);
    }

    [Fact]
    public void Automated_test_runtime_may_use_an_explicit_configured_authority()
    {
        using var root = new TemporaryDirectory();
        var context = TestRuntimeContext.Create(root.Path);
        var resolver = new MemManagedServiceAuthorityResolver(context);

        var authority = resolver.Resolve(
            MemManagedServicePurposes.Health,
            SeqSource(
                publishedHostPort: null,
                configuredHealthAuthority: new Uri("http://127.0.0.1:25341/")));

        Assert.Equal("http://127.0.0.1:25341/", authority.Authority.AbsoluteUri);
        Assert.Equal(MemManagedServiceRouteKinds.ConfiguredAuthority, authority.RouteKind);
    }

    [Fact]
    public void Restart_contract_is_server_owned_for_local_and_container_modes()
    {
        using var localRoot = new TemporaryDirectory();
        var local = TestRuntimeContext.Create(
            localRoot.Path,
            mode: MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite);
        var localRestart = MemRestartContractFactory.Create(local);

        Assert.Equal(MemRestartKinds.DeveloperProcess, localRestart.Kind);
        Assert.False(localRestart.CommandAvailable);
        Assert.Null(localRestart.Command);

        using var containerRoot = new TemporaryDirectory();
        var container = TestRuntimeContext.Create(
            containerRoot.Path,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
        var containerRestart = MemRestartContractFactory.Create(container);

        Assert.Equal(MemRestartKinds.Container, containerRestart.Kind);
        Assert.True(containerRestart.CommandAvailable);
        Assert.Equal("sudo docker restart mem-control-plane", containerRestart.Command);
    }

    private static MemManagedServiceAuthoritySource SeqSource(
        int? publishedHostPort,
        Uri? browserAuthority = null,
        Uri? configuredHealthAuthority = null) => new(
        ServiceName: "seq",
        PublishedHostPort: publishedHostPort,
        DockerNetworkAlias: "seq",
        HealthPort: 80,
        AdministrationPort: 80,
        IngestionPort: 5341,
        BrowserAuthority: browserAuthority,
        ConfiguredHealthAuthority: configuredHealthAuthority);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"mem-runtime-contract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
