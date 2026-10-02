using System.Net;
using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Clients;

public sealed class MaintenanceClientTests
{
    [Fact]
    public async Task GetRuntimeReconciliationReportAsync_uses_read_only_reconciliation_route()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "needs_attention",
                  "checkedAtUtc": "2026-07-09T05:10:00Z",
                  "summary": {
                    "activeStackCount": 1,
                    "destroyedStackHistoryCount": 40,
                    "activeRouteCount": 2,
                    "npmProxyHostCount": 15,
                    "memManagedNpmProxyHostCount": 15,
                    "orphanedNpmProxyHostCount": 13,
                    "manifestCount": 1,
                    "activeDatabaseRowsWithoutManifestCount": 0,
                    "manifestWithoutActiveDatabaseRowCount": 0
                  },
                  "activeRoutes": [],
                  "orphanedNpmProxyHosts": [
                    {
                      "proxyHostId": 59,
                      "domainNames": ["chat-old-stack.deltabox.dev"],
                      "forwardHost": "mem-element-old-stack",
                      "forwardPort": 80,
                      "enabled": true,
                      "nginxOnline": true,
                      "looksLikeMemStackRoute": true,
                      "matchesActiveRoute": false,
                      "matchedActiveRouteHosts": [],
                      "reason": "orphaned"
                    }
                  ],
                  "warnings": [],
                  "detail": "Runtime reconciliation found read-only evidence that needs operator review."
                }
                """));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: null,
                Json: true,
                DeviceCredential: "device-token"),
            handler);

        var result = await client.GetRuntimeReconciliationReportAsync();

        Assert.Equal("needs_attention", result.Status);
        Assert.Equal(13, result.Summary.OrphanedNpmProxyHostCount);
        Assert.Single(result.OrphanedNpmProxyHosts);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/admin/maintenance/runtime-reconciliation",
            request.PathAndQuery);
        Assert.Equal("Bearer", request.AuthorizationScheme);
    }

    [Fact]
    public async Task DeleteOrphanedNpmProxyHostsAsync_posts_selected_ids_and_confirmation()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "status": "completed",
                  "checkedAtUtc": "2026-07-09T05:12:00Z",
                  "requestedProxyHostIds": [59],
                  "deletedCount": 1,
                  "alreadyMissingCount": 0,
                  "skippedCount": 0,
                  "failedCount": 0,
                  "results": [
                    {
                      "proxyHostId": 59,
                      "domainNames": ["chat-old-stack.deltabox.dev"],
                      "status": "deleted",
                      "reason": "Deleted orphaned NPM proxy host."
                    }
                  ],
                  "detail": "Selected orphaned NPM proxy host cleanup completed."
                }
                """));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: null,
                Json: true,
                DeviceCredential: "device-token"),
            handler);

        var result = await client.DeleteOrphanedNpmProxyHostsAsync(
            new RuntimeReconciliationNpmProxyHostCleanupRequest(
                [59],
                "DELETE ORPHANED NPM HOSTS"));

        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.DeletedCount);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            "/internal/host-agent/admin/maintenance/runtime-reconciliation/npm-proxy-hosts/delete",
            request.PathAndQuery);
        Assert.Equal("application/json", request.ContentType);
        Assert.Contains("\"proxyHostIds\":[59]", request.ContentBody);
        Assert.Contains("DELETE ORPHANED NPM HOSTS", request.ContentBody);
        Assert.Equal("Bearer", request.AuthorizationScheme);
    }

    [Fact]
    public async Task DeleteOrphanedNpmProxyHostsAsync_fails_closed_when_step_up_required()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "error": "step_up_required",
                  "detail": "Fresh identity verification is required before this action."
                }
                """,
                HttpStatusCode.Forbidden));

        using var client = new HostAgentClient(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: null,
                Json: true,
                DeviceCredential: "device-token"),
            handler);

        var result = await client.DeleteOrphanedNpmProxyHostsAsync(
            new RuntimeReconciliationNpmProxyHostCleanupRequest(
                [59],
                "DELETE ORPHANED NPM HOSTS"));

        Assert.Equal("error", result.Status);
        Assert.Equal(1, result.FailedCount);
        Assert.Contains("step_up_required", result.Detail);
    }
}
