using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Destroy;
using Infrastructure.Data.Entities;
using Modules.Integrations.Npm.Contracts;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Tests.Services;

public sealed class RuntimeStackDestroyManifestlessRecoveryTests
{
    [Fact]
    public void STACK_DESTROY_REL_01B_reconstructs_exact_destroy_ownership_from_active_database_rows()
    {
        var stack = CompleteActiveStack();

        var snapshot = RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack);
        var manifestRecoverySnapshot = RuntimeStackManifestDatabaseReconstructor.Build(stack);

        Assert.Equal(snapshot.StackId, manifestRecoverySnapshot.StackId);
        Assert.Equal(snapshot.Slug, manifestRecoverySnapshot.Slug);
        Assert.Equal(snapshot.Matrix.ContainerId, manifestRecoverySnapshot.Matrix.ContainerId);
        Assert.Equal(snapshot.Element?.ContainerId, manifestRecoverySnapshot.Element?.ContainerId);
        Assert.Equal("control-plane-database-reconstruction", snapshot.Source);
        Assert.Equal(stack.Id, snapshot.StackId);
        Assert.Equal("demo-stack-3", snapshot.Slug);
        Assert.Equal("matrix-container-id", snapshot.Matrix.ContainerId);
        Assert.Equal("mem-matrix-demo-stack-3", snapshot.Matrix.ContainerName);
        Assert.Equal("3", snapshot.Matrix.PublicRouteId);
        Assert.Equal("mem-matrix-demo-stack-3", snapshot.Matrix.RuntimeMetadata["publicForwardHost"]);
        Assert.Equal("8008", snapshot.Matrix.RuntimeMetadata["publicForwardPort"]);
        Assert.NotNull(snapshot.Element);
        Assert.Equal(
            "2419d8b525902a54edd3a477c29fef4e4126ddeee20ce8a42353966374d03259",
            snapshot.Element!.ContainerId);
        Assert.Equal("mem-element-demo-stack-3", snapshot.Element.ContainerName);
        Assert.Equal("4", snapshot.Element.PublicRouteId);
        Assert.Equal("database-reconstruction", snapshot.Metadata["ownershipSource"]);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_accepts_unchanged_reconstructed_ownership_at_execution_time()
    {
        var stack = CompleteActiveStack();
        var accepted = RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack);
        var current = RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack);

        RuntimeStackDestroyService.ValidateDatabaseReconstructionSnapshot(
            accepted,
            current);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_reconstructed_ownership_that_changed_after_review()
    {
        var stack = CompleteActiveStack();
        var accepted = RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack);
        var element = Assert.Single(stack.ServiceInstances.Where(service =>
            string.Equals(
                service.ServiceKey,
                ServiceKeys.ElementWeb,
                StringComparison.OrdinalIgnoreCase)));
        element.ContainerId = "replacement-element-container-id";
        var current = RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack);

        var exception = Assert.Throws<RuntimeStackDestroyOwnershipRefusedException>(() =>
            RuntimeStackDestroyService.ValidateDatabaseReconstructionSnapshot(
                accepted,
                current));

        Assert.Contains("changed after reconciliation review", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_reconstruction_without_one_matrix_service_record()
    {
        var stack = ActiveStack();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack));

        Assert.Contains("'matrix' service record is missing", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_reconstruction_for_destroyed_history()
    {
        var stack = ActiveStack();
        stack.Status = "destroyed";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack));

        Assert.Contains("already recorded as destroyed", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_ambiguous_route_ownership()
    {
        var stack = ActiveStack();
        var matrixService = MatrixServiceEntity(stack.Id);
        stack.ServiceInstances.Add(matrixService);
        stack.Routes.Add(Route(stack.Id, matrixService.Id, "3"));
        stack.Routes.Add(Route(stack.Id, matrixService.Id, "99"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeStackDestroyService.BuildOwnershipSnapshotFromDatabase(stack));

        Assert.Contains("has 2 route records", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_accepts_exact_MEM_owned_container_evidence()
    {
        var stackId = Guid.Parse("a69b081d-2ec8-473e-a6bd-ba341e5a6087");
        var runtimeContext = RuntimeContext(
            Guid.Parse("2b3612ed-628c-44ea-92eb-42711c335e3c"));
        var service = ElementServiceManifest(stackId);
        var evidence = OwnedContainerEvidence(
            service,
            runtimeContext,
            mountSources: [$"{service.DataPath}/config.json"]);

        RuntimeStackDestroyContainerOwnershipValidator.ValidateDatabaseReconstruction(
            stackId,
            service,
            evidence,
            runtimeContext);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_conflicting_control_plane_ownership()
    {
        var stackId = Guid.NewGuid();
        var expectedRuntimeContext = RuntimeContext(Guid.NewGuid());
        var conflictingRuntimeContext = RuntimeContext(Guid.NewGuid());
        var service = MatrixServiceManifest(stackId);
        var evidence = OwnedContainerEvidence(
            service,
            conflictingRuntimeContext);

        var exception = Assert.Throws<RuntimeStackDestroyOwnershipRefusedException>(() =>
            RuntimeStackDestroyContainerOwnershipValidator.ValidateDatabaseReconstruction(
                stackId,
                service,
                evidence,
                expectedRuntimeContext));

        Assert.Contains("control-plane-instance", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_container_without_recorded_data_mount()
    {
        var stackId = Guid.NewGuid();
        var runtimeContext = RuntimeContext(Guid.NewGuid());
        var service = MatrixServiceManifest(stackId);
        var evidence = OwnedContainerEvidence(
            service,
            runtimeContext,
            mountSources: ["/some/other/path"]);

        var exception = Assert.Throws<RuntimeStackDestroyOwnershipRefusedException>(() =>
            RuntimeStackDestroyContainerOwnershipValidator.ValidateDatabaseReconstruction(
                stackId,
                service,
                evidence,
                runtimeContext));

        Assert.Contains("does not mount the recorded stack data path", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_accepts_exact_NPM_route_evidence()
    {
        var stackId = Guid.NewGuid();
        var service = MatrixServiceManifest(stackId) with
        {
            RuntimeMetadata = new Dictionary<string, string?>
            {
                ["publicForwardHost"] = "mem-matrix-demo-stack",
                ["publicForwardPort"] = "8008"
            }
        };
        var observed = ProxyHost(
            id: 12,
            domain: service.PublicHost!,
            forwardHost: "mem-matrix-demo-stack",
            forwardPort: 8008,
            certificateId: 1);

        RuntimeStackDestroyRouteOwnershipValidator.ValidateDatabaseReconstruction(
            stackId,
            service,
            observed);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_refuses_NPM_route_with_conflicting_upstream()
    {
        var stackId = Guid.NewGuid();
        var service = MatrixServiceManifest(stackId) with
        {
            RuntimeMetadata = new Dictionary<string, string?>
            {
                ["publicForwardHost"] = "mem-matrix-demo-stack",
                ["publicForwardPort"] = "8008"
            }
        };
        var observed = ProxyHost(
            id: 12,
            domain: service.PublicHost!,
            forwardHost: "some-other-container",
            forwardPort: 8008,
            certificateId: 1);

        var exception = Assert.Throws<RuntimeStackDestroyOwnershipRefusedException>(() =>
            RuntimeStackDestroyRouteOwnershipValidator.ValidateDatabaseReconstruction(
                stackId,
                service,
                observed));

        Assert.Contains("does not match the recorded target", exception.Message);
    }

    [Fact]
    public void STACK_DESTROY_REL_01B_force_never_bypasses_ownership_refusal()
    {
        Assert.False(RuntimeStackDestroyService.CanContinueAfterStepFailure(
            force: true,
            new RuntimeStackDestroyOwnershipRefusedException("refused")));
    }

    private static RuntimeStackEntity CompleteActiveStack()
    {
        var stackId = Guid.Parse("a69b081d-2ec8-473e-a6bd-ba341e5a6087");
        var matrixService = MatrixServiceEntity(stackId);
        var elementService = ElementServiceEntity(stackId);

        return new RuntimeStackEntity
        {
            Id = stackId,
            Slug = "demo-stack-3",
            DisplayName = "Demo Stack 3",
            Status = "public_routes_created",
            LastVerifiedStatus = "public_routes_created",
            LastVerifiedAtUtc = new DateTime(2026, 8, 16, 1, 50, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 8, 16, 1, 50, 0, DateTimeKind.Utc),
            ActiveNpmCertificateId = 1,
            RuntimeNetworkName = "mem-gateway",
            DataRoot = "/data",
            ManifestPath = "/data/control-plane/runtime-stacks/a69b081d2ec8473ea6bdba341e5a6087.json",
            MatrixInstanceId = matrixService.InstanceId,
            ElementInstanceId = elementService.InstanceId,
            ServiceInstances = [matrixService, elementService],
            Routes =
            [
                Route(stackId, matrixService.Id, "3"),
                new RuntimeRouteEntity
                {
                    Id = Guid.NewGuid(),
                    RuntimeStackId = stackId,
                    RuntimeServiceInstanceId = elementService.Id,
                    ServiceKey = ServiceKeys.ElementWeb,
                    Provider = "npm",
                    RouteKind = "ElementWeb",
                    IsPublic = true,
                    PublicHost = "chat-demo-stack-3.deltabox.dev",
                    PublicBaseUrl = "https://chat-demo-stack-3.deltabox.dev",
                    ForwardScheme = "http",
                    ForwardHost = "mem-element-demo-stack-3",
                    ForwardPort = 80,
                    ProviderRouteId = "4",
                    NpmCertificateId = 1,
                    Status = "created"
                }
            ]
        };
    }

    private static RuntimeStackEntity ActiveStack() => new()
    {
        Id = Guid.NewGuid(),
        Slug = "demo-stack-3",
        Status = "public_routes_created",
        LastVerifiedStatus = "public_routes_created",
        UpdatedAtUtc = DateTime.UtcNow,
        MatrixInstanceId = Guid.NewGuid(),
        ServiceInstances = [],
        Routes = []
    };

    private static RuntimeServiceInstanceEntity MatrixServiceEntity(Guid stackId) => new()
    {
        Id = Guid.NewGuid(),
        RuntimeStackId = stackId,
        InstanceId = Guid.NewGuid(),
        ServiceKey = ServiceKeys.Matrix,
        Status = "running",
        ContainerId = "matrix-container-id",
        ContainerName = "mem-matrix-demo-stack-3",
        NetworkName = "mem-gateway",
        InternalHost = "mem-matrix-demo-stack-3",
        InternalBaseUrl = "http://mem-matrix-demo-stack-3:8008",
        PublicHost = "matrix-demo-stack-3.deltabox.dev",
        PublicBaseUrl = "https://matrix-demo-stack-3.deltabox.dev",
        ContainerPort = 8008,
        DataPath = $"/host/instances/{stackId:N}/matrix-instance",
        ServerName = "matrix-demo-stack-3.deltabox.dev"
    };

    private static RuntimeServiceInstanceEntity ElementServiceEntity(Guid stackId) => new()
    {
        Id = Guid.NewGuid(),
        RuntimeStackId = stackId,
        InstanceId = Guid.NewGuid(),
        ServiceKey = ServiceKeys.ElementWeb,
        Status = "stopped",
        ContainerId = "2419d8b525902a54edd3a477c29fef4e4126ddeee20ce8a42353966374d03259",
        ContainerName = "mem-element-demo-stack-3",
        NetworkName = "mem-gateway",
        InternalHost = "mem-element-demo-stack-3",
        InternalBaseUrl = "http://mem-element-demo-stack-3",
        PublicHost = "chat-demo-stack-3.deltabox.dev",
        PublicBaseUrl = "https://chat-demo-stack-3.deltabox.dev",
        ContainerPort = 80,
        DataPath = $"/host/instances/{stackId:N}/element-instance"
    };

    private static RuntimeStackServiceManifest MatrixServiceManifest(Guid stackId)
    {
        var dataPath = $"/host/instances/{stackId:N}/matrix-instance";
        return new RuntimeStackServiceManifest(
            InstanceId: Guid.NewGuid(),
            ServiceKey: ServiceKeys.Matrix,
            ContainerId: "matrix-container-id",
            ContainerName: "mem-matrix-demo-stack",
            HostPort: 0,
            DataPath: dataPath,
            ServerName: "matrix-demo-stack.deltabox.dev",
            PublicHost: "matrix-demo-stack.deltabox.dev",
            PublicBaseUrl: "https://matrix-demo-stack.deltabox.dev",
            InternalHost: "mem-matrix-demo-stack",
            InternalBaseUrl: "http://mem-matrix-demo-stack:8008",
            PublicRouteId: "12",
            InternalRouteId: null,
            NpmCertificateId: 1,
            RuntimeMetadata: new Dictionary<string, string?>());
    }

    private static RuntimeStackServiceManifest ElementServiceManifest(Guid stackId)
    {
        var dataPath = $"/host/instances/{stackId:N}/element-instance";
        return new RuntimeStackServiceManifest(
            InstanceId: Guid.NewGuid(),
            ServiceKey: ServiceKeys.ElementWeb,
            ContainerId: "container-id",
            ContainerName: "mem-element-demo-stack-3",
            HostPort: 0,
            DataPath: dataPath,
            ServerName: null,
            PublicHost: "chat-demo-stack-3.deltabox.dev",
            PublicBaseUrl: "https://chat-demo-stack-3.deltabox.dev",
            InternalHost: "mem-element-demo-stack-3",
            InternalBaseUrl: "http://mem-element-demo-stack-3",
            PublicRouteId: "4",
            InternalRouteId: null,
            NpmCertificateId: 1,
            RuntimeMetadata: new Dictionary<string, string?>());
    }

    private static RuntimeStackDestroyContainerObservation OwnedContainerEvidence(
        RuntimeStackServiceManifest service,
        MemControlPlaneRuntimeContext runtimeContext,
        IReadOnlyList<string>? mountSources = null) =>
        new(
            Id: service.ContainerId!,
            Name: service.ContainerName!,
            Running: false,
            Labels: new Dictionary<string, string>
            {
                [MemDockerOwnershipLabels.ManagedKey] = "true",
                [MemDockerOwnershipLabels.ResourceKey] = "matrix-stack-service",
                [MemDockerOwnershipLabels.ServiceKey] = service.ServiceKey == ServiceKeys.Matrix
                    ? "synapse"
                    : "element",
                [MemDockerOwnershipLabels.ControlPlaneInstanceKey] =
                    runtimeContext.ControlPlaneInstanceId.ToString("D"),
                [MemDockerOwnershipLabels.RuntimeModeKey] = runtimeContext.RuntimeMode,
                ["mem.managed-by"] = "host-agent",
                ["mem.component"] = service.ServiceKey == ServiceKeys.Matrix
                    ? "matrix"
                    : "element-web"
            },
            MountSources: mountSources ?? [service.DataPath!]);

    private static MemControlPlaneRuntimeContext RuntimeContext(Guid controlPlaneId) =>
        new(
            SchemaVersion: 1,
            RuntimeMode: MemRuntimeModes.ContainerizedDevelopment,
            ControlPlaneInstanceId: controlPlaneId,
            ApiProcessInstanceId: Guid.NewGuid(),
            EnvironmentName: "Production",
            RunningInContainer: true,
            ContentRootPath: "/app",
            ContentRootKind: "published-container",
            StateRootPath: "/data",
            StateRootKind: "development-volume",
            StateRootProfile: "default",
            UiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
            DockerEndpointKind: "local-unix-socket",
            ConfiguredContainerName: "mem-control-plane-dev",
            ApplicationName: "mem-control-plane",
            Version: "0.2.0",
            Commit: null,
            ValidationState: "valid",
            MutationsAllowed: true,
            ShowDevelopmentBanner: true,
            Warnings: []);

    private static RuntimeRouteEntity Route(
        Guid stackId,
        Guid serviceId,
        string providerRouteId) => new()
        {
            Id = Guid.NewGuid(),
            RuntimeStackId = stackId,
            RuntimeServiceInstanceId = serviceId,
            ServiceKey = ServiceKeys.Matrix,
            Provider = "npm",
            RouteKind = "Matrix",
            IsPublic = true,
            PublicHost = "matrix-demo-stack-3.deltabox.dev",
            ForwardScheme = "http",
            ForwardHost = "mem-matrix-demo-stack-3",
            ForwardPort = 8008,
            ProviderRouteId = providerRouteId,
            NpmCertificateId = 1,
            Status = "created"
        };

    private static NpmProxyHost ProxyHost(
        int id,
        string domain,
        string forwardHost,
        int forwardPort,
        int certificateId) =>
        new(
            id: id,
            domain_names: [domain],
            forward_host: forwardHost,
            forward_port: forwardPort,
            access_list_id: 0,
            certificate_id: certificateId,
            forward_scheme: "http",
            advanced_config: string.Empty,
            meta: new NpmProxyHostMeta(nginx_online: true, nginx_err: null),
            locations: [],
            ssl_forced: true,
            http2_support: true,
            allow_websocket_upgrade: true,
            block_exploits: true,
            caching_enabled: false,
            enabled: true,
            hsts_enabled: false,
            hsts_subdomains: false,
            trust_forwarded_proto: false);
}
