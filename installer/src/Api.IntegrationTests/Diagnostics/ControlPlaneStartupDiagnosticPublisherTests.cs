using Api.Diagnostics;
using Api.IntegrationTests.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class ControlPlaneStartupDiagnosticPublisherTests
{
    [Fact]
    public async Task Startup_event_is_information_only_and_published_once_per_process()
    {
        var writer = new CapturingWriter();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-diagnostic-{Guid.NewGuid():N}");
        var runtimeContext = TestRuntimeContext.Create(root);
        using var services = ExposureServices(MemControlPlaneExposureProjection.NotApplicable);
        var publisher = new ControlPlaneStartupDiagnosticPublisher(
            writer,
            runtimeContext,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ControlPlaneStartupDiagnosticPublisher>.Instance);

        var first = await publisher.PublishAsync();
        var second = await publisher.PublishAsync();

        Assert.NotNull(first);
        Assert.True(first!.Stored);
        Assert.Null(second);
        var request = Assert.Single(writer.Requests);
        Assert.Equal("control_plane.started", request.EventCode);
        Assert.Equal(MemDiagnosticSeverities.Information, request.Severity);
        Assert.Equal("control-plane", request.Feature);
        Assert.Equal("startup", request.Stage);
        Assert.False(request.CreateIncident);
        Assert.Null(request.IncidentId);
        Assert.Null(first.IncidentId);
        Assert.Equal(runtimeContext.RuntimeMode, request.Details!["runtimeMode"]);
        Assert.Equal(
            runtimeContext.ControlPlaneInstanceId.ToString("D"),
            request.Details["controlPlaneInstanceId"]);
        Assert.Equal(
            runtimeContext.ApiProcessInstanceId.ToString("D"),
            request.Details["apiProcessInstanceId"]);
        Assert.Equal(runtimeContext.Version, request.Details["version"]);
        Assert.Equal(
            MemControlPlaneExposureStates.NotApplicable,
            request.Details["controlPlaneExposureState"]);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Unsupported_container_exposure_creates_one_correlated_security_incident()
    {
        var writer = new CapturingWriter();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-exposure-{Guid.NewGuid():N}");
        var runtimeContext = TestRuntimeContext.Create(
            root,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
        var exposure = new MemControlPlaneExposureProjection(
            MemControlPlaneExposureStates.NeedsAttention,
            MemControlPlaneAccessModes.Unsupported,
            HostAddress: "0.0.0.0",
            HostPort: 8443,
            BindingCount: 1,
            WarningCode: "control_plane_exposure_wildcard_binding");
        using var services = ExposureServices(exposure);
        var publisher = new ControlPlaneStartupDiagnosticPublisher(
            writer,
            runtimeContext,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ControlPlaneStartupDiagnosticPublisher>.Instance);

        await publisher.PublishAsync();

        Assert.Equal(2, writer.Requests.Count);
        var startup = writer.Requests[0];
        Assert.Equal("control_plane.started", startup.EventCode);
        Assert.Equal("needs-attention", startup.Details!["controlPlaneExposureState"]);
        Assert.Equal("0.0.0.0", startup.Details["controlPlaneHostAddress"]);

        var incident = writer.Requests[1];
        Assert.Equal("control_plane.exposure.unsupported", incident.EventCode);
        Assert.Equal(MemDiagnosticSeverities.Error, incident.Severity);
        Assert.True(incident.CreateIncident);
        Assert.Equal("private-administration", incident.Stage);
        Assert.Equal(
            $"inc_control_plane_exposure_{runtimeContext.ControlPlaneInstanceId:N}",
            incident.IncidentId);
        Assert.Equal("0.0.0.0", incident.Observed!["hostAddress"]);
        Assert.Equal("control_plane_exposure_wildcard_binding", incident.Observed["warningCode"]);
        Assert.Contains("SSH tunnel", incident.SuggestedAction, StringComparison.Ordinal);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task Unavailable_container_exposure_records_warning_without_creating_incident()
    {
        var writer = new CapturingWriter();
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-exposure-unavailable-{Guid.NewGuid():N}");
        var runtimeContext = TestRuntimeContext.Create(
            root,
            mode: MemRuntimeModes.ContainerizedProduction,
            runningInContainer: true,
            containerName: "mem-control-plane",
            uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
        var exposure = new MemControlPlaneExposureProjection(
            MemControlPlaneExposureStates.Unavailable,
            MemControlPlaneAccessModes.Unknown,
            HostAddress: null,
            HostPort: null,
            BindingCount: 0,
            WarningCode: "control_plane_exposure_inspection_failed");
        using var services = ExposureServices(exposure);
        var publisher = new ControlPlaneStartupDiagnosticPublisher(
            writer,
            runtimeContext,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ControlPlaneStartupDiagnosticPublisher>.Instance);

        await publisher.PublishAsync();

        Assert.Equal(2, writer.Requests.Count);
        var startup = writer.Requests[0];
        Assert.Equal("control_plane.started", startup.EventCode);
        Assert.Equal("unavailable", startup.Details!["controlPlaneExposureState"]);

        var warning = writer.Requests[1];
        Assert.Equal("control_plane.exposure.unavailable", warning.EventCode);
        Assert.Equal(MemDiagnosticSeverities.Warning, warning.Severity);
        Assert.False(warning.CreateIncident);
        Assert.Null(warning.IncidentId);
        Assert.Equal("private-administration", warning.Stage);
        Assert.Equal(
            "control_plane_exposure_inspection_failed",
            warning.Details!["warningCode"]);

        Directory.Delete(root, recursive: true);
    }

    private static ServiceProvider ExposureServices(
        MemControlPlaneExposureProjection exposure)
    {
        var services = new ServiceCollection();
        services.AddScoped<IControlPlaneExposureInspector>(_ =>
            new StubExposureInspector(exposure));
        return services.BuildServiceProvider();
    }

    private sealed class StubExposureInspector(
        MemControlPlaneExposureProjection exposure) : IControlPlaneExposureInspector
    {
        public Task<MemControlPlaneExposureProjection> InspectAsync(
            CancellationToken cancellationToken) => Task.FromResult(exposure);
    }

    private sealed class CapturingWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_startup_{Requests.Count}",
                IncidentId: request.IncidentId,
                WarningCode: null));
        }
    }
}
