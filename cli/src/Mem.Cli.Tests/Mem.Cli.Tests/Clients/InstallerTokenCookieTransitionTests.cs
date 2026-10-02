using System.Net;
using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Clients;

public sealed class InstallerTokenCookieTransitionTests
{
    [Fact]
    public async Task GetBackupCatalogAsync_uses_device_bearer_without_unlock_cookie_or_shared_secret_header()
    {
        const string credential = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "totalCount": 0,
                  "entries": []
                }
                """));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: null,
                Json: false,
                DeviceCredential: credential),
            handler);

        var result = await client.GetBackupCatalogAsync();

        Assert.Equal("ok", result.Status);
        Assert.Empty(handler.InstallerUnlockRequests);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/internal/host-agent/backups/catalog", request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
        Assert.Null(request.CookieHeader);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(credential, request.AuthorizationParameter);
    }

    [Fact]
    public async Task GetBackupCatalogAsync_exchanges_installer_token_for_cookie_without_shared_secret_header()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "totalCount": 0,
                  "entries": []
                }
                """));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            handler);

        var result = await client.GetBackupCatalogAsync();

        Assert.Equal("ok", result.Status);

        var unlock = Assert.Single(handler.InstallerUnlockRequests);
        Assert.Equal("POST", unlock.Method);
        Assert.Equal("/api/installer-auth/unlock", unlock.PathAndQuery);
        Assert.False(unlock.HasRetiredSharedSecretHeader);
        Assert.Equal("application/json", unlock.ContentType);

        using var unlockBody = JsonDocument.Parse(
            Assert.IsType<string>(unlock.ContentBody));
        Assert.Equal(
            "mem_cli_test_token",
            unlockBody.RootElement.GetProperty("token").GetString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/internal/host-agent/backups/catalog", request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
        Assert.Contains(
            "mem_installer_auth=mem-cli-test-auth",
            Assert.IsType<string>(request.CookieHeader));
    }

    [Fact]
    public async Task GetBackupCatalogAsync_refuses_to_issue_an_operation_without_an_installer_token()
    {
        var handler = new RecordingHttpMessageHandler();

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: null,
                Json: false),
            handler);

        var exception = await Assert.ThrowsAsync<InstallerAuthenticationException>(
            () => client.GetBackupCatalogAsync());

        Assert.Equal(
            InstallerAuthenticationFailureKind.MissingInstallerToken,
            exception.FailureKind);
        Assert.Empty(handler.InstallerUnlockRequests);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetBackupCatalogAsync_does_not_leak_a_rejected_unlock_response_body()
    {
        var handler = new RecordingHttpMessageHandler();
        handler.SetInstallerUnlockResponse(
            RecordingHttpMessageHandler.Json(
                """{ "error": "invalid_token", "detail": "do not surface this" }""",
                HttpStatusCode.Unauthorized));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            handler);

        var exception = await Assert.ThrowsAsync<InstallerAuthenticationException>(
            () => client.GetBackupCatalogAsync());

        Assert.Equal(
            InstallerAuthenticationFailureKind.InstallerTokenRejected,
            exception.FailureKind);
        Assert.Equal(401, exception.StatusCode);
        Assert.DoesNotContain(
            "invalid_token",
            exception.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(handler.Requests);
    }
}
