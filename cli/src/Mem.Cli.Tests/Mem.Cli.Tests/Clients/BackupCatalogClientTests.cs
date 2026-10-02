using System.Net;
using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Clients;

public sealed class BackupCatalogClientTests
{
    [Fact]
    public async Task GetBackupCatalogAsync_uses_canonical_catalog_route_and_agent_secret()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "totalCount": 1,
                  "entries": [
                    {
                      "catalogEntryId": "catalog-20260702-local-01",
                      "originKind": "local-captured",
                      "displayName": "postgres-stack backup",
                      "sourceStackSlug": "postgres-stack",
                      "sourceBackupId": "20260702-093000Z",
                      "capturedAtUtc": "2026-07-02T09:30:00Z",
                      "payloadState": "available",
                      "integrityStatus": "valid",
                      "warningCount": 0,
                      "payloadBytes": 1310720,
                      "createdAtUtc": "2026-07-02T09:30:03Z",
                      "importedAtUtc": null,
                      "materialisedAtUtc": null,
                      "payloadRemovedAtUtc": null,
                      "advisoryCount": 0
                    }
                  ]
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
        Assert.Equal(1, result.TotalCount);

        var entry = Assert.Single(result.Entries);
        Assert.Equal("catalog-20260702-local-01", entry.CatalogEntryId);
        Assert.Equal("local-captured", entry.OriginKind);
        Assert.Equal("postgres-stack", entry.SourceStackSlug);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/internal/host-agent/backups/catalog", request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
    }

    [Fact]
    public async Task DeleteBackupCatalogAsync_fails_closed_when_recent_step_up_or_role_is_required()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "error": "step_up_required",
                  "detail": "raw detail with password=NeverLogThis123 and totp=654321"
                }
                """,
                HttpStatusCode.Forbidden));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: null,
                Json: true,
                DeviceCredential: "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"),
            handler);

        var result = await client.DeleteBackupCatalogAsync(
            "catalog-step-up-required");

        Assert.Equal("error", result.Status);
        Assert.Equal("catalog-step-up-required", result.CatalogEntryId);
        var detail = Assert.IsType<string>(result.Detail);
        Assert.Contains("recent identity verification", detail);
        Assert.Contains("fails closed", detail);
        Assert.DoesNotContain("NeverLogThis123", detail);
        Assert.DoesNotContain("654321", detail);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("DELETE", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/catalog-step-up-required",
            request.PathAndQuery);
        Assert.Equal("Bearer", request.AuthorizationScheme);
    }

    [Fact]
    public async Task GetBackupCatalogDetailAsync_reads_catalog_identity_without_local_artifact_route()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "catalogEntryId": "catalog-20260702-import-01",
                  "originKind": "imported-zip",
                  "displayName": "imported-postgres.zip",
                  "sourceStackSlug": "postgres-stack-2",
                  "sourceBackupId": null,
                  "validationId": "20260702-094500Z-12345678",
                  "manifestVersion": 1,
                  "memVersion": "0.1.1",
                  "matrixServerName": "matrix.example.test",
                  "matrixHost": "matrix.example.test",
                  "elementHost": "chat.example.test",
                  "capturedAtUtc": "2026-07-02T09:45:00Z",
                  "payloadState": "available",
                  "integrityStatus": "warning",
                  "integritySummary": "One non-blocking advisory was recorded.",
                  "warningCount": 1,
                  "payloadBytes": 1153433,
                  "createdAtUtc": "2026-07-02T09:45:04Z",
                  "importedAtUtc": "2026-07-02T09:45:00Z",
                  "materialisedAtUtc": "2026-07-02T09:45:04Z",
                  "payloadRemovedAtUtc": null,
                  "payloadRemovedBy": null,
                  "advisoryCount": 1,
                  "advisories": [
                    {
                      "category": "compatibility",
                      "title": "Older export",
                      "message": "This portable export was created by an earlier MEM version."
                    }
                  ]
                }
                """));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            handler);

        var result = await client.GetBackupCatalogDetailAsync(
            "catalog-20260702-import-01");

        Assert.Equal("ok", result.Status);
        var entry = Assert.IsType<Mem.Cli.Models.BackupCatalogDetailApiResponse>(result.Entry);
        Assert.Equal("catalog-20260702-import-01", entry.CatalogEntryId);
        Assert.Equal("imported-zip", entry.OriginKind);
        Assert.Equal("20260702-094500Z-12345678", entry.ValidationId);
        var advisories = Assert.IsAssignableFrom<IReadOnlyList<Mem.Cli.Models.BackupCatalogAdvisory>>(entry.Advisories);
        Assert.Single(advisories);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/catalog/catalog-20260702-import-01",
            request.PathAndQuery);
    }

    [Fact]
    public async Task GetBackupCatalogDetailAsync_projects_http_failure_without_throwing()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "error": "backup_catalog_entry_not_found",
                  "detail": "Do not expose /var/lib/mem/backups, a raw SQL trace, or this response body."
                }
                """,
                System.Net.HttpStatusCode.NotFound));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            handler);

        var result = await client.GetBackupCatalogDetailAsync("missing-catalog-entry");

        Assert.Equal("error", result.Status);
        Assert.Null(result.Entry);
        var detail = Assert.IsType<string>(result.Detail);
        Assert.Equal(
            "The control plane request failed (HTTP 404).",
            detail);
        Assert.DoesNotContain("backup_catalog_entry_not_found", detail);
        Assert.DoesNotContain("/var/lib/mem/backups", detail);
        Assert.DoesNotContain("raw SQL trace", detail);
    }

    [Fact]
    public async Task GetBackupCatalogAsync_does_not_surface_transport_exception_text()
    {
        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            new ThrowingAfterInstallerUnlockHandler());

        var result = await client.GetBackupCatalogAsync();

        Assert.Equal("unreachable", result.Status);
        Assert.Equal(
            "The control plane could not be reached. Check the private management connection and try again.",
            result.Detail);
        Assert.DoesNotContain("private control-plane path", result.Detail);
        Assert.DoesNotContain("certificate fingerprint", result.Detail);
    }

    private sealed class ThrowingAfterInstallerUnlockHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (string.Equals(
                    request.RequestUri?.AbsolutePath,
                    "/api/installer-auth/unlock",
                    StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(
                    System.Net.HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation(
                    "Set-Cookie",
                    "mem_installer_auth=mem-cli-test-auth; Path=/; HttpOnly");

                return Task.FromResult(response);
            }

            throw new HttpRequestException(
                "private control-plane path failed; certificate fingerprint=secret-fingerprint");
        }
    }

}
