using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class RestoreCommandsTests
{
    [Fact]
    public async Task List_json_emits_canonical_restore_sessions_without_validation_identity_or_paths()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreListResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                [
                    "restores", "list",
                    "--page", "2",
                    "--page-size", "25",
                    "--search", "restored",
                    "--status", "needs-action",
                    "--target-stack", "cool-stack",
                    "--sort-by", "updated",
                    "--sort-direction", "desc",
                    "--json"
                ],
                "list",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("validationId", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storedZipPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("totalSessions").GetInt32());
        var session = root.GetProperty("sessions")[0];
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, session.GetProperty("restoreSessionId").GetString());
        Assert.True(session.GetProperty("sourceDeleted").GetBoolean());
        Assert.False(
            session.TryGetProperty("catalogEntryId", out _),
            "The shared CLI JSON serializer omits null properties. " +
            "A deleted source is represented by sourceDeleted=true and no catalogEntryId.");
    }

    [Fact]
    public async Task Inspect_human_output_keeps_deleted_source_history_session_centric()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreWorkspaceResponse));
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "inspect", RestoreTestPayloads.RestoreSessionId],
                "inspect",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM Restore Workspace", captured.StandardOutput);
        Assert.Contains($"Restore session ID: {RestoreTestPayloads.RestoreSessionId}", captured.StandardOutput);
        Assert.Contains("Display name: Deleted backup", captured.StandardOutput);
        Assert.Contains("not available (source deleted)", captured.StandardOutput);
        Assert.DoesNotContain("validation ID", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/workspace",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Evidence_json_projects_curated_workspace_evidence_without_internal_paths()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreWorkspaceResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "evidence", RestoreTestPayloads.RestoreSessionId, "--json"],
                "evidence",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("payloadDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stagingWorkspacePath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, root.GetProperty("restoreSessionId").GetString());
        Assert.Equal("private-test", root.GetProperty("evidence").GetProperty("categories")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Logs_json_uses_restore_scoped_query_and_server_projected_event_data()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreLogsResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                [
                    "restores", "logs", RestoreTestPayloads.RestoreSessionId,
                    "--page", "2",
                    "--page-size", "50",
                    "--severity", "warning",
                    "--stage", "verification",
                    "--search", "handover",
                    "--json"
                ],
                "logs",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("restore.verification.passed", root.GetProperty("logs").GetProperty("events")[0].GetProperty("eventCode").GetString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/logs?page=2&pageSize=50&severity=warning&stage=verification&search=handover",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Support_report_json_reads_existing_report_without_posting_and_hides_internal_source_key()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.SupportReportResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "support-report", RestoreTestPayloads.RestoreSessionId, "--json"],
                "support-report",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("sourceKey", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reportPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, root.GetProperty("report").GetProperty("restoreSessionId").GetString());
        Assert.True(root.GetProperty("report").GetProperty("source").GetProperty("sourceDeleted").GetBoolean());

        var request = Assert.Single(handler.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/support-report",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Evidence_human_output_keeps_deleted_source_history_safe_and_session_centric()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.RestoreWorkspaceResponse));
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "evidence", RestoreTestPayloads.RestoreSessionId],
                "evidence",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains(
            "Source backup:      Deleted backup",
            captured.StandardOutput);
        Assert.Contains(
            "Catalog ID:         not available (source deleted)",
            captured.StandardOutput);
        Assert.DoesNotContain(
            "payloadDirectoryPath",
            captured.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Support_report_human_output_hides_internal_source_key_for_deleted_source_history()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreTestPayloads.SupportReportResponse));
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "support-report", RestoreTestPayloads.RestoreSessionId],
                "support-report",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains(
            "Display name: Deleted backup",
            captured.StandardOutput);
        Assert.Contains(
            "Catalog ID:   not available (source deleted)",
            captured.StandardOutput);
        Assert.DoesNotContain(
            "internal-source-key-should-not-reach-cli-output",
            captured.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "sourceKey",
            captured.StandardOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_invalid_page_refuses_before_control_plane_call()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "list", "--page", "0", "--json"],
                "list",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("--page requires a positive integer value.", captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    private static CliOptions JsonOptions() =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: true);

    private static CliOptions HumanOptions() =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);
}
