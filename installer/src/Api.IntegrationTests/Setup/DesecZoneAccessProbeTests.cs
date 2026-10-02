using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.Domains.Dns;

namespace Api.IntegrationTests.Setup;

public sealed class DesecZoneAccessProbeTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_desec_zone_probe_performs_one_read_only_GET_and_returns_no_token()
    {
        const string token = "desec-zone-probe-secret-token";
        var handler = new RecordingHttpHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://desec.io/api/v1/")
        };
        var provider = new DesecDnsChallengeProvider(
            httpClient,
            NullLogger<DesecDnsChallengeProvider>.Instance);

        var result = await provider.ProbeAsync(
            new DnsZoneAccessProbeRequest("example.com", token),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(200, result.ProviderStatusCode);
        Assert.Single(handler.Requests);
        var request = handler.Requests[0];
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            "https://desec.io/api/v1/domains/example.com/rrsets/",
            request.Uri);
        Assert.Equal($"Token {token}", request.Authorization);
        Assert.Null(request.Body);

        var safeProjection = string.Join(
            "|",
            result.Evidence.Select(item => $"{item.Key}:{item.Value}:{item.Status}"));
        Assert.DoesNotContain(token, result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token, safeProjection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_desec_zone_probe_classifies_auth_failure_without_returning_token()
    {
        const string token = "desec-invalid-secret-token";
        var handler = new RecordingHttpHandler(
            new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    $"{{\"detail\":\"Rejected token {token}\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://desec.io/api/v1/")
        };
        var provider = new DesecDnsChallengeProvider(
            httpClient,
            NullLogger<DesecDnsChallengeProvider>.Instance);

        var result = await provider.ProbeAsync(
            new DnsZoneAccessProbeRequest("example.com", token),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorCode);
        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);

        var safeProjection = result.Message + "|" + string.Join(
            "|",
            result.Evidence.Select(item => $"{item.Key}:{item.Value}:{item.Status}"));
        Assert.DoesNotContain(token, safeProjection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_CORR_01_desec_zone_probe_logs_safe_404_classification_without_token()
    {
        const string token = "desec-zone-not-found-secret-token";
        var handler = new RecordingHttpHandler(
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    $"{{\"detail\":\"provider body mentions {token}\"}}",
                    Encoding.UTF8,
                    "application/json")
            });
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://desec.io/api/v1/")
        };
        var logger = new CapturingLogger<DesecDnsChallengeProvider>();
        var provider = new DesecDnsChallengeProvider(httpClient, logger);

        var result = await provider.ProbeAsync(
            new DnsZoneAccessProbeRequest("wrong.example", token),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("DesecZoneNotAccessible", result.ErrorCode);
        Assert.Equal(404, result.ProviderStatusCode);
        Assert.Contains(
            "could not find DNS zone 'wrong.example'",
            result.Message,
            StringComparison.OrdinalIgnoreCase);

        var logText = string.Join("\n", logger.Entries.Select(entry => entry.Message));
        Assert.Contains("Zone=wrong.example", logText, StringComparison.Ordinal);
        Assert.Contains("HttpStatus=404", logText, StringComparison.Ordinal);
        Assert.Contains("FailureClass=DesecZoneNotAccessible", logText, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logText, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result.Message, StringComparison.Ordinal);
    }

    private sealed class RecordingHttpHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.TryGetValues("Authorization", out var values)
                    ? values.Single()
                    : null,
                body));
            return response;
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? Authorization,
        string? Body);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(
                logLevel,
                formatter(state, exception),
                exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);

}
