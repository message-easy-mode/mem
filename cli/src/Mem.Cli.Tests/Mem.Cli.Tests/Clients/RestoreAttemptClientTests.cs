using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Clients;

public sealed class RestoreAttemptClientTests
{
    [Fact]
    public async Task GetRestoreAttemptsAsync_uses_canonical_paged_restore_route_and_projects_deleted_source_history()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreListResponse));

        using var client = CreateClient(handler);

        var result = await client.GetRestoreAttemptsAsync(
            new RestoreAttemptListRequest(
                Page: 2,
                PageSize: 25,
                Search: "restored",
                Status: "needs-action",
                TargetStack: "cool-stack",
                SortBy: "updated",
                SortDirection: "desc"));

        Assert.Equal("ok", result.Status);
        Assert.Equal(1, result.TotalSessions);
        var session = Assert.Single(result.Sessions);
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, session.RestoreSessionId);
        Assert.True(session.SourceDeleted);
        Assert.Null(session.CatalogEntryId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/restores?page=2&pageSize=25&search=restored&status=needs-action&targetStack=cool-stack&sortBy=updated&sortDirection=desc",
            request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task GetRestoreWorkspaceAsync_uses_restore_session_workspace_route()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreWorkspaceResponse));

        using var client = CreateClient(handler);

        var result = await client.GetRestoreWorkspaceAsync(
            RestoreTestPayloads.RestoreSessionId);

        Assert.Equal("ok", result.Status);
        var workspace = Assert.IsType<RestoreWorkspaceResponse>(result.Workspace);
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, workspace.RestoreSessionId);
        Assert.True(workspace.Source.SourceDeleted);
        Assert.Null(workspace.Source.CatalogEntryId);
        Assert.Equal("awaiting-handover", workspace.Attempt.Status);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/workspace",
            request.PathAndQuery);
    }

    [Fact]
    public async Task GetRestoreLogsAsync_uses_restore_scoped_redacted_log_route()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreLogsResponse));

        using var client = CreateClient(handler);

        var result = await client.GetRestoreLogsAsync(
            RestoreTestPayloads.RestoreSessionId,
            new RestoreLogListRequest(
                Page: 2,
                PageSize: 50,
                Severity: "warning",
                Stage: "verification",
                Search: "handover"));

        Assert.Equal("ok", result.Status);
        var logs = Assert.IsType<RestoreLogPage>(result.Logs);
        Assert.Equal(3, logs.TotalEvents);
        Assert.Equal("restore.verification.passed", Assert.Single(logs.Events).EventCode);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/logs?page=2&pageSize=50&severity=warning&stage=verification&search=handover",
            request.PathAndQuery);
    }

    [Fact]
    public async Task GetRestoreSupportReportAsync_uses_read_only_support_report_route_and_omits_internal_source_key_from_cli_projection()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.SupportReportResponse));

        using var client = CreateClient(handler);

        var result = await client.GetRestoreSupportReportAsync(
            RestoreTestPayloads.RestoreSessionId);

        Assert.Equal("ok", result.Status);
        var report = Assert.IsType<RestoreSupportReportCliProjection>(result.Report);
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, report.RestoreSessionId);
        Assert.True(report.Source.SourceDeleted);
        Assert.Null(report.Source.CatalogEntryId);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/support-report",
            request.PathAndQuery);
        Assert.Null(request.ContentBody);
    }

    private static HostAgentClient CreateClient(RecordingHttpMessageHandler handler) =>
        new(
            new CliOptions(
                HostAgentUrl: "http://mem.test",
                InstallerToken: "mem_cli_test_token",
                Json: false),
            handler);
}
