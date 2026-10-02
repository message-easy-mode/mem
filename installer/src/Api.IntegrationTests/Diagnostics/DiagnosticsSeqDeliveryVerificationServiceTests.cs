using System.Security.Claims;
using System.Text.Json;
using Infrastructure.Persistence;
using Api.IntegrationTests.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modules.Auth.Identity;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Services;
using Modules.Shared.RuntimeImages;
using Shared.Diagnostics;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsSeqDeliveryVerificationServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"mem-seq-delivery-verify-{Guid.NewGuid():N}");
    private readonly string _apiKeyVariable = $"MEM_SEQ_API_{Guid.NewGuid():N}";

    [Fact]
    public async Task Active_delivery_verification_emits_through_the_current_logger_and_binds_to_the_process()
    {
        await using var fixture = await CreateFixtureAsync(
            new SeqDeliveryState(true, true, false, Now.AddMinutes(-1), null));

        var result = await fixture.Service.VerifyAsync(
            Principal(),
            CancellationToken.None);

        Assert.Equal("succeeded", result.Status);
        Assert.StartsWith("seq-active-", result.VerificationId);
        Assert.Equal("verified", result.Overview.Delivery.ActivationState);
        Assert.Equal(result.VerificationId, result.Overview.Delivery.LastActivationVerificationId);
        Assert.Contains(
            fixture.Logger.Entries,
            entry => entry.Contains(result.VerificationId, StringComparison.Ordinal) &&
                     entry.Contains(
                         DiagnosticsSeqDeliveryVerificationService.VerificationEventCode,
                         StringComparison.Ordinal));

        var state = Assert.IsType<SeqBootstrapState>(fixture.StateStore.Read().State);
        Assert.Equal(fixture.RuntimeContext.ApiProcessInstanceId, fixture.ProcessIdentity.Value);
        Assert.Equal(fixture.RuntimeContext.ApiProcessInstanceId, state.ActiveDeliveryProcessId);
        Assert.Equal(result.VerificationId, state.LastActiveDeliveryVerificationId);
        Assert.NotNull(state.ActiveDeliveryVerifiedAtUtc);
        Assert.Contains(
            fixture.Writer.Requests,
            request => request.EventCode == "diagnostics.seq_delivery_activation_verified" &&
                       request.Details?["verificationId"] == result.VerificationId);
        var operation = Assert.Single(
            (await fixture.Db.RuntimeOperations.ToListAsync())
                .Where(operation => operation.Operation == "seq.delivery.verify" &&
                                    operation.Status == "succeeded"));
        Assert.NotNull(operation.ResultJson);
        Assert.NotNull(operation.EvidenceJson);
        Assert.Contains(result.VerificationId, operation.ResultJson!, StringComparison.Ordinal);
        using (var evidence = JsonDocument.Parse(operation.EvidenceJson!))
        {
            var root = evidence.RootElement;
            Assert.Equal(
                fixture.RuntimeContext.ApiProcessInstanceId,
                root.GetProperty("apiProcessInstanceId").GetGuid());
            Assert.Equal(
                fixture.RuntimeContext.ControlPlaneInstanceId,
                root.GetProperty("controlPlaneInstanceId").GetGuid());
            Assert.Equal(
                fixture.RuntimeContext.RuntimeMode,
                root.GetProperty("runtimeMode").GetString());
            Assert.Equal(
                fixture.RuntimeContext.EnvironmentName,
                root.GetProperty("environment").GetString());
            Assert.Equal(
                fixture.RuntimeContext.Version,
                root.GetProperty("version").GetString());
            Assert.Equal(
                fixture.RuntimeContext.Commit,
                root.GetProperty("commit").GetString());
        }
        var safeEvent = Assert.Single(
            fixture.Writer.Requests.Where(request =>
                request.EventCode == "diagnostics.seq_delivery_activation_verified"));
        Assert.Equal(
            fixture.RuntimeContext.RuntimeMode,
            safeEvent.Details?["runtimeMode"]);
        Assert.Equal(
            fixture.RuntimeContext.ApiProcessInstanceId.ToString("D"),
            safeEvent.Details?["apiProcessInstanceId"]);
    }

    [Fact]
    public async Task Restart_pending_blocks_verification_before_any_logger_event_is_emitted()
    {
        await using var fixture = await CreateFixtureAsync(
            new SeqDeliveryState(false, true, true, Now, null));

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.VerifyAsync(Principal(), CancellationToken.None));

        Assert.Equal("seq_delivery_restart_required", exception.Code);
        Assert.Empty(fixture.Logger.Entries);
        Assert.DoesNotContain(
            await fixture.Db.RuntimeOperations.ToListAsync(),
            operation => operation.Operation == "seq.delivery.verify" &&
                         operation.Status == "succeeded");
    }

    [Fact]
    public async Task Information_level_logging_must_be_enabled_before_active_delivery_is_recorded()
    {
        await using var fixture = await CreateFixtureAsync(
            new SeqDeliveryState(true, true, false, Now, null),
            loggerEnabled: false);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.VerifyAsync(Principal(), CancellationToken.None));

        Assert.Equal("seq_delivery_information_logging_disabled", exception.Code);
        Assert.Empty(fixture.Logger.Entries);
        var state = Assert.IsType<SeqBootstrapState>(fixture.StateStore.Read().State);
        Assert.Null(state.ActiveDeliveryProcessId);
        var failedOperation = Assert.Single(
            (await fixture.Db.RuntimeOperations.ToListAsync())
                .Where(operation => operation.Operation == "seq.delivery.verify" &&
                                    operation.Status == "failed" &&
                                    operation.LastError == "seq_delivery_information_logging_disabled"));
        Assert.NotNull(failedOperation.EvidenceJson);
        using var evidence = JsonDocument.Parse(failedOperation.EvidenceJson!);
        Assert.Equal(
            fixture.RuntimeContext.ApiProcessInstanceId,
            evidence.RootElement.GetProperty("apiProcessInstanceId").GetGuid());
        Assert.Equal(
            fixture.RuntimeContext.RuntimeMode,
            evidence.RootElement.GetProperty("runtimeMode").GetString());
    }


    [Fact]
    public async Task Logging_bootstrap_must_have_attached_the_seq_sink_before_verification()
    {
        await using var fixture = await CreateFixtureAsync(
            new SeqDeliveryState(true, true, false, Now, null),
            sinkConfigured: false);

        var exception = await Assert.ThrowsAsync<SeqOperationException>(() =>
            fixture.Service.VerifyAsync(Principal(), CancellationToken.None));

        Assert.Equal("diagnostics.seq_sink_configuration_failed", exception.Code);
        Assert.Empty(fixture.Logger.Entries);
        var state = Assert.IsType<SeqBootstrapState>(fixture.StateStore.Read().State);
        Assert.Null(state.ActiveDeliveryProcessId);
    }

    private async Task<Fixture> CreateFixtureAsync(
        SeqDeliveryState delivery,
        bool loggerEnabled = true,
        bool sinkConfigured = true)
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(_apiKeyVariable, "seq-api-key-test");
        var environment = new TestHostEnvironment(_root);
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = delivery.EffectiveEnabled,
            ManagementEnabled = true,
            EulaAccepted = true,
            IngestionUrl = "http://127.0.0.1:25341/",
            HealthUrl = "http://127.0.0.1:25341/",
            ApiKeyEnvironmentVariableName = _apiKeyVariable,
            ApiKeyFilePath = null,
            AdminPasswordHashEnvironmentVariableName = string.Empty,
            AdminPasswordHashFilePath = Path.Combine(_root, "admin-hash"),
            HostDataPath = Path.Combine(_root, "seq-data"),
            BootstrapStatePath = Path.Combine(_root, "seq-bootstrap.json"),
            DeliveryStatePath = Path.Combine(_root, "seq-delivery.json")
        };
        var secrets = new SeqSecretResolver(environment);
        var stateStore = new SeqBootstrapStateStore(options, environment);
        await stateStore.WriteAsync(
            new SeqBootstrapState(
                ManagementEnabled: true,
                EulaAccepted: true,
                SelectedHostPort: 25341,
                SetupStage: "connected",
                RuntimeVerifiedAtUtc: Now.AddMinutes(-5),
                IngestionCredentialState: "available",
                IngestionCredentialId: "api-key-mem",
                DeliveryVerifiedAtUtc: Now.AddMinutes(-4),
                LastDeliveryVerificationId: "seq-connect-proof",
                LastDeliveryVerificationEventId: "event-connect-proof"),
            CancellationToken.None);

        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options);
        await db.Database.EnsureCreatedAsync();

        var runtime = new FixedRuntimeStatusReader(new SeqStatusResponse(
            ServiceName: "seq",
            ContainerName: "mem-seq",
            ExpectedVersion: options.ExpectedVersion,
            HostDataPath: options.HostDataPath,
            UiHostPort: 25341,
            Exists: true,
            Running: true,
            State: "running",
            Image: "sha256:approved-seq",
            UsesApprovedRuntime: true,
            Warnings: [],
            Managed: true,
            OwnershipState: "managed",
            WarningCode: null));
        var healthState = new SeqHealthState(options, secrets);
        healthState.RecordReady(Now);
        var deliveryStore = new FixedDeliveryStateStore(delivery);
        var runtimeContext = TestRuntimeContext.Create(
            _root,
            MemRuntimeModes.LocalDevelopment,
            uiDeliveryMode: MemUiDeliveryModes.Vite,
            version: "0.2.0-seq-test",
            commit: "seq-runtime-context-test");
        var processIdentity = SeqDeliveryProcessIdentity.FromRuntimeContext(runtimeContext);
        var effective = new SeqEffectiveConfigurationProvider(options, stateStore);
        var diagnostics = new DiagnosticsSeqService(
            options,
            secrets,
            healthState,
            runtime,
            new FixedImageInspector(),
            new FixedTimeProvider(Now),
            deliveryStore,
            effective,
            environment,
            stateStore,
            securitySettings: null,
            deliveryProcessIdentity: processIdentity,
            runtimeContext: runtimeContext);
        var writer = new RecordingWriter();
        var logger = new CapturingLogger<DiagnosticsSeqDeliveryVerificationService>(
            loggerEnabled);
        var service = new DiagnosticsSeqDeliveryVerificationService(
            deliveryStore,
            effective,
            secrets,
            runtime,
            new PassingHealthVerifier(),
            stateStore,
            processIdentity,
            new SeqLoggingRuntimeState(
                SinkConfigured: sinkConfigured,
                WarningCode: sinkConfigured
                    ? null
                    : "diagnostics.seq_sink_configuration_failed"),
            diagnostics,
            db,
            writer,
            runtimeContext,
            new FixedTimeProvider(Now),
            logger);

        return new Fixture(
            connection,
            db,
            stateStore,
            processIdentity,
            runtimeContext,
            writer,
            logger,
            service);
    }

    private static ClaimsPrincipal Principal() => new(
        new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Name, "owner"),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        "test"));

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_apiKeyVariable, null);
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record Fixture(
        SqliteConnection Connection,
        MemDbContext Db,
        ISeqBootstrapStateStore StateStore,
        SeqDeliveryProcessIdentity ProcessIdentity,
        MemControlPlaneRuntimeContext RuntimeContext,
        RecordingWriter Writer,
        CapturingLogger<DiagnosticsSeqDeliveryVerificationService> Logger,
        DiagnosticsSeqDeliveryVerificationService Service) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FixedDeliveryStateStore(SeqDeliveryState state)
        : ISeqDeliveryStateStore
    {
        public SeqDeliveryState GetState() => state;

        public Task<SeqDeliveryState> SetDesiredAsync(
            bool enabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedRuntimeStatusReader(SeqStatusResponse status)
        : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(status);
    }

    private sealed class PassingHealthVerifier : ISeqRuntimeHealthVerifier
    {
        public Task<SeqRuntimeHealthVerification> VerifyAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new SeqRuntimeHealthVerification(
                true,
                "healthy",
                Now,
                null));
    }

    private sealed class FixedImageInspector : IRuntimeImageInspector
    {
        public Task<RuntimeImageInspection?> InspectAsync(
            string imageReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeImageInspection?>(new RuntimeImageInspection(
                "sha256:" + new string('a', 64),
                [$"datalust/seq@sha256:{new string('b', 64)}"],
                []));

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                true,
                "evt-delivery-verify",
                null,
                null));
        }
    }

    private sealed class CapturingLogger<T>(bool enabled = true) : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => enabled;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
