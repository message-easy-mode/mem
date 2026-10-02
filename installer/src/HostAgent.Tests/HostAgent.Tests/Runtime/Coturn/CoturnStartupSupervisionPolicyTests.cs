using HostAgent.Runtime.Coturn;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnStartupSupervisionPolicyTests
{
    [Fact]
    public void Automatic_supervision_requires_containerized_mutation_authority()
    {
        var containerized = CreateRuntimeContext(
            MemRuntimeModes.ContainerizedDevelopment,
            mutationsAllowed: true);

        Assert.True(CoturnStartupSupervisionPolicy.IsEnabled(containerized));
        Assert.False(CoturnStartupSupervisionPolicy.IsEnabled(
            containerized with { MutationsAllowed = false }));
        Assert.False(CoturnStartupSupervisionPolicy.IsEnabled(
            CreateRuntimeContext(MemRuntimeModes.LocalDevelopment, mutationsAllowed: true)));
    }

    [Fact]
    public void Exact_running_runtime_is_ready_for_startup_verification()
    {
        var current = ReadyRuntime();

        Assert.Equal(
            CoturnStartupSupervisionDecisions.Ready,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Docker_network_mode_field_is_not_required_when_expected_gateway_attachment_and_aliases_are_exact()
    {
        var current = ReadyRuntime();

        Assert.Equal("default", current.DockerRuntime?.NetworkMode);
        Assert.False(current.DockerRuntime!.NetworkModeMatches);
        Assert.True(current.DockerRuntime!.ExpectedNetworkAttached);
        Assert.True(current.DockerRuntime!.NetworkAliasesMatch);
        Assert.Equal(
            CoturnStartupSupervisionDecisions.Ready,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Exact_stopped_owned_runtime_can_be_started_without_recreation()
    {
        var current = ReadyRuntime() with
        {
            Status = "not_ready",
            ContainerState = "stopped",
            Readiness = "stopped",
            Running = false,
            DockerState = "exited",
            OperatorStatus = CoturnOperatorStatuses.Stopped,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state"],
            RelayPortsPublished = false,
            PublishedPorts = [],
            DockerRuntime = ReadyDocker() with
            {
                ExitCode = 0,
                FinishedAtUtc = DateTimeOffset.Parse("2026-08-23T01:00:00Z")
            }
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.StartStopped,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Running_runtime_without_live_relay_port_publication_still_requires_repair()
    {
        var current = ReadyRuntime() with
        {
            RuntimeExact = false,
            RelayPortsPublished = false,
            PublishedPorts = []
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.RepairRequired,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Stopped_runtime_with_restart_policy_drift_can_be_corrected_and_started()
    {
        var current = ReadyRuntime() with
        {
            Status = "not_ready",
            ContainerState = "stopped",
            Readiness = "stopped",
            Running = false,
            DockerState = "exited",
            OperatorStatus = CoturnOperatorStatuses.Stopped,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state", "restart-policy"],
            DockerRuntime = ReadyDocker() with
            {
                RestartPolicy = "no",
                RestartPolicyMatches = false
            }
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.StartStopped,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Docker_restart_loop_gets_a_bounded_settling_window_and_is_not_started_as_if_cleanly_stopped()
    {
        var current = ReadyRuntime() with
        {
            Status = "runtime_degraded",
            ContainerState = "stopped",
            Readiness = "degraded",
            Running = false,
            DockerState = "restarting",
            OperatorStatus = CoturnOperatorStatuses.NeedsAttention,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state"],
            DockerRuntime = ReadyDocker() with
            {
                Restarting = true
            }
        };

        Assert.True(CoturnStartupSupervisionPolicy.CouldBeTransientDuringStartup(current));
        Assert.Equal(
            CoturnStartupSupervisionDecisions.Unavailable,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Oom_or_nonzero_exit_is_not_treated_as_a_clean_stopped_container()
    {
        var current = ReadyRuntime() with
        {
            Status = "not_ready",
            ContainerState = "stopped",
            Readiness = "stopped",
            Running = false,
            DockerState = "exited",
            OperatorStatus = CoturnOperatorStatuses.Stopped,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state"],
            DockerRuntime = ReadyDocker() with
            {
                ExitCode = 137,
                OomKilled = true
            }
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.Unavailable,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Restart_policy_only_drift_is_the_only_running_in_place_correction()
    {
        var current = ReadyRuntime() with
        {
            Status = "runtime_drift",
            Readiness = "repair-required",
            OperatorStatus = CoturnOperatorStatuses.RepairRequired,
            RuntimeExact = false,
            RuntimeDrift = ["restart-policy"],
            DockerRuntime = ReadyDocker() with
            {
                RestartPolicy = "no",
                RestartPolicyMatches = false
            }
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.CorrectRestartPolicy,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Missing_docker_inspection_is_unavailable_not_repair_required()
    {
        var current = ReadyRuntime() with
        {
            Status = "inspection_unavailable",
            Readiness = "unknown",
            OperatorStatus = CoturnOperatorStatuses.Unknown,
            RuntimeExact = false,
            DockerRuntime = null,
            RuntimeDrift = ["docker-inspection"]
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.Unavailable,
            CoturnStartupSupervisionPolicy.Evaluate(current));
        Assert.True(CoturnStartupSupervisionPolicy.CouldBeTransientDuringStartup(current));
    }

    [Fact]
    public void Protected_evidence_unavailable_never_triggers_automatic_repair()
    {
        var current = ReadyRuntime() with
        {
            ProtectedEvidenceAccess = CoturnProtectedEvidenceAccess.Unavailable,
            OperatorStatus = CoturnOperatorStatuses.Unknown,
            RuntimeExact = true
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.Unavailable,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Wrong_mount_never_becomes_automatic_startup_repair()
    {
        var current = ReadyRuntime() with
        {
            Status = "not_ready",
            ContainerState = "stopped",
            Readiness = "stopped",
            Running = false,
            DockerState = "exited",
            OperatorStatus = CoturnOperatorStatuses.Stopped,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state", "config-mount"],
            DockerRuntime = ReadyDocker() with
            {
                ConfigMountSourceMatches = false
            }
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.RepairRequired,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Missing_container_is_quiet_before_install_but_requires_review_after_protected_setup_exists()
    {
        var fresh = ReadyRuntime() with
        {
            ContainerExists = false,
            Running = false,
            OwnershipVerified = false,
            ContainerId = null,
            DockerState = null,
            OperatorStatus = CoturnOperatorStatuses.NotDeployed,
            RuntimeExact = false,
            DockerRuntime = null,
            RuntimeDrift = [],
            SecretPresent = false,
            ConfigurationPresent = false,
            ConfigurationExact = false
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.NotInstalled,
            CoturnStartupSupervisionPolicy.Evaluate(fresh));

        Assert.Equal(
            CoturnStartupSupervisionDecisions.RepairRequired,
            CoturnStartupSupervisionPolicy.Evaluate(fresh with { SecretPresent = true }));

        Assert.Equal(
            CoturnStartupSupervisionDecisions.RepairRequired,
            CoturnStartupSupervisionPolicy.Evaluate(fresh with { ConfigurationPresent = true }));
    }

    [Fact]
    public void Foreign_collision_fails_closed()
    {
        var current = ReadyRuntime() with
        {
            OwnershipVerified = false,
            OperatorStatus = CoturnOperatorStatuses.Conflict,
            RuntimeExact = false
        };

        Assert.Equal(
            CoturnStartupSupervisionDecisions.Conflict,
            CoturnStartupSupervisionPolicy.Evaluate(current));
    }

    [Fact]
    public void Gateway_network_startup_drift_is_given_a_bounded_settling_window()
    {
        var current = ReadyRuntime() with
        {
            Status = "runtime_drift",
            Readiness = "repair-required",
            OperatorStatus = CoturnOperatorStatuses.RepairRequired,
            RuntimeExact = false,
            RuntimeDrift = ["runtime-state", "gateway-network", "network-aliases"],
            DockerRuntime = ReadyDocker() with
            {
                ExpectedNetworkAttached = false,
                NetworkAliasesMatch = false
            }
        };

        Assert.True(CoturnStartupSupervisionPolicy.CouldBeTransientDuringStartup(current));
    }

    private static MemControlPlaneRuntimeContext CreateRuntimeContext(
        string runtimeMode,
        bool mutationsAllowed) =>
        new(
            SchemaVersion: 1,
            RuntimeMode: runtimeMode,
            ControlPlaneInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ApiProcessInstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EnvironmentName: "Development",
            RunningInContainer: MemRuntimeModes.IsContainerized(runtimeMode),
            ContentRootPath: MemRuntimeModes.IsContainerized(runtimeMode) ? "/app" : "/repo",
            ContentRootKind: MemRuntimeModes.IsContainerized(runtimeMode) ? "container-image" : "source-tree",
            StateRootPath: MemRuntimeModes.IsContainerized(runtimeMode) ? "/data" : "/repo/installer/data",
            StateRootKind: MemRuntimeModes.IsContainerized(runtimeMode) ? "docker-volume" : "repository-local",
            StateRootProfile: MemStateRootProfiles.Default,
            UiDeliveryMode: MemRuntimeModes.IsContainerized(runtimeMode)
                ? MemUiDeliveryModes.EmbeddedSpa
                : MemUiDeliveryModes.Vite,
            DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
            DockerEndpointKind: "unix-socket",
            ConfiguredContainerName: MemRuntimeModes.IsContainerized(runtimeMode) ? "mem-control-plane-dev" : null,
            ApplicationName: "mem-control-plane",
            Version: "0.2.0",
            Commit: "test",
            ValidationState: MemRuntimeValidationStates.Valid,
            MutationsAllowed: mutationsAllowed,
            ShowDevelopmentBanner: true,
            Warnings: []);

    private static CoturnRuntimeResponse ReadyRuntime() =>
        new(
            Source: "control-plane",
            Status: "ok",
            ContainerState: "running",
            Readiness: "ready",
            ServiceKey: "coturn",
            ContainerName: "mem-coturn",
            Image: "sha256:approved",
            ApprovedImageReference: "coturn/coturn@sha256:approved",
            ResolvedImageId: "sha256:approved",
            ImageApproved: true,
            ContainerExists: true,
            Running: true,
            OwnershipVerified: true,
            ContainerId: "coturn-container-1",
            DockerState: "running",
            OperatorStatus: CoturnOperatorStatuses.RuntimeReady,
            RuntimeExact: true,
            DockerRuntime: ReadyDocker(),
            RuntimeDrift: [],
            ProtectedEvidenceAccess: CoturnProtectedEvidenceAccess.Available,
            Realm: "example.test",
            PublicHost: "turn.example.test",
            TurnPort: 3478,
            RelayMinPort: 49160,
            RelayMaxPort: 49200,
            TurnUris: ["turn:turn.example.test:3478?transport=udp"],
            SecretPresent: true,
            SecretSource: "protected-file",
            SecretStorage: "protected-host-file",
            SecretFilePermissionsApplied: true,
            ExpectedBaseDomain: "example.test",
            ConfiguredBaseDomain: "example.test",
            DomainDriftDetected: false,
            Recreated: false,
            ExternalIp: null,
            RelayPortsPublished: true,
            SecurityPolicyApplied: true,
            SecurityPolicyVersion: "mem-0.2.0-v2",
            PublishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
            RequiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
            Warnings: [],
            Detail: "ready")
        {
            ConfigurationPresent = true,
            ConfigurationExact = true
        };

    private static CoturnDockerRuntimeEvidence ReadyDocker() =>
        new(
            RestartPolicy: CoturnRuntimePolicy.ExpectedRestartPolicy,
            ExpectedRestartPolicy: CoturnRuntimePolicy.ExpectedRestartPolicy,
            RestartPolicyMatches: true,
            RestartCount: 0,
            Restarting: false,
            Paused: false,
            ExitCode: 0,
            OomKilled: false,
            Dead: false,
            StateErrorPresent: false,
            HealthStatus: null,
            StartedAtUtc: DateTimeOffset.Parse("2026-08-23T00:00:00Z"),
            FinishedAtUtc: null,
            NetworkMode: "default",
            ExpectedNetwork: "mem-gateway",
            NetworkModeMatches: false,
            AttachedNetworks: ["mem-gateway"],
            ExpectedNetworkAliases: ["coturn", "mem-coturn"],
            ObservedExpectedNetworkAliases: ["coturn", "mem-coturn"],
            ExpectedNetworkAttached: true,
            NetworkAliasesMatch: true,
            ConfigMountPresent: true,
            ConfigMountReadOnly: true,
            ConfigMountSourceMatches: true,
            ConfigMountDestinationMatches: true,
            CommandMatches: true,
            StartupUserMatches: true);
}
