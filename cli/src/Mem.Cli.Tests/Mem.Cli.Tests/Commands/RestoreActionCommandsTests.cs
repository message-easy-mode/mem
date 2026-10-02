using System.Text.Json;
using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class RestoreActionCommandsTests
{
    [Fact]
    public async Task Create_json_starts_from_catalog_entry_and_returns_restore_session_identity()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.CreateOrResumeResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "create", RestoreActionTestPayloads.CatalogEntryId, "--json"],
                "create",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("validationId", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payloadDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal(RestoreActionTestPayloads.CatalogEntryId, root.GetProperty("catalogEntryId").GetString());
        Assert.Equal(RestoreActionTestPayloads.RestoreSessionId, root.GetProperty("restoreSessionId").GetString());
    }

    [Fact]
    public async Task Private_test_human_output_is_session_centric_and_surfaces_safe_evidence_summary()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.PrivateTestResponse));
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "private-test", RestoreActionTestPayloads.RestoreSessionId],
                "private-test",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM Restore Private Test", captured.StandardOutput);
        Assert.Contains($"Restore session ID: {RestoreActionTestPayloads.RestoreSessionId}", captured.StandardOutput);
        Assert.Contains("Private only:       yes", captured.StandardOutput);
        Assert.DoesNotContain("validation ID", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspacePath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/private-test",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Recreate_preflight_json_uses_required_targets_and_keeps_read_only_result_machine_safe()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.PreflightReadyResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                [
                    "restores", "recreate", "preflight", RestoreActionTestPayloads.RestoreSessionId,
                    "--target-stack", "action-stack-restored",
                    "--element-host", "chat-action.example.test",
                    "--matrix-host", "matrix-action.example.test",
                    "--requested-domain-id", "domain-01",
                    "--json"
                ],
                "recreate",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.True(root.GetProperty("canCreate").GetBoolean());
        Assert.Equal("action-stack-restored", root.GetProperty("targets").GetProperty("targetStackSlug").GetString());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/standard-recreate/preflight?targetStackSlug=action-stack-restored&requestedDomainId=domain-01&matrixHost=matrix-action.example.test&elementHost=chat-action.example.test",
            request.PathAndQuery);
    }

    [Fact]
    public async Task Recreate_execute_refuses_to_call_api_without_all_explicit_acknowledgements()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                [
                    "restores", "recreate", "execute", RestoreActionTestPayloads.RestoreSessionId,
                    "--target-stack", "action-stack-restored",
                    "--element-host", "chat-action.example.test"
                ],
                "recreate",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("--execute-production-recreate", captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Recreate_execute_json_projects_away_runtime_paths_and_container_identifiers()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.StandardRecreateVerifiedResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                [
                    "restores", "recreate", "execute", RestoreActionTestPayloads.RestoreSessionId,
                    "--target-stack", "action-stack-restored",
                    "--element-host", "chat-action.example.test",
                    "--matrix-host", "matrix-action.example.test",
                    "--operator", "Nigel",
                    "--note", "CLI action test",
                    "--execute-production-recreate",
                    "--acknowledge-creates-real-stack",
                    "--acknowledge-mutates-production-postgres",
                    "--acknowledge-mutates-npm-routes",
                    "--acknowledge-no-automatic-rollback",
                    "--json"
                ],
                "recreate",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("matrixDataPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("databaseUsername", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matrixContainerId", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("production_recreate_verified", root.GetProperty("status").GetString());
        Assert.Equal("backup-catalog", root.GetProperty("sourceKind").GetString());
        Assert.True(root.GetProperty("routes").GetProperty("publicReadinessPassed").GetBoolean());
    }

    [Fact]
    public async Task Cancel_requires_yes_and_does_not_call_api_without_confirmation()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "cancel", RestoreActionTestPayloads.RestoreSessionId],
                "cancel",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("without --yes", captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Cancel_json_uses_acknowledged_session_action_and_omits_internal_paths()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.CancelledAttemptResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "cancel", RestoreActionTestPayloads.RestoreSessionId, "--yes", "--json"],
                "cancel",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("sessionDirectoryPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supportReportPath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("cancel", root.GetProperty("action").GetString());
        Assert.Equal("cancelled", root.GetProperty("attemptStatus").GetString());
    }

    [Fact]
    public async Task Handover_complete_requires_yes_and_completes_through_canonical_session_route()
    {
        var withoutConfirmationHandler = new RecordingHttpMessageHandler();
        var options = HumanOptions();
        using (var withoutConfirmationClient = new HostAgentClient(options, withoutConfirmationHandler))
        {
            var refusal = await ConsoleOutputCapture.CaptureAsync(() =>
                RestoreCommands.RunAsync(
                    ["restores", "handover", "complete", RestoreActionTestPayloads.RestoreSessionId],
                    "handover",
                    options,
                    withoutConfirmationClient));

            Assert.Equal(1, refusal.ExitCode);
            Assert.Contains("without --yes", refusal.StandardError);
            Assert.Empty(withoutConfirmationHandler.Requests);
        }

        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.CompletedAttemptResponse));
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "handover", "complete", RestoreActionTestPayloads.RestoreSessionId, "--yes"],
                "handover",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.Contains("MEM Restore Handover Completion", captured.StandardOutput);
        Assert.Contains("Attempt status:     completed", captured.StandardOutput);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/complete-handover?acknowledgeCompletion=true",
            request.PathAndQuery);
    }


    [Fact]
    public async Task Private_test_destroy_requires_yes_and_does_not_read_or_mutate_the_workspace()
    {
        var handler = new RecordingHttpMessageHandler();
        var options = HumanOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "private-test", "destroy", RestoreActionTestPayloads.RestoreSessionId],
                "private-test",
                options,
                client));

        Assert.Equal(1, captured.ExitCode);
        Assert.Contains("without --yes", captured.StandardError);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Private_test_destroy_resolves_staging_from_workspace_and_emits_safe_json()
    {
        var retainedWorkspace = RestoreTestPayloads.RestoreWorkspaceResponse
            .Replace("\"stagingId\": \"staging-20260702-01\"", "\"stagingId\": \"staging-20260702-action-01\"", StringComparison.Ordinal)
            .Replace("\"requiresExplicitDestroy\": false", "\"requiresExplicitDestroy\": true", StringComparison.Ordinal)
            .Replace("\"stagingRuntimeStatus\": \"destroyed\"", "\"stagingRuntimeStatus\": \"retained\"", StringComparison.Ordinal)
            .Replace("\"stagingRuntimeDestroyed\": true", "\"stagingRuntimeDestroyed\": false", StringComparison.Ordinal)
            .Replace("\"destroyAvailable\": false", "\"destroyAvailable\": true", StringComparison.Ordinal)
            .Replace("\"destroyedAtUtc\": \"2026-07-02T10:05:01Z\"", "\"destroyedAtUtc\": null", StringComparison.Ordinal);

        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(retainedWorkspace),
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.PrivateTestDestroyResponse));
        var options = JsonOptions();
        using var client = new HostAgentClient(options, handler);

        var captured = await ConsoleOutputCapture.CaptureAsync(() =>
            RestoreCommands.RunAsync(
                ["restores", "private-test", "destroy", RestoreTestPayloads.RestoreSessionId, "--yes", "--json"],
                "private-test",
                options,
                client));

        Assert.Equal(0, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("workspacePath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtimePath", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("containerId", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validationId", captured.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(captured.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("destroyed", root.GetProperty("status").GetString());
        Assert.Equal(RestoreTestPayloads.RestoreSessionId, root.GetProperty("restoreSessionId").GetString());
        Assert.Equal("staging-20260702-action-01", root.GetProperty("stagingId").GetString());
        Assert.True(root.GetProperty("networkRemoved").GetBoolean());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreTestPayloads.RestoreSessionId}/workspace",
            handler.Requests[0].PathAndQuery);
        Assert.Equal(
            "/internal/host-agent/backups/verification/private-runtime/private-staging/staging-20260702-action-01/destroy",
            handler.Requests[1].PathAndQuery);
    }

    private static CliOptions HumanOptions() =>
        new(
            HostAgentUrl: "http://mem.test",
            InstallerToken: "mem_cli_test_token",
            Json: false);

    private static CliOptions JsonOptions() =>
        HumanOptions() with { Json = true };
}
