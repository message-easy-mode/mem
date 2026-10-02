using System.Net;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Integrations.Seq.Contracts;
using Modules.Integrations.Seq.Services;
using Api.IntegrationTests.Runtime;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqRuntimeHealthVerifierTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 4, 1, 15, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_probe_marks_the_runtime_ready_without_requiring_delivery()
    {
        var fixture = CreateFixture(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await fixture.Verifier.VerifyAsync(CancellationToken.None);
        var health = fixture.State.GetHealth();

        Assert.True(result.Passed);
        Assert.Equal("healthy", result.Status);
        Assert.Null(result.WarningCode);
        Assert.Equal("ready", health.Status);
        Assert.True(health.Reachable);
        Assert.Equal(Now, health.LastCheckedAtUtc);
        Assert.Equal(Now, health.LastSuccessAtUtc);
    }

    [Fact]
    public async Task Managed_runtime_is_probed_through_its_server_authored_loopback_port()
    {
        Uri? requestedUri = null;
        var fixture = CreateFixture(
            request =>
            {
                requestedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            ManagedStatus(uiHostPort: 15341));

        var result = await fixture.Verifier.VerifyAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal("http://127.0.0.1:15341/health", requestedUri?.ToString());
    }

    [Theory]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment, "seq-dev")]
    [InlineData(MemRuntimeModes.ContainerizedProduction, "seq")]
    public async Task Containerized_runtime_is_probed_through_the_context_owned_Docker_network_authority(
        string runtimeMode,
        string expectedHost)
    {
        Uri? requestedUri = null;
        var fixture = CreateFixture(
            request =>
            {
                requestedUri = request.RequestUri;
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            ManagedStatus(uiHostPort: 15341),
            runtimeMode: runtimeMode);

        var result = await fixture.Verifier.VerifyAsync(CancellationToken.None);

        Assert.True(result.Passed);
        Assert.Equal(expectedHost, requestedUri?.Host);
        Assert.Equal(80, requestedUri?.Port);
        Assert.Equal("/health", requestedUri?.AbsolutePath);
    }

    [Fact]
    public async Task Managed_runtime_without_a_published_port_fails_closed()
    {
        var requests = 0;
        var fixture = CreateFixture(
            _ =>
            {
                requests++;
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            ManagedStatus(uiHostPort: null));

        var result = await fixture.Verifier.VerifyAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("invalid", result.Status);
        Assert.Equal("diagnostics.seq_health_url_invalid", result.WarningCode);
        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Non_success_response_is_bounded_and_records_a_safe_warning_code()
    {
        var fixture = CreateFixture(_ =>
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await fixture.Verifier.VerifyAsync(CancellationToken.None);
        var health = fixture.State.GetHealth();

        Assert.False(result.Passed);
        Assert.Equal("failed", result.Status);
        Assert.Equal("diagnostics.seq_health_failed", result.WarningCode);
        Assert.Equal("unavailable", health.Status);
        Assert.False(health.Reachable);
        Assert.Equal(result.WarningCode, health.WarningCode);
    }

    [Fact]
    public async Task Probe_timeout_is_reported_without_exposing_an_exception()
    {
        var fixture = CreateFixture(_ => throw new OperationCanceledException());

        var result = await fixture.Verifier.VerifyAsync(CancellationToken.None);

        Assert.False(result.Passed);
        Assert.Equal("timeout", result.Status);
        Assert.Equal("diagnostics.seq_probe_timeout", result.WarningCode);
        Assert.Equal(
            "diagnostics.seq_probe_timeout",
            fixture.State.GetHealth().WarningCode);
    }

    private static Fixture CreateFixture(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory,
        SeqStatusResponse? runtimeStatus = null,
        string runtimeMode = MemRuntimeModes.LocalDevelopment)
    {
        var options = new SeqDiagnosticsOptions
        {
            SinkEnabled = false,
            ManagementEnabled = true,
            HealthUrl = "http://seq:80",
            ProbeTimeoutSeconds = 5
        };
        var environment = new TestHostEnvironment();
        var state = new SeqHealthState(
            options,
            new SeqSecretResolver(environment));
        var client = new HttpClient(new DelegateHandler(responseFactory));
        var runtimeRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-seq-health-runtime-{Guid.NewGuid():N}");
        var containerized = MemRuntimeModes.IsContainerized(runtimeMode);
        var runtimeContext = TestRuntimeContext.Create(
            runtimeRoot,
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
                : MemUiDeliveryModes.Vite);
        var verifier = new SeqRuntimeHealthVerifier(
            new SingleClientFactory(client),
            options,
            state,
            new FixedRuntimeStatusReader(runtimeStatus ?? ManagedStatus(uiHostPort: 15341)),
            new MemManagedServiceAuthorityResolver(runtimeContext),
            new FixedTimeProvider(Now),
            SeqRuntimeContextProfile.Create(runtimeContext, options));
        return new Fixture(verifier, state, client);
    }

    private static SeqStatusResponse ManagedStatus(int? uiHostPort) => new(
        ServiceName: "seq",
        ContainerName: "mem-seq",
        ExpectedVersion: "2026.1.17044",
        HostDataPath: "/data/seq",
        UiHostPort: uiHostPort,
        Exists: true,
        Running: true,
        State: "running",
        Image: "sha256:seq",
        UsesApprovedRuntime: true,
        Warnings: [],
        Managed: true,
        OwnershipState: "managed",
        WarningCode: null);

    private sealed record Fixture(
        SeqRuntimeHealthVerifier Verifier,
        SeqHealthState State,
        HttpClient Client);

    private sealed class FixedRuntimeStatusReader(SeqStatusResponse status)
        : ISeqRuntimeStatusReader
    {
        public Task<SeqStatusResponse> GetStatusAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(status);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
