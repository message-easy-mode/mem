using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Mem.Cli.Clients;
using Mem.Cli.Config;

namespace Mem.Cli.Tests.Clients;

public sealed class CliDeviceSessionClientTests
{
    private const string DeviceCredential =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

    [Fact]
    public async Task Get_current_session_sends_bearer_credential_without_cookies()
    {
        var handler = new RecordingSessionHandler(
            Json("""
            {
              "status": "authenticated",
              "displayName": "admin",
              "roles": [ "platform_owner", "operator" ],
              "idleExpiresAtUtc": "2026-07-07T15:08:54.3255056+00:00",
              "absoluteExpiresAtUtc": "2026-07-14T07:08:54.3255056+00:00"
            }
            """));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.GetCurrentAsync(
            DeviceCredential);

        Assert.Equal("authenticated", result.Status);
        Assert.Equal("admin", result.DisplayName);
        Assert.Equal(new[] { "operator", "platform_owner" }, result.Roles);
        Assert.Equal("GET", handler.Method);
        Assert.Equal(
            "/api/auth/cli-device/session",
            handler.PathAndQuery);
        Assert.Equal(
            "Bearer",
            handler.AuthorizationScheme);
        Assert.Equal(
            DeviceCredential,
            handler.AuthorizationParameter);
        Assert.False(handler.HasCookieHeader);
    }

    [Fact]
    public async Task Get_current_session_treats_unauthorized_as_unauthenticated()
    {
        var handler = new RecordingSessionHandler(
            Json(
                """{ "status": "unauthenticated" }""",
                HttpStatusCode.Unauthorized));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.GetCurrentAsync(
            DeviceCredential);

        Assert.Equal("unauthenticated", result.Status);
    }

    [Fact]
    public async Task Get_current_session_rejects_invalid_local_credential_without_network()
    {
        var handler = new RecordingSessionHandler(
            Json("""{ "status": "authenticated" }"""));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.GetCurrentAsync(
            "not-a-valid-credential");

        Assert.Equal("unauthenticated", result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Revoke_current_session_sends_delete_bearer_credential_without_cookies()
    {
        var handler = new RecordingSessionHandler(
            Json("""
            {
              "status": "revoked",
              "revokedAtUtc": "2026-07-07T20:08:54.3255056+00:00"
            }
            """));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.RevokeCurrentAsync(
            DeviceCredential);

        Assert.Equal("revoked", result.Status);
        Assert.NotNull(result.RevokedAtUtc);
        Assert.Equal("DELETE", handler.Method);
        Assert.Equal(
            "/api/auth/cli-device/session",
            handler.PathAndQuery);
        Assert.Equal(
            "Bearer",
            handler.AuthorizationScheme);
        Assert.Equal(
            DeviceCredential,
            handler.AuthorizationParameter);
        Assert.False(handler.HasCookieHeader);
    }

    [Fact]
    public async Task Revoke_current_session_treats_unauthorized_as_unauthenticated()
    {
        var handler = new RecordingSessionHandler(
            Json(
                """{ "status": "auth_required" }""",
                HttpStatusCode.Unauthorized));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.RevokeCurrentAsync(
            DeviceCredential);

        Assert.Equal("unauthenticated", result.Status);
    }

    [Fact]
    public async Task Revoke_current_session_rejects_invalid_local_credential_without_network()
    {
        var handler = new RecordingSessionHandler(
            Json("""{ "status": "revoked" }"""));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.RevokeCurrentAsync(
            "not-a-valid-credential");

        Assert.Equal("unauthenticated", result.Status);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Get_current_session_normalizes_unexpected_success_payloads_to_unavailable()
    {
        var handler = new RecordingSessionHandler(
            Json("""{ "status": "surprising", "details": "unsafe" }"""));
        using var client = new CliDeviceSessionClient(
            CreateOptions(),
            handler);

        var result = await client.GetCurrentAsync(
            DeviceCredential);

        Assert.Equal("unavailable", result.Status);
    }

    private static CliOptions CreateOptions() =>
        new(
            HostAgentUrl: "https://mem.example.internal",
            InstallerToken: null,
            Json: false,
            ProfileName: "home");

    private static HttpResponseMessage Json(
        string content,
        HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class RecordingSessionHandler(
        HttpResponseMessage response)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public bool HasCookieHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method.Method;
            PathAndQuery = request.RequestUri?.PathAndQuery;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            HasCookieHeader = request.Headers.Contains("Cookie");

            return Task.FromResult(response);
        }
    }
}
