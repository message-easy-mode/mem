using System.Text.Json;
using HostAgent.Commands;
using HostAgent.Runtime.Backups.Contracts;
using Mem.Localization;

namespace HostAgent.Tests.Runtime.Backups.Contracts;

public sealed class HostAgentStructuredMessagesTests
{
    [Fact]
    public void Backup_catalog_not_found_uses_a_stable_semantic_code_and_catalog_argument()
    {
        var message = HostAgentStructuredMessages.BackupCatalogEntryNotFound(
            "bkp_20260703-071259Z-example");

        Assert.Equal(
            MemMessageCodes.BackupCatalog.EntryNotFound,
            message.Code);
        Assert.Equal(
            "bkp_20260703-071259Z-example",
            message.Arguments[MemMessageArgumentNames.CatalogEntryId]);
    }

    [Fact]
    public void Backup_catalog_unavailable_includes_only_safe_source_identity_arguments()
    {
        var message = HostAgentStructuredMessages.BackupCatalogEntryUnavailable(
            "bkp_20260703-071259Z-example",
            "missing");

        Assert.Equal(
            MemMessageCodes.BackupCatalog.EntryUnavailable,
            message.Code);
        Assert.Equal(
            "bkp_20260703-071259Z-example",
            message.Arguments[MemMessageArgumentNames.CatalogEntryId]);
        Assert.Equal(
            "missing",
            message.Arguments[MemMessageArgumentNames.PayloadState]);
        Assert.Equal(2, message.Arguments.Count);
    }

    [Fact]
    public void Private_test_started_preserves_the_existing_event_code_and_uses_named_details()
    {
        var message = HostAgentStructuredMessages.RestorePrivateTestStarted(
            "20260703-071259Z-example",
            "backup-catalog",
            "bkp_20260703-071259Z-example");

        Assert.Equal(
            "restore.private-test.started",
            message.Code);
        Assert.Equal(
            "20260703-071259Z-example",
            message.Arguments[MemMessageArgumentNames.RestoreSessionId]);
        Assert.Equal(
            "backup-catalog",
            message.Arguments[MemMessageArgumentNames.SourceKind]);
        Assert.Equal(
            "bkp_20260703-071259Z-example",
            message.Arguments[MemMessageArgumentNames.CatalogEntryId]);
    }

    [Fact]
    public void Error_response_preserves_legacy_fields_and_appends_message_only_when_supplied()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Serialize(
            new HostAgentErrorResponse(
                Error: "restore_attempt_not_found",
                Detail: "Restore attempt was not found."),
            options);

        using var legacyDocument = JsonDocument.Parse(legacy);
        Assert.Equal(
            "restore_attempt_not_found",
            legacyDocument.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "Restore attempt was not found.",
            legacyDocument.RootElement.GetProperty("detail").GetString());
        Assert.False(legacyDocument.RootElement.TryGetProperty("message", out _));

        var structured = JsonSerializer.Serialize(
            new HostAgentErrorResponse(
                Error: "restore_attempt_not_found",
                Detail: "Restore attempt was not found.",
                Message: HostAgentStructuredMessages.RestoreAttemptNotFound(
                    "20260703-071259Z-example")),
            options);

        using var structuredDocument = JsonDocument.Parse(structured);
        var message = structuredDocument.RootElement.GetProperty("message");
        Assert.Equal(
            MemMessageCodes.Restore.AttemptNotFound,
            message.GetProperty("code").GetString());
        Assert.Equal(
            "20260703-071259Z-example",
            message.GetProperty("arguments")
                .GetProperty(MemMessageArgumentNames.RestoreSessionId)
                .GetString());
    }
}
