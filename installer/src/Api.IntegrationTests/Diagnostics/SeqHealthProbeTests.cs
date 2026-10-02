using System.Net;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Integrations.Seq.Services;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class SeqHealthProbeTests
{
    [Fact]
    public async Task Successful_probe_marks_seq_ready_and_records_one_transition()
    {
        await using var fixture = new ProbeFixture(HttpStatusCode.OK);

        await fixture.Service.ProbeOnceAsync(CancellationToken.None);

        var health = fixture.State.GetHealth();
        Assert.Equal("ready", health.Status);
        Assert.True(health.Reachable);
        Assert.NotNull(health.LastSuccessAtUtc);
        Assert.Contains(fixture.Writer.Requests, request => request.EventCode == "logging.seq_ready");
    }

    [Fact]
    public async Task Failed_probe_degrades_seq_without_affecting_local_diagnostics()
    {
        await using var fixture = new ProbeFixture(HttpStatusCode.ServiceUnavailable);

        await fixture.Service.ProbeOnceAsync(CancellationToken.None);

        var health = fixture.State.GetHealth();
        Assert.Equal("unavailable", health.Status);
        Assert.False(health.Reachable);
        Assert.Equal("diagnostics.seq_health_failed", health.WarningCode);
        Assert.Contains(fixture.Writer.Requests, request => request.EventCode == "logging.seq_unavailable");
    }

    [Fact]
    public async Task Timed_out_probe_is_bounded_and_reported_as_optional_unavailable()
    {
        await using var fixture = new ProbeFixture(
            HttpStatusCode.OK,
            delayUntilCancelled: true);

        await fixture.Service.ProbeOnceAsync(CancellationToken.None);

        var health = fixture.State.GetHealth();
        Assert.Equal("unavailable", health.Status);
        Assert.Equal("diagnostics.seq_probe_timeout", health.WarningCode);
    }

    private sealed class ProbeFixture : IAsyncDisposable
    {
        private readonly string _apiVariable;

        public ProbeFixture(
            HttpStatusCode statusCode,
            bool delayUntilCancelled = false)
        {
            _apiVariable = $"MEM_SEQ_API_KEY_{Guid.NewGuid():N}";
            Environment.SetEnvironmentVariable(_apiVariable, "test-api-key");
            var options = new SeqDiagnosticsOptions
            {
                SinkEnabled = true,
                IngestionUrl = "http://seq:5341",
                HealthUrl = "http://seq:80",
                ApiKeyEnvironmentVariableName = _apiVariable,
                ProbeTimeoutSeconds = 1,
                ProbeIntervalSeconds = 30
            };
            var secrets = new SeqSecretResolver(new TestHostEnvironment());
            State = new SeqHealthState(options, secrets);
            Writer = new RecordingWriter();
            var handler = new StubHandler(statusCode, delayUntilCancelled);
            Service = new SeqHealthProbeService(
                new StubHttpClientFactory(handler),
                options,
                State,
                Writer,
                TimeProvider.System,
                NullLogger<SeqHealthProbeService>.Instance);
        }

        public SeqHealthState State { get; }
        public RecordingWriter Writer { get; }
        public SeqHealthProbeService Service { get; }

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable(_apiVariable, null);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHandler(
        HttpStatusCode statusCode,
        bool delayUntilCancelled) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal("http://seq/health", request.RequestUri?.ToString());
            if (delayUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(statusCode);
        }
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
                $"evt_{Guid.NewGuid():N}",
                null,
                null));
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
