using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Config;

namespace Mem.Cli.Tests.Clients;

public sealed class CliDeviceAuthorizationClientTests
{
    private const string Verifier =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    private const string DeviceCredential =
        "ICEiIyQlJicoKSorLC0uLzAxMjM0NTY3ODk6Ozw9Pj8";

    [Fact]
    public async Task Start_posts_only_the_verifier_challenge_and_device_label_without_browser_or_legacy_auth_headers()
    {
        var authorizationId = Guid.NewGuid();
        var handler = new RecordingHandler(
            RecordingHandler.Json(
                $$"""
                {
                  "status": "authorization_started",
                  "authorizationId": "{{authorizationId:D}}",
                  "userCode": "ABCD-EFGH",
                  "expiresAtUtc": "2030-01-02T03:04:05+00:00",
                  "browserApprovalUrl": "https://mem.example.internal/cli/authorize"
                }
                """));

        using var client = new CliDeviceAuthorizationClient(
            CreateOptions(),
            handler);

        var result = await client.StartAsync(
            "challenge-value",
            "MEM CLI");

        Assert.Equal("authorization_started", result.Status);
        Assert.Equal(authorizationId, result.AuthorizationId);
        Assert.Equal("ABCD-EFGH", result.UserCode);
        Assert.Equal(
            "https://mem.example.internal/cli/authorize",
            result.BrowserApprovalUrl);

        var request = Assert.Single(handler.Requests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "/api/auth/cli-device/authorizations",
            request.PathAndQuery);
        Assert.False(request.HasAuthorizationHeader);
        Assert.False(request.HasCookieHeader);
        Assert.False(request.HasRetiredSharedSecretHeader);

        using var document = JsonDocument.Parse(
            request.ContentBody!);

        Assert.Equal(
            "challenge-value",
            document.RootElement
                .GetProperty("verifierChallenge")
                .GetString());
        Assert.Equal(
            "MEM CLI",
            document.RootElement
                .GetProperty("deviceLabel")
                .GetString());
        Assert.Equal(
            2,
            document.RootElement
                .EnumerateObject()
                .Count());
    }

    [Fact]
    public async Task Poll_posts_only_the_in_memory_verifier_and_returns_the_approved_credential_to_the_command_layer()
    {
        var authorizationId = Guid.NewGuid();
        var handler = new RecordingHandler(
            RecordingHandler.Json(
                $$"""
                {
                  "status": "authorized",
                  "deviceCredential": "{{DeviceCredential}}",
                  "idleExpiresAtUtc": "2030-01-02T03:04:05+00:00",
                  "absoluteExpiresAtUtc": "2030-01-09T03:04:05+00:00"
                }
                """));

        using var client = new CliDeviceAuthorizationClient(
            CreateOptions(),
            handler);

        var result = await client.PollAsync(
            authorizationId,
            Verifier);

        Assert.Equal("authorized", result.Status);
        Assert.Equal(DeviceCredential, result.DeviceCredential);
        Assert.NotNull(result.IdleExpiresAtUtc);
        Assert.NotNull(result.AbsoluteExpiresAtUtc);

        var request = Assert.Single(handler.Requests);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            $"/api/auth/cli-device/authorizations/{authorizationId:D}/poll",
            request.PathAndQuery);
        Assert.False(request.HasAuthorizationHeader);
        Assert.False(request.HasCookieHeader);
        Assert.False(request.HasRetiredSharedSecretHeader);

        using var document = JsonDocument.Parse(
            request.ContentBody!);

        Assert.Equal(
            Verifier,
            document.RootElement
                .GetProperty("verifier")
                .GetString());
        Assert.Equal(
            1,
            document.RootElement
                .EnumerateObject()
                .Count());
    }

    [Fact]
    public async Task Start_maps_a_rate_limit_response_without_returning_server_detail()
    {
        var response = RecordingHandler.Json(
            """{ "status": "rate_limited", "detail": "do-not-return-this" }""",
            HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(
            TimeSpan.FromSeconds(7));

        var handler = new RecordingHandler(response);

        using var client = new CliDeviceAuthorizationClient(
            CreateOptions(),
            handler);

        var result = await client.StartAsync(
            "challenge-value",
            "MEM CLI");

        Assert.Equal("rate_limited", result.Status);
        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
        Assert.Null(result.UserCode);
        Assert.Null(result.BrowserApprovalUrl);
    }

    [Fact]
    public async Task Poll_maps_unrecognised_error_output_to_a_safe_unavailable_status()
    {
        var handler = new RecordingHandler(
            RecordingHandler.Json(
                """{ "status": "untrusted_error", "detail": "secret" }""",
                HttpStatusCode.InternalServerError));

        using var client = new CliDeviceAuthorizationClient(
            CreateOptions(),
            handler);

        var result = await client.PollAsync(
            Guid.NewGuid(),
            Verifier);

        Assert.Equal("authorization_unavailable", result.Status);
        Assert.Null(result.DeviceCredential);
        Assert.Null(result.RetryAfter);
    }

    private static CliOptions CreateOptions() =>
        new(
            HostAgentUrl: "https://mem.example.internal",
            InstallerToken: null,
            Json: false,
            ProfileName: "home");

    private sealed class RecordingHandler(
        params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses =
            new(responses);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(
                new RecordedRequest(
                    request.Method,
                    request.RequestUri?.PathAndQuery ?? string.Empty,
                    request.Headers.Authorization is not null,
                    request.Headers.Contains("Cookie"),
                    request.Headers.Contains("X-MEM-Agent-Secret"),
                    request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(
                            cancellationToken)));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scripted HTTP response was configured.");
            }

            return _responses.Dequeue();
        }

        public static HttpResponseMessage Json(
            string json,
            HttpStatusCode statusCode = HttpStatusCode.OK) =>
            new(statusCode)
            {
                Content = new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
            };
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string PathAndQuery,
        bool HasAuthorizationHeader,
        bool HasCookieHeader,
        bool HasRetiredSharedSecretHeader,
        string? ContentBody);
}
