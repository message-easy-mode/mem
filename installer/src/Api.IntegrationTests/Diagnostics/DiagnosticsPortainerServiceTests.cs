using Microsoft.AspNetCore.Http;
using Modules.Integrations.Portainer.Contracts;
using Modules.Integrations.Portainer.Services;
using Modules.Operator.Diagnostics.Services;
using Shared.Diagnostics;
using Shared.Exceptions;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsPortainerServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Overview_projects_only_server_authored_safe_links()
    {
        var service = CreateService();

        var result = await service.GetOverviewAsync(CancellationToken.None);
        var json = System.Text.Json.JsonSerializer.Serialize(result);

        Assert.True(result.Available);
        Assert.True(result.Managed);
        Assert.True(result.EnvironmentConfigured);
        Assert.True(result.ExactResourceLinksSupported);
        Assert.Equal("https://portainer.example.test", result.Links.Home);
        Assert.Contains("#!/endpoints/1/docker/dashboard", result.Links.Environment);
        Assert.Contains("#!/endpoints/1/docker/containers", result.Links.Containers);
        Assert.DoesNotContain("container-", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Incident_handoff_resolves_the_current_container_and_uses_the_exact_route()
    {
        var containerId = new string('a', 64);
        var locator = new FakeLocator(new MemDockerResourceLocation(
            new MemDiagnosticResource("stack", "demo", Service: "matrix"),
            containerId,
            "Demo Synapse"));
        var service = CreateService(locator: locator);

        var result = await service.HandoffIncidentAsync(
            "inc_demo",
            CancellationToken.None);

        Assert.Equal("exact", result.Kind);
        Assert.EndsWith(
            $"#!/endpoints/1/docker/containers/{containerId}",
            result.Location,
            StringComparison.Ordinal);
        Assert.Equal("inc_demo", locator.IncidentId);
    }

    [Fact]
    public async Task Stack_service_handoff_uses_only_the_logical_stack_and_service_contract()
    {
        var containerId = new string('e', 64);
        var locator = new FakeLocator(new MemDockerResourceLocation(
            new MemDiagnosticResource("stack", "family", Service: "matrix"),
            containerId,
            "Family Synapse"));
        var service = CreateService(locator: locator);

        var result = await service.HandoffResourceAsync(
            "stack",
            "Family-Chat",
            "synapse",
            CancellationToken.None);

        Assert.Equal("exact", result.Kind);
        Assert.Equal("stack", locator.Resource?.Kind);
        Assert.Equal("Family-Chat", locator.Resource?.Id);
        Assert.Equal("synapse", locator.Resource?.Service);
    }

    [Fact]
    public async Task Missing_or_destroyed_resource_falls_back_to_the_containers_list()
    {
        var service = CreateService(locator: new FakeLocator(null));

        var result = await service.HandoffResourceAsync(
            "migration",
            "migration-1",
            "synapse",
            CancellationToken.None);

        Assert.Equal("containers-fallback", result.Kind);
        Assert.EndsWith(
            "#!/endpoints/1/docker/containers",
            result.Location,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seq_handoff_uses_a_logical_platform_resource_and_never_browser_container_input()
    {
        var containerId = new string('b', 64);
        var locator = new FakeLocator(new MemDockerResourceLocation(
            new MemDiagnosticResource("platform-service", "seq", Service: "seq"),
            containerId,
            "MEM platform service seq"));
        var service = CreateService(locator: locator);

        var result = await service.HandoffSeqAsync(CancellationToken.None);

        Assert.Equal("exact", result.Kind);
        Assert.Equal("platform-service", locator.Resource?.Kind);
        Assert.Equal("seq", locator.Resource?.Id);
        Assert.Equal("seq", locator.Resource?.Service);
    }

    [Fact]
    public async Task Coturn_handoff_uses_the_logical_platform_resource_and_never_browser_container_input()
    {
        var containerId = new string('f', 64);
        var locator = new FakeLocator(new MemDockerResourceLocation(
            new MemDiagnosticResource("platform-service", "coturn", Service: "coturn"),
            containerId,
            "MEM platform service coturn"));
        var service = CreateService(locator: locator);

        var result = await service.HandoffResourceAsync(
            "platform-service",
            "coturn",
            "coturn",
            CancellationToken.None);

        Assert.Equal("exact", result.Kind);
        Assert.Equal("platform-service", locator.Resource?.Kind);
        Assert.Equal("coturn", locator.Resource?.Id);
        Assert.Equal("coturn", locator.Resource?.Service);
        Assert.EndsWith(
            $"#!/endpoints/1/docker/containers/{containerId}",
            result.Location,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Arbitrary_container_target_is_rejected()
    {
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<MemProblemException>(() =>
            service.HandoffResourceAsync(
                "container",
                new string('c', 64),
                null,
                CancellationToken.None));

        Assert.Equal("diagnostic_resource_not_supported", exception.Code);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task Unavailable_portainer_returns_a_stable_problem_instead_of_a_broken_link()
    {
        var status = ReadyStatus() with
        {
            Running = false,
            Ready = false
        };
        var service = CreateService(status: status);

        var exception = await Assert.ThrowsAsync<MemProblemException>(() =>
            service.HandoffSeqAsync(CancellationToken.None));

        Assert.Equal("portainer_unavailable", exception.Code);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, exception.StatusCode);
    }

    [Fact]
    public async Task Missing_environment_configuration_falls_back_to_the_authoritative_home()
    {
        var options = Options();
        options.EnvironmentId = null;
        var service = new DiagnosticsPortainerService(
            new FakeStatusReader(ReadyStatus()),
            new DiagnosticsPortainerLinkBuilder(options),
            new FakeIncidentReader(),
            new FakeLocator(null),
            new FixedTimeProvider(Now));

        var overview = await service.GetOverviewAsync(CancellationToken.None);
        var handoff = await service.HandoffSeqAsync(CancellationToken.None);

        Assert.True(overview.Available);
        Assert.False(overview.EnvironmentConfigured);
        Assert.True(overview.Capabilities.CanOpenHome);
        Assert.False(overview.Capabilities.CanOpenContainers);
        Assert.Contains("portainer_environment_not_configured", overview.Warnings);
        Assert.Equal("home-fallback", handoff.Kind);
        Assert.Equal("https://portainer.example.test", handoff.Location);
    }

    [Fact]
    public async Task Host_native_development_uses_the_live_local_Portainer_port_before_private_handoff_is_configured()
    {
        var options = Options();
        options.UiUrl = null;
        options.EnvironmentId = null;
        var service = new DiagnosticsPortainerService(
            new FakeStatusReader(ReadyStatus()),
            new DiagnosticsPortainerLinkBuilder(options),
            new FakeIncidentReader(),
            new FakeLocator(null),
            new FixedTimeProvider(Now),
            LocalDevelopmentRuntime());

        var overview = await service.GetOverviewAsync(CancellationToken.None);
        var handoff = await service.HandoffResourceAsync(
            "platform-service",
            "coturn",
            "coturn",
            CancellationToken.None);

        Assert.True(overview.Available);
        Assert.False(overview.EnvironmentConfigured);
        Assert.Equal("https://localhost:9443", overview.Links.Home);
        Assert.True(overview.Capabilities.CanOpenHome);
        Assert.False(overview.Capabilities.CanOpenContainers);
        Assert.Contains("portainer_ui_not_configured", overview.Warnings);
        Assert.Contains("portainer_environment_not_configured", overview.Warnings);
        Assert.Equal("home-fallback", handoff.Kind);
        Assert.Equal("https://localhost:9443", handoff.Location);
    }

    [Fact]
    public async Task Containerized_development_uses_the_live_local_Portainer_port_before_private_handoff_is_configured()
    {
        var options = Options();
        options.UiUrl = null;
        options.EnvironmentId = null;
        var service = new DiagnosticsPortainerService(
            new FakeStatusReader(ReadyStatus()),
            new DiagnosticsPortainerLinkBuilder(options),
            new FakeIncidentReader(),
            new FakeLocator(null),
            new FixedTimeProvider(Now),
            LocalDevelopmentRuntime() with
            {
                RuntimeMode = MemRuntimeModes.ContainerizedDevelopment,
                RunningInContainer = true
            });

        var overview = await service.GetOverviewAsync(CancellationToken.None);
        var handoff = await service.HandoffSeqAsync(CancellationToken.None);

        Assert.True(overview.Available);
        Assert.False(overview.EnvironmentConfigured);
        Assert.Equal("https://localhost:9443", overview.Links.Home);
        Assert.True(overview.Capabilities.CanOpenHome);
        Assert.False(overview.Capabilities.CanOpenContainers);
        Assert.Contains("portainer_ui_not_configured", overview.Warnings);
        Assert.Contains("portainer_environment_not_configured", overview.Warnings);
        Assert.Equal("home-fallback", handoff.Kind);
        Assert.Equal("https://localhost:9443", handoff.Location);
    }

    [Fact]
    public async Task Containerized_production_does_not_invent_a_local_browser_authority()
    {
        var options = Options();
        options.UiUrl = null;
        options.EnvironmentId = null;
        var service = new DiagnosticsPortainerService(
            new FakeStatusReader(ReadyStatus()),
            new DiagnosticsPortainerLinkBuilder(options),
            new FakeIncidentReader(),
            new FakeLocator(null),
            new FixedTimeProvider(Now),
            LocalDevelopmentRuntime() with
            {
                RuntimeMode = MemRuntimeModes.ContainerizedProduction,
                RunningInContainer = true
            });

        var overview = await service.GetOverviewAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<MemProblemException>(() =>
            service.HandoffSeqAsync(CancellationToken.None));

        Assert.False(overview.Available);
        Assert.Null(overview.Links.Home);
        Assert.False(overview.Capabilities.CanOpenHome);
        Assert.Equal("portainer_not_configured", exception.Code);
    }

    [Fact]
    public void Exact_routes_are_disabled_outside_the_approved_239_contract()
    {
        var options = Options();
        var builder = new DiagnosticsPortainerLinkBuilder(options);
        var links = builder.Build("2.40.0");

        Assert.False(links.ExactResourceLinksSupported);
        Assert.Null(builder.BuildContainer(links, new string('d', 64)));
        Assert.NotNull(links.Containers);
    }

    private static DiagnosticsPortainerService CreateService(
        PortainerRuntimeStatus? status = null,
        FakeLocator? locator = null)
    {
        var options = Options();
        return new DiagnosticsPortainerService(
            new FakeStatusReader(status ?? ReadyStatus()),
            new DiagnosticsPortainerLinkBuilder(options),
            new FakeIncidentReader(),
            locator ?? new FakeLocator(null),
            new FixedTimeProvider(Now));
    }

    private static PortainerRuntimeOptions Options() => new()
    {
        UiUrl = "https://portainer.example.test/",
        EnvironmentId = 1
    };

    private static PortainerRuntimeStatus ReadyStatus() => new(
        ServiceName: "portainer",
        ContainerName: "portainer",
        ApprovedVersion: "2.39.5",
        ApprovedImageReference: "portainer/portainer-ce:2.39.5",
        Exists: true,
        Running: true,
        Ready: true,
        Managed: true,
        OwnershipState: "managed",
        UsesApprovedRuntime: true,
        UpgradeAvailable: false,
        ObservedImageReference: "portainer/portainer-ce:2.39.5",
        ObservedVersion: "2.39.5",
        UiHostPort: 9443,
        DataRetained: true,
        PublishesPublicIngress: false,
        Warnings: []);

    private static MemControlPlaneRuntimeContext LocalDevelopmentRuntime() => new(
        SchemaVersion: 1,
        RuntimeMode: "local-development",
        ControlPlaneInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ApiProcessInstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        EnvironmentName: "Development",
        RunningInContainer: false,
        ContentRootPath: "/repo/installer/src/Api",
        ContentRootKind: "repository",
        StateRootPath: "/repo/installer/data",
        StateRootKind: "repository-local",
        StateRootProfile: "interactive",
        UiDeliveryMode: "vite",
        DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
        DockerEndpointKind: "unix-socket",
        ConfiguredContainerName: null,
        ApplicationName: "mem-control-plane",
        Version: "0.2.0",
        Commit: "test",
        ValidationState: "valid",
        MutationsAllowed: true,
        ShowDevelopmentBanner: true,
        Warnings: []);

    private sealed class FakeStatusReader(PortainerRuntimeStatus status)
        : IPortainerRuntimeStatusReader
    {
        public Task<PortainerRuntimeStatus> GetStatusAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class FakeIncidentReader : IDiagnosticsIncidentResourceReader
    {
        public Task<DiagnosticsIncidentResourceEvidence> LoadAsync(
            string incidentId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DiagnosticsIncidentResourceEvidence(
                incidentId,
                [DiagnosticEvent(incidentId)]));
    }

    private sealed class FakeLocator(MemDockerResourceLocation? result)
        : IMemDockerResourceLocator
    {
        public string? IncidentId { get; private set; }
        public MemDiagnosticResource? Resource { get; private set; }

        public Task<MemDockerResourceLocation?> LocateForIncidentAsync(
            string incidentId,
            IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
            CancellationToken cancellationToken = default)
        {
            IncidentId = incidentId;
            return Task.FromResult(result);
        }

        public Task<MemDockerResourceLocation?> LocateAsync(
            MemDiagnosticResource resource,
            CancellationToken cancellationToken = default)
        {
            Resource = resource;
            return Task.FromResult(result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static MemDiagnosticEvent DiagnosticEvent(string incidentId) => new(
        SchemaVersion: 1,
        EventId: "evt_demo",
        TimestampUtc: Now,
        Severity: "Error",
        EventCode: "demo.failed",
        Source: "api",
        Feature: "stack",
        Stage: null,
        Message: "Demo failed.",
        IncidentId: incidentId,
        TraceId: null,
        SpanId: null,
        RequestId: null,
        CorrelationId: null,
        OperationId: null,
        Resource: new MemDiagnosticResource("stack", "demo", Service: "matrix"),
        Expected: null,
        Observed: null,
        Details: null,
        Exception: null,
        SuggestedAction: null,
        Retryable: false,
        RedactionsApplied: true,
        Truncated: false);
}
