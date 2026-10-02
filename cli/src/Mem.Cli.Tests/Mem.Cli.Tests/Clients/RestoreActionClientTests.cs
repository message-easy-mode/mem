using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Models;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Clients;

public sealed class RestoreActionClientTests
{
    [Fact]
    public async Task CreateOrResumeRestoreSessionAsync_uses_catalog_entry_route_and_returns_restore_session_identity()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.CreateOrResumeResponse));
        using var client = CreateClient(handler);

        var result = await client.CreateOrResumeRestoreSessionAsync(
            RestoreActionTestPayloads.CatalogEntryId);

        Assert.Equal("ok", result.Status);
        Assert.Equal(RestoreActionTestPayloads.CatalogEntryId, result.CatalogEntryId);
        Assert.Equal(RestoreActionTestPayloads.RestoreSessionId, result.RestoreSessionId);
        Assert.True(result.RestoreAttemptCreated);
        Assert.False(result.RestoreAttemptResumed);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/catalog/{RestoreActionTestPayloads.CatalogEntryId}/restore-session",
            request.PathAndQuery);
        Assert.False(request.HasRetiredSharedSecretHeader);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task RunRestorePrivateTestAsync_uses_restore_session_route_without_validation_identity()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.PrivateTestResponse));
        using var client = CreateClient(handler);

        var result = await client.RunRestorePrivateTestAsync(
            RestoreActionTestPayloads.RestoreSessionId);

        Assert.Equal("ready", result.Status);
        Assert.Equal(RestoreActionTestPayloads.RestoreSessionId, result.RestoreSessionId);
        Assert.Equal(RestoreActionTestPayloads.CatalogEntryId, result.CatalogEntryId);
        Assert.True(result.PrivateOnly == true);
        Assert.True(result.SynapseHealthPassed == true);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/private-test",
            request.PathAndQuery);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task PreflightRestoreStandardRecreateAsync_uses_restore_session_scoped_read_only_query()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.PreflightReadyResponse));
        using var client = CreateClient(handler);

        var result = await client.PreflightRestoreStandardRecreateAsync(
            RestoreActionTestPayloads.RestoreSessionId,
            new RestoreStandardRecreatePreflightCliRequest(
                TargetStackSlug: "action-stack-restored",
                ElementHost: "chat-action.example.test",
                RequestedDomainId: "domain-01",
                MatrixHost: "matrix-action.example.test"));

        Assert.Equal("ready", result.Status);
        Assert.True(result.CanCreate);
        Assert.Equal("action-stack-restored", result.Targets?.TargetStackSlug);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/standard-recreate/preflight?targetStackSlug=action-stack-restored&requestedDomainId=domain-01&matrixHost=matrix-action.example.test&elementHost=chat-action.example.test",
            request.PathAndQuery);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task ExecuteRestoreStandardRecreateAsync_uses_explicit_acknowledgements_and_projects_away_internal_details()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.StandardRecreateVerifiedResponse));
        using var client = CreateClient(handler);

        var result = await client.ExecuteRestoreStandardRecreateAsync(
            RestoreActionTestPayloads.RestoreSessionId,
            new RestoreStandardRecreateCliRequest(
                TargetStackSlug: "action-stack-restored",
                ElementHost: "chat-action.example.test",
                RequestedDomainId: "domain-01",
                MatrixHost: "matrix-action.example.test",
                MatrixImage: "matrixdotorg/synapse:latest",
                ElementImage: "vectorim/element-web:latest",
                Operator: "Nigel",
                Note: "CLI action test",
                ExecuteProductionRecreate: true,
                AcknowledgeCreatesRealStack: true,
                AcknowledgeMutatesProductionPostgres: true,
                AcknowledgeMutatesNpmRoutes: true,
                AcknowledgeNoAutomaticRollback: true));

        Assert.Equal("production_recreate_verified", result.Status);
        Assert.Equal(RestoreActionTestPayloads.RestoreSessionId, result.RestoreSessionId);
        Assert.Equal("backup-catalog", result.SourceKind);
        Assert.Equal(RestoreActionTestPayloads.CatalogEntryId, result.CatalogEntryId);
        Assert.True(result.Database?.ImportSucceeded == true);
        Assert.True(result.Runtime?.StackRegistered == true);
        Assert.True(result.Routes?.PublicReadinessPassed == true);
        Assert.DoesNotContain("matrixDataPath", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("databaseUsername", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("matrixContainerId", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/standard-recreate?targetStackSlug=action-stack-restored&requestedDomainId=domain-01&matrixHost=matrix-action.example.test&elementHost=chat-action.example.test&matrixImage=matrixdotorg%2Fsynapse%3Alatest&elementImage=vectorim%2Felement-web%3Alatest&operatorName=Nigel&note=CLI%20action%20test&executeProductionRecreate=true&acknowledgeCreatesRealStack=true&acknowledgeMutatesProductionPostgres=true&acknowledgeMutatesNpmRoutes=true&acknowledgeNoAutomaticRollback=true",
            request.PathAndQuery);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task CancelRestoreAsync_uses_server_acknowledgement_and_safe_attempt_projection()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.CancelledAttemptResponse));
        using var client = CreateClient(handler);

        var result = await client.CancelRestoreAsync(
            RestoreActionTestPayloads.RestoreSessionId);

        Assert.Equal("ok", result.Status);
        Assert.Equal("cancel", result.Action);
        Assert.Equal("cancelled", result.AttemptStatus);
        Assert.Null(result.LastErrorSummary);
        Assert.DoesNotContain("sessionDirectoryPath", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/cancel?acknowledgeCancel=true",
            request.PathAndQuery);
        Assert.Null(request.ContentBody);
    }

    [Fact]
    public async Task CompleteRestoreHandoverAsync_uses_server_acknowledgement_and_safe_attempt_projection()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.CompletedAttemptResponse));
        using var client = CreateClient(handler);

        var result = await client.CompleteRestoreHandoverAsync(
            RestoreActionTestPayloads.RestoreSessionId);

        Assert.Equal("ok", result.Status);
        Assert.Equal("complete-handover", result.Action);
        Assert.Equal("completed", result.AttemptStatus);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            $"/internal/host-agent/backups/restores/{RestoreActionTestPayloads.RestoreSessionId}/complete-handover?acknowledgeCompletion=true",
            request.PathAndQuery);
        Assert.Null(request.ContentBody);
    }


    [Fact]
    public async Task DestroyPrivateTestStagingAsync_uses_low_level_destroy_route_and_projects_only_cleanup_summary()
    {
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(RestoreActionTestPayloads.PrivateTestDestroyResponse));
        using var client = CreateClient(handler);

        var result = await client.DestroyPrivateTestStagingAsync(
            RestoreActionTestPayloads.RestoreSessionId,
            "staging-20260702-action-01");

        Assert.Equal("destroyed", result.Status);
        Assert.Equal(RestoreActionTestPayloads.RestoreSessionId, result.RestoreSessionId);
        Assert.Equal("staging-20260702-action-01", result.StagingId);
        Assert.True(result.NetworkRemoved == true);
        Assert.DoesNotContain("workspacePath", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("containerId", System.Text.Json.JsonSerializer.Serialize(result), StringComparison.OrdinalIgnoreCase);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(
            "/internal/host-agent/backups/verification/private-runtime/private-staging/staging-20260702-action-01/destroy",
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
