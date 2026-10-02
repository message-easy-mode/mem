using HostAgent.Runtime.Coturn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Tests.Runtime.Coturn;

public sealed class CoturnStartupSupervisionHostedServiceTests
{
    [Fact]
    public async Task Current_process_operator_overlap_is_retried_and_resumes_in_same_api_process()
    {
        var runtimeContext = CreateRuntimeContext();
        var state = new CoturnStartupSupervisionState(runtimeContext, TimeProvider.System);
        var runtime = new ReadyStartupRuntime();
        var processor = new SequencedProcessor(
        [
            CoturnStartupSupervisionExecutionOutcome.DeferredCurrentProcessMutation,
            CoturnStartupSupervisionExecutionOutcome.Finished
        ]);
        var scopeFactory = new TestScopeFactory(new Dictionary<Type, object>
        {
            [typeof(ICoturnStartupSupervisionRuntime)] = runtime,
            [typeof(ICoturnStartupSupervisionProcessor)] = processor
        });
        var service = new CoturnStartupSupervisionHostedService(
            scopeFactory,
            runtimeContext,
            state,
            TimeProvider.System,
            NullLogger<CoturnStartupSupervisionHostedService>.Instance,
            operatorMutationResumeWindow: TimeSpan.FromSeconds(1),
            operatorMutationRetryDelay: TimeSpan.FromMilliseconds(10));

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(2, processor.Calls);
        Assert.Equal(1, runtime.InspectCalls);
    }

    [Fact]
    public async Task Not_expected_deferral_is_rechecked_and_resumes_after_coturn_becomes_expected()
    {
        var runtimeContext = CreateRuntimeContext();
        var state = new CoturnStartupSupervisionState(runtimeContext, TimeProvider.System);
        var runtime = new ReadyStartupRuntime();
        var processor = new SequencedProcessor(
        [
            CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected,
            CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected,
            CoturnStartupSupervisionExecutionOutcome.Finished
        ]);
        var scopeFactory = new TestScopeFactory(new Dictionary<Type, object>
        {
            [typeof(ICoturnStartupSupervisionRuntime)] = runtime,
            [typeof(ICoturnStartupSupervisionProcessor)] = processor
        });
        var service = new CoturnStartupSupervisionHostedService(
            scopeFactory,
            runtimeContext,
            state,
            TimeProvider.System,
            NullLogger<CoturnStartupSupervisionHostedService>.Instance,
            operatorMutationResumeWindow: TimeSpan.FromSeconds(1),
            operatorMutationRetryDelay: TimeSpan.FromMilliseconds(10),
            eligibilityRetryDelay: TimeSpan.FromMilliseconds(10));

        await service.RunAsync(CancellationToken.None);

        Assert.Equal(3, processor.Calls);
        Assert.Equal(1, runtime.InspectCalls);
    }

    [Fact]
    public async Task Shutdown_while_waiting_for_first_time_setup_eligibility_exits_without_failure()
    {
        var runtimeContext = CreateRuntimeContext();
        var state = new CoturnStartupSupervisionState(runtimeContext, TimeProvider.System);
        var runtime = new ReadyStartupRuntime();
        using var stopping = new CancellationTokenSource();
        var processor = new SequencedProcessor(
        [
            CoturnStartupSupervisionExecutionOutcome.DeferredNotExpected
        ],
        onCall: call =>
        {
            if (call == 1)
            {
                stopping.Cancel();
            }
        });
        var scopeFactory = new TestScopeFactory(new Dictionary<Type, object>
        {
            [typeof(ICoturnStartupSupervisionRuntime)] = runtime,
            [typeof(ICoturnStartupSupervisionProcessor)] = processor
        });
        var service = new CoturnStartupSupervisionHostedService(
            scopeFactory,
            runtimeContext,
            state,
            TimeProvider.System,
            NullLogger<CoturnStartupSupervisionHostedService>.Instance,
            operatorMutationResumeWindow: TimeSpan.FromSeconds(1),
            operatorMutationRetryDelay: TimeSpan.FromMilliseconds(10),
            eligibilityRetryDelay: TimeSpan.FromMilliseconds(10));

        await service.RunAsync(stopping.Token);

        Assert.Equal(1, processor.Calls);
        Assert.NotEqual(CoturnStartupSupervisionStatuses.Failed, state.Read().Status);
    }

    [Fact]
    public async Task Shutdown_while_waiting_for_operator_operation_exits_without_another_supervision_pass()
    {
        var runtimeContext = CreateRuntimeContext();
        var state = new CoturnStartupSupervisionState(runtimeContext, TimeProvider.System);
        var runtime = new ReadyStartupRuntime();
        using var stopping = new CancellationTokenSource();
        var processor = new SequencedProcessor(
        [
            CoturnStartupSupervisionExecutionOutcome.DeferredCurrentProcessMutation,
            CoturnStartupSupervisionExecutionOutcome.Finished
        ],
        onCall: call =>
        {
            if (call == 1)
            {
                stopping.Cancel();
            }
        });
        var scopeFactory = new TestScopeFactory(new Dictionary<Type, object>
        {
            [typeof(ICoturnStartupSupervisionRuntime)] = runtime,
            [typeof(ICoturnStartupSupervisionProcessor)] = processor
        });
        var service = new CoturnStartupSupervisionHostedService(
            scopeFactory,
            runtimeContext,
            state,
            TimeProvider.System,
            NullLogger<CoturnStartupSupervisionHostedService>.Instance,
            operatorMutationResumeWindow: TimeSpan.FromSeconds(1),
            operatorMutationRetryDelay: TimeSpan.FromMilliseconds(10));

        await service.RunAsync(stopping.Token);

        Assert.Equal(1, processor.Calls);
        Assert.NotEqual(CoturnStartupSupervisionStatuses.Failed, state.Read().Status);
    }

    private sealed class SequencedProcessor(
        IEnumerable<CoturnStartupSupervisionExecutionOutcome> outcomes,
        Action<int>? onCall = null) : ICoturnStartupSupervisionProcessor
    {
        private readonly Queue<CoturnStartupSupervisionExecutionOutcome> _outcomes =
            new(outcomes);

        public int Calls { get; private set; }

        public Task<CoturnStartupSupervisionExecutionOutcome> ExecuteAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            onCall?.Invoke(Calls);
            if (_outcomes.Count == 0)
            {
                throw new InvalidOperationException("No fake startup-supervision outcome remains.");
            }

            return Task.FromResult(_outcomes.Dequeue());
        }
    }

    private sealed class ReadyStartupRuntime : ICoturnStartupSupervisionRuntime
    {
        public int InspectCalls { get; private set; }

        public Task<CoturnRuntimeResponse> InspectAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCalls++;
            return Task.FromResult(ReadyRuntime());
        }

        public Task<CoturnStartupRecoveryMutationResult> RecoverStartupAsync(
            string decision,
            string expectedContainerId,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Hosted-service readiness must not mutate Coturn.");

        public Task<CoturnRuntimeResponse> RestartOwnedAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Hosted-service readiness must not restart Coturn.");

        public Task<CoturnCheckResponse> CheckAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Hosted-service readiness must not run a functional check.");

        public Task<CoturnLogsResponse> GetRecentLogsAsync(
            int? tail,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Hosted-service readiness must not read Coturn logs.");
    }

    private sealed class TestScopeFactory(IReadOnlyDictionary<Type, object> services) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new TestScope(new TestServiceProvider(services));
    }

    private sealed class TestScope(IServiceProvider serviceProvider) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestServiceProvider(IReadOnlyDictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            services.TryGetValue(serviceType, out var service)
                ? service
                : null;
    }

    private static MemControlPlaneRuntimeContext CreateRuntimeContext() =>
        new(
            SchemaVersion: 1,
            RuntimeMode: MemRuntimeModes.ContainerizedDevelopment,
            ControlPlaneInstanceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            ApiProcessInstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            EnvironmentName: "Development",
            RunningInContainer: true,
            ContentRootPath: "/app",
            ContentRootKind: "container-image",
            StateRootPath: "/data",
            StateRootKind: "docker-volume",
            StateRootProfile: MemStateRootProfiles.Default,
            UiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa,
            DockerEndpoint: new Uri("unix:///var/run/docker.sock"),
            DockerEndpointKind: "unix-socket",
            ConfiguredContainerName: "mem-control-plane-dev",
            ApplicationName: "mem-control-plane",
            Version: "0.2.0",
            Commit: "test",
            ValidationState: MemRuntimeValidationStates.Valid,
            MutationsAllowed: true,
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
